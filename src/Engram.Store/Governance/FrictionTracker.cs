using System;
using System.Collections.Generic;
using System.Linq;
using Engram.Store.Events;

namespace Engram.Store.Governance;

/// <summary>
/// Monitors user interaction friction (dismissals, cancellations) and dynamically scales up
/// silence/confidence thresholds to prevent fatigue.
/// </summary>
public class FrictionTracker : IDisposable
{
    private readonly GovernanceConfig _config;
    private readonly LongitudinalTrustModel _trustModel;
    private readonly object _lock = new();
    private int _consecutiveFrictionCount;
    private readonly double _baseConfidenceThreshold;
    private readonly List<DateTimeOffset> _frictionTimestamps = new();
    private DateTimeOffset _silencedUntil = DateTimeOffset.MinValue;
    private readonly IDisposable? _subscription;

    public Func<DateTimeOffset> TimeProvider { get; set; } = () => DateTimeOffset.UtcNow;

    public int ConsecutiveFrictionCount
    {
        get { lock (_lock) return _consecutiveFrictionCount; }
    }

    public bool IsSilenced
    {
        get { lock (_lock) return TimeProvider() < _silencedUntil; }
    }

    public DateTimeOffset SilencedUntil
    {
        get { lock (_lock) return _silencedUntil; }
    }

    public double AnnoyanceScore
    {
        get { lock (_lock) return _trustModel.AnnoyanceScore; }
    }

    public double HistoricalTrustIndex
    {
        get { lock (_lock) return _trustModel.HistoricalTrustIndex; }
    }

    public FrictionTracker(GovernanceConfig config, LongitudinalTrustModel trustModel, IEventBus? eventBus = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _trustModel = trustModel ?? throw new ArgumentNullException(nameof(trustModel));
        _baseConfidenceThreshold = config.MinConfidenceToEscalate;

        if (eventBus != null)
        {
            _subscription = eventBus.SubscribeAll(HandleEvent);
        }
    }

    private void HandleEvent(EventEnvelope envelope)
    {
        if (envelope.EventType == EventTypes.FrictionUserDismissed ||
            envelope.EventType == EventTypes.FrictionActionCancelled ||
            envelope.EventType == EventTypes.FrictionTrustOverride)
        {
            double intensity = 1.0;
            if (envelope.Metadata.TryGetValue("intensity", out var intensityStr) && double.TryParse(intensityStr, out var val))
            {
                intensity = val;
            }
            RecordFriction(intensity);
        }
    }

    /// <summary>
    /// Log an occurrence of user friction (e.g. cancellation/dismissal).
    /// </summary>
    public void RecordFriction(double intensity = 1.0)
    {
        lock (_lock)
        {
            _consecutiveFrictionCount++;
            _trustModel.RecordAnnoyance(intensity);

            // Scale up silence threshold (MinConfidenceToEscalate) dynamically by 0.05 per consecutive friction, up to a max of 0.95
            _config.MinConfidenceToEscalate = Math.Min(0.95, _config.MinConfidenceToEscalate + (intensity * 0.05));

            var now = TimeProvider();
            _frictionTimestamps.Add(now);

            // Keep only the last 24 hours of timestamps
            _frictionTimestamps.RemoveAll(t => t < now.AddHours(-24));

            // Check if >3 dismissals in 6 hours
            var sixHoursAgo = now.AddHours(-6);
            var frictionInSixHours = _frictionTimestamps.Count(t => t >= sixHoursAgo);

            if (frictionInSixHours > 3)
            {
                // Silencing non-essential prompts for 48 hours
                _silencedUntil = now.AddDays(2);
                
                // Scale up safety constitution restraint multipliers (MinConfidenceToEscalate to max)
                _config.MinConfidenceToEscalate = 0.95;
            }
        }
    }

    /// <summary>
    /// Log a successful, non-friction intervention interaction.
    /// </summary>
    public void RecordSuccess()
    {
        lock (_lock)
        {
            _consecutiveFrictionCount = 0;
            _silencedUntil = DateTimeOffset.MinValue;

            // Slowly restore/decay the confidence gate back to the baseline configured level
            _config.MinConfidenceToEscalate = Math.Max(_baseConfidenceThreshold, _config.MinConfidenceToEscalate - 0.02);
        }
    }

    public void Dispose()
    {
        _subscription?.Dispose();
        GC.SuppressFinalize(this);
    }
}
