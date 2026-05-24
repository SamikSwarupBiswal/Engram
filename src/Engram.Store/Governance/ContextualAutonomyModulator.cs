using System;

namespace Engram.Store.Governance;

/// <summary>
/// Dynamic situational autonomy elasticity.
/// Temporarily softens active autonomy downward when the user is multitasking rapidly
/// or actively typing (focus instability), preventing annoying interventions.
/// </summary>
public class ContextualAutonomyModulator
{
    private readonly GovernanceConfig _config;

    public ContextualAutonomyModulator(GovernanceConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// Soften baseline autonomy if multitasking velocity is high or user is in a typing burst.
    /// </summary>
    public AutonomyLevel DetermineModulatedAutonomy(int multitaskingVelocity, bool isTypingBurst = false)
    {
        var baseline = _config.BaselineAutonomy;

        // If multitasking velocity > 6 (more than 6 window switches in 2 mins) or typing burst:
        if (multitaskingVelocity > 6 || isTypingBurst)
        {
            return baseline switch
            {
                AutonomyLevel.Aggressive => AutonomyLevel.Medium,
                AutonomyLevel.Medium => AutonomyLevel.Low,
                _ => AutonomyLevel.Low
            };
        }

        return baseline;
    }
}
