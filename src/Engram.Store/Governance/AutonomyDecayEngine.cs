using System;

namespace Engram.Store.Governance;

/// <summary>
/// AutonomyDecayEngine — dynamically regresses/softens active autonomy level
/// when user overrides, dismissals, and cancellations increase.
/// Slowly decays back to baseline preference as successful non-intrusive operations occur.
/// </summary>
public class AutonomyDecayEngine
{
    private readonly GovernanceConfig _config;
    private double _frictionScore = 0.0;
    private readonly object _lock = new();

    public AutonomyDecayEngine(GovernanceConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public double FrictionScore
    {
        get
        {
            lock (_lock) return _frictionScore;
        }
    }

    /// <summary>
    /// Record user friction (e.g. override, cancel, dismissal) to decay autonomy.
    /// </summary>
    public void RecordFriction(double intensity = 1.0)
    {
        lock (_lock)
        {
            _frictionScore = Math.Min(5.0, _frictionScore + intensity);
        }
    }

    /// <summary>
    /// Record successful non-friction operation to slowly recover towards baseline.
    /// </summary>
    public void RecordSuccess()
    {
        lock (_lock)
        {
            _frictionScore = Math.Max(0.0, _frictionScore - 0.5);
        }
    }

    /// <summary>
    /// Determine modulated autonomy after decay.
    /// </summary>
    public AutonomyLevel DetermineDecayedAutonomy(AutonomyLevel currentLevel)
    {
        lock (_lock)
        {
            if (_frictionScore >= 3.0)
            {
                return AutonomyLevel.Low;
            }
            if (_frictionScore >= 1.5)
            {
                return currentLevel switch
                {
                    AutonomyLevel.Aggressive => AutonomyLevel.Medium,
                    _ => AutonomyLevel.Low
                };
            }
            return currentLevel;
        }
    }
}
