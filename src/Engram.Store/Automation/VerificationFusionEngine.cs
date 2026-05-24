using System;
using System.Collections.Generic;
using System.Linq;

namespace Engram.Store.Automation;

public enum VerificationTier
{
    Structured,      // Layer 1 - Ground-truth
    Accessibility,   // Layer 2 - OS accessibility trees
    Ocr,             // Layer 3 - Text perception
    Heuristics       // Layer 4 - Screenshot layouts
}

public class VerificationSignal
{
    public VerificationTier Tier { get; set; }
    public bool Outcome { get; set; }
    public double SignalConfidence { get; set; }
    public string Details { get; set; } = string.Empty;
}

public class VerificationFusionResult
{
    public bool IsVerified { get; set; }
    public double VerificationConfidence { get; set; }
    public double AutonomyConfidencePenalty { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class VerificationFusionEngine
{
    private static readonly Dictionary<VerificationTier, double> TierWeights = new()
    {
        { VerificationTier.Structured, 1.0 },
        { VerificationTier.Accessibility, 0.8 },
        { VerificationTier.Ocr, 0.6 },
        { VerificationTier.Heuristics, 0.3 }
    };

    public VerificationFusionResult Fuse(List<VerificationSignal> signals)
    {
        if (signals == null || signals.Count == 0)
        {
            return new VerificationFusionResult
            {
                IsVerified = false,
                VerificationConfidence = 0.0,
                AutonomyConfidencePenalty = 0.5,
                Message = "No verification signals available"
            };
        }

        // 1. Calculate weighted outcomes
        double totalWeight = 0.0;
        double weightedSuccess = 0.0;

        foreach (var sig in signals)
        {
            double weight = TierWeights[sig.Tier] * sig.SignalConfidence;
            totalWeight += weight;
            if (sig.Outcome)
            {
                weightedSuccess += weight;
            }
        }

        double overallConfidence = totalWeight > 0.0 ? weightedSuccess / totalWeight : 0.0;

        // 2. Determine verification success (threshold at 0.5)
        bool verified = overallConfidence >= 0.5;

        // 3. Autonomy Confidence Penalty
        // Lower verification confidence must reduce overall autonomy confidence.
        // If highest tier verified is OCR (0.6) or Heuristics (0.3), penalize more.
        double highestTierConfidence = 0.0;
        var successfulSignals = signals.Where(s => s.Outcome).ToList();
        if (successfulSignals.Any())
        {
            highestTierConfidence = successfulSignals.Max(s => TierWeights[s.Tier]);
        }

        double autonomyPenalty = 0.0;
        if (highestTierConfidence < 0.8)
        {
            // Highest verification is Layer 3 (OCR) or Layer 4 (Heuristics) -> penalty applies
            autonomyPenalty = 0.4 * (1.0 - highestTierConfidence);
        }

        // 4. Resolve conflicts
        string msg = "Verification succeeded";
        if (signals.Any(s => s.Outcome) && signals.Any(s => !s.Outcome))
        {
            var failedTiers = string.Join(", ", signals.Where(s => !s.Outcome).Select(s => s.Tier.ToString()));
            var successTiers = string.Join(", ", signals.Where(s => s.Outcome).Select(s => s.Tier.ToString()));
            msg = $"Conflict detected. Successful: [{successTiers}]. Failed: [{failedTiers}]. Weighted Confidence: {overallConfidence:F2}";
        }
        else if (!verified)
        {
            msg = "Verification failed across all channels";
        }

        return new VerificationFusionResult
        {
            IsVerified = verified,
            VerificationConfidence = overallConfidence,
            AutonomyConfidencePenalty = autonomyPenalty,
            Message = msg
        };
    }
}
