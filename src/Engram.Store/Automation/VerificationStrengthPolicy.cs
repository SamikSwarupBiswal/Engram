using System;

namespace Engram.Store.Automation;

public enum RiskLevel
{
    LowRisk,
    MediumRisk,
    HighRisk,
    ExtremelyHigh,
    Maximal
}

public class VerificationStrengthPolicy
{
    public double GetRequiredCertainty(RiskLevel risk)
    {
        return risk switch
        {
            RiskLevel.LowRisk => 0.3,
            RiskLevel.MediumRisk => 0.6,
            RiskLevel.HighRisk => 0.8,
            RiskLevel.ExtremelyHigh => 0.9,
            RiskLevel.Maximal => 1.0,
            _ => 0.8
        };
    }

    public bool MeetsVerificationRequirements(RiskLevel risk, double confidence, VerificationSignals signals)
    {
        if (signals == null) return false;

        double required = GetRequiredCertainty(risk);
        if (confidence < required)
        {
            return false;
        }

        // Epistemic requirements per risk level:
        switch (risk)
        {
            case RiskLevel.HighRisk:
                // Needs at least one high-reliability verified source
                return signals.StructuredApiVerified == true || 
                       signals.FilesystemVerified == true || 
                       signals.AccessibilityVerified == true;

            case RiskLevel.ExtremelyHigh:
                // Needs dual-source verification amongst high-reliability signals
                int highReliabilityCount = 0;
                if (signals.StructuredApiVerified == true) highReliabilityCount++;
                if (signals.FilesystemVerified == true) highReliabilityCount++;
                if (signals.AccessibilityVerified == true) highReliabilityCount++;
                return highReliabilityCount >= 2;

            case RiskLevel.Maximal:
                // Needs maximum verification: Structured API and Filesystem/Accessibility verified
                return signals.StructuredApiVerified == true && 
                       (signals.FilesystemVerified == true || signals.AccessibilityVerified == true);

            default:
                return true;
        }
    }
}
