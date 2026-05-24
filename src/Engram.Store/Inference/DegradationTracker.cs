using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Engram.Store.Inference;

/// <summary>
/// Monitors and registers active degradations, capability surface limits,
/// capability hysteresis, and pathology recovery curves.
/// </summary>
public sealed class DegradationTracker
{
    private static readonly Lazy<DegradationTracker> LazyInstance = new(() => new DegradationTracker());
    public static DegradationTracker Instance => LazyInstance.Value;

    private readonly ConcurrentDictionary<string, bool> _activeDegradations = new();
    private readonly ConcurrentDictionary<string, string> _capabilityDetails = new();
    private readonly ConcurrentDictionary<string, DateTime> _recoveryCooldowns = new();
    private readonly ConcurrentDictionary<string, double> _distrustLevels = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastFailureTimes = new();

    private const double DistrustDecayConstant = 0.005; // Distrust decay factor lambda per second
    private static readonly TimeSpan HysteresisCooldown = TimeSpan.FromMinutes(5);

    private readonly object _lock = new();

    public DegradationTracker()
    {
    }

    /// <summary>
    /// Registers a degradation status.
    /// </summary>
    public void SetDegradation(string key, bool active, string? detail = null)
    {
        lock (_lock)
        {
            if (active)
            {
                _activeDegradations[key] = true;
                if (!string.IsNullOrEmpty(detail))
                {
                    _capabilityDetails[key] = detail;
                }
                // When entering degradation, set initial distrust to 1.0 (maximum caution)
                _distrustLevels[key] = 1.0;
                _lastFailureTimes[key] = DateTime.UtcNow;
                _recoveryCooldowns.TryRemove(key, out _);
            }
            else
            {
                // To prevent state oscillation, apply capability hysteresis cooldown before removing
                if (_activeDegradations.ContainsKey(key))
                {
                    if (!_recoveryCooldowns.ContainsKey(key))
                    {
                        _recoveryCooldowns[key] = DateTime.UtcNow.Add(HysteresisCooldown);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Checks if a degradation key is currently active, taking capability hysteresis and distrust decay into account.
    /// </summary>
    public bool IsDegraded(string key)
    {
        lock (_lock)
        {
            if (_activeDegradations.TryGetValue(key, out var active) && active)
            {
                // Check if distrust has decayed below recovery threshold
                if (_lastFailureTimes.TryGetValue(key, out var lastFailure))
                {
                    var distrust = GetDistrustLevel(key, lastFailure);
                    if (distrust < 0.10)
                    {
                        // Distrust decayed enough to recover!
                        _activeDegradations.TryRemove(key, out _);
                        _capabilityDetails.TryRemove(key, out _);
                        _recoveryCooldowns.TryRemove(key, out _);
                        _distrustLevels.TryRemove(key, out _);
                        _lastFailureTimes.TryRemove(key, out _);
                        return false;
                    }
                }

                // If in cooldown, check if the hysteresis timer has elapsed
                if (_recoveryCooldowns.TryGetValue(key, out var end))
                {
                    if (DateTime.UtcNow >= end)
                    {
                        _activeDegradations.TryRemove(key, out _);
                        _capabilityDetails.TryRemove(key, out _);
                        _recoveryCooldowns.TryRemove(key, out _);
                        _distrustLevels.TryRemove(key, out _);
                        _lastFailureTimes.TryRemove(key, out _);
                        return false;
                    }
                    return true;
                }
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Evaluates the pathology recovery curve for a specific environmental failure.
    /// Distrust decays exponentially over time: D(t) = D0 * e^(-lambda * dt)
    /// </summary>
    public double GetDistrustLevel(string key, DateTime lastFailureTime)
    {
        if (!_distrustLevels.TryGetValue(key, out var d0))
            return 0.0;

        var elapsedSeconds = (DateTime.UtcNow - lastFailureTime).TotalSeconds;
        if (elapsedSeconds < 0) return d0;

        var currentDistrust = d0 * Math.Exp(-DistrustDecayConstant * elapsedSeconds);

        if (currentDistrust < 0.01)
        {
            _distrustLevels.TryRemove(key, out _);
            return 0.0;
        }

        return currentDistrust;
    }

    /// <summary>
    /// Gets the capability details payload for the active degradations.
    /// </summary>
    public IReadOnlyDictionary<string, string> GetCapabilityDetails()
    {
        var activeDetails = new Dictionary<string, string>();
        foreach (var key in _activeDegradations.Keys)
        {
            if (IsDegraded(key))
            {
                activeDetails[key] = _capabilityDetails.GetValueOrDefault(key, "Degraded");
            }
        }
        return activeDetails;
    }

    /// <summary>
    /// Resolves the Environmental Confidence Score based on current active degradations.
    /// Defaults to 1.0 (full confidence). Drops as fallbacks and warnings trigger.
    /// </summary>
    public double GetEnvironmentalConfidence()
    {
        double confidence = 1.0;

        // Apply product of active degradations' confidence multipliers
        if (IsDegraded("WebView2FallbackActive"))
        {
            confidence *= 0.62; // WebView2 fallback confidence
        }
        if (IsDegraded("HighDpiOcrDegraded"))
        {
            confidence *= 0.51; // OCR high DPI degradation
        }
        if (IsDegraded("WakeStabilizing"))
        {
            confidence *= 0.71; // Wake stabilization window
        }
        if (IsDegraded("SafeModeActive"))
        {
            confidence *= 0.0;  // Safe-Mode block
        }
        if (IsDegraded("QuarantineActive"))
        {
            confidence *= 0.25; // Quarantine block
        }
        if (IsDegraded("DegradedActive"))
        {
            confidence *= 0.75; // Degraded block
        }

        return Math.Clamp(confidence, 0.0, 1.0);
    }

    /// <summary>
    /// Forcefully resets a degradation state bypassing cooldown timers (for testing/recovery console).
    /// </summary>
    public void ResetDegradation(string key)
    {
        lock (_lock)
        {
            _activeDegradations.TryRemove(key, out _);
            _capabilityDetails.TryRemove(key, out _);
            _recoveryCooldowns.TryRemove(key, out _);
            _distrustLevels.TryRemove(key, out _);
            _lastFailureTimes.TryRemove(key, out _);
        }
    }
}
