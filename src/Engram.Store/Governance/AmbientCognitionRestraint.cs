using System;
using System.Collections.Generic;

namespace Engram.Store.Governance;

/// <summary>
/// Gathers user ambient interaction states and restrains suggestions to avoid cognitive fatigue.
/// </summary>
public class AmbientCognitionRestraint
{
    private readonly GovernanceConfig _config;
    private int _dailyInterventionsCount;
    private DateTime _lastResetDate = DateTime.UtcNow.Date;
    private readonly object _lock = new();

    public AmbientCognitionRestraint(GovernanceConfig config)
    {
        _config = config ?? new GovernanceConfig();
    }

    /// <summary>
    /// Checks if we have exceeded the daily budget of interventions.
    /// </summary>
    public bool CheckDailyBudget()
    {
        lock (_lock)
        {
            var today = DateTime.UtcNow.Date;
            if (today != _lastResetDate)
            {
                _dailyInterventionsCount = 0;
                _lastResetDate = today;
            }

            return _dailyInterventionsCount < _config.MaxDailyInterventions;
        }
    }

    /// <summary>
    /// Record that an intervention occurred, incrementing the daily budget consumption.
    /// </summary>
    public void RecordIntervention()
    {
        lock (_lock)
        {
            var today = DateTime.UtcNow.Date;
            if (today != _lastResetDate)
            {
                _dailyInterventionsCount = 0;
                _lastResetDate = today;
            }
            _dailyInterventionsCount++;
        }
    }

    /// <summary>
    /// AttentionRespectModel: Postpones non-essential alerts if user is deeply focused
    /// or actively task-switching/typing.
    /// </summary>
    public bool ShouldSuppressDueToActivity(string behavioralMode, double windowSwitchRatePerMin, bool isTyping)
    {
        // Suppress if typing or in flow state (deep_work)
        if (isTyping) return true;
        if (string.Equals(behavioralMode, "deep_work", StringComparison.OrdinalIgnoreCase)) return true;

        // Suppress if rapid window switching (indicating frantic search or context overload)
        if (windowSwitchRatePerMin > 10.0) return true;

        return false;
    }

    /// <summary>
    /// SilenceConfidenceSystem: Keeps the system quiet on low-confidence assertions.
    /// </summary>
    public bool CheckConfidenceGate(double confidence, bool isUrgent)
    {
        if (isUrgent)
        {
            return confidence >= 0.5; // Lower threshold for urgent actions
        }
        return confidence >= _config.MinConfidenceToEscalate;
    }

    /// <summary>
    /// CognitiveLoadEstimator: Determines appropriate response verbosity based on user load.
    /// </summary>
    public string EstimateVerbosity(double taskSwitchRate, double appLoadFactor)
    {
        double load = (taskSwitchRate * 0.6) + (appLoadFactor * 0.4);
        if (load > 7.0)
        {
            return "Concise"; // Busy, keep feedback minimal
        }
        if (load > 3.0)
        {
            return "Standard";
        }
        return "Detailed"; // Calm context, safe to provide deeper insights
    }
}
