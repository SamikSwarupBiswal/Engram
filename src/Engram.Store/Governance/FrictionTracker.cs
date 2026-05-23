using System;

namespace Engram.Store.Governance;

/// <summary>
/// Monitors user interaction friction (dismissals, cancellations) and dynamically scales up
/// silence/confidence thresholds to prevent fatigue.
/// </summary>
public class FrictionTracker
{
    private readonly GovernanceConfig _config;
    private readonly LongitudinalTrustModel _trustModel;
    private readonly object _lock = new();
    private int _consecutiveFrictionCount;
    private readonly double _baseConfidenceThreshold;

    public int ConsecutiveFrictionCount
    {
        get { lock (_lock) return _consecutiveFrictionCount; }
    }

    public FrictionTracker(GovernanceConfig config, LongitudinalTrustModel trustModel)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _trustModel = trustModel ?? throw new ArgumentNullException(nameof(trustModel));
        _baseConfidenceThreshold = config.MinConfidenceToEscalate;
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

            // Slowly restore/decay the confidence gate back to the baseline configured level
            _config.MinConfidenceToEscalate = Math.Max(_baseConfidenceThreshold, _config.MinConfidenceToEscalate - 0.02);
        }
    }
}
