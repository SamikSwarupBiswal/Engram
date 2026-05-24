using System;
using System.Collections.Generic;

namespace Engram.Store.Automation;

public class VerificationSignals
{
    public bool? StructuredApiVerified { get; set; }
    public bool? FilesystemVerified { get; set; }
    public bool? AccessibilityVerified { get; set; }
    public bool? DomVerified { get; set; }
    public bool? OcrVerified { get; set; }
    public bool? HeuristicVisualVerified { get; set; }
}

public class VerificationConsensusEngine
{
    private const double WeightStructuredApi = 1.0;
    private const double WeightFilesystem = 0.95;
    private const double WeightAccessibility = 0.85;
    private const double WeightDom = 0.82;
    private const double WeightOcr = 0.65;
    private const double WeightHeuristicVisual = 0.4;

    public double CalculateRealityConfidence(VerificationSignals signals)
    {
        if (signals == null) return 0.0;

        double weightedSum = 0.0;
        double weightTotal = 0.0;
        int checkCount = 0;
        int failCount = 0;

        void ProcessSignal(bool? verified, double weight)
        {
            if (verified.HasValue)
            {
                checkCount++;
                weightTotal += weight;
                if (verified.Value)
                {
                    weightedSum += weight;
                }
                else
                {
                    failCount++;
                }
            }
        }

        ProcessSignal(signals.StructuredApiVerified, WeightStructuredApi);
        ProcessSignal(signals.FilesystemVerified, WeightFilesystem);
        ProcessSignal(signals.AccessibilityVerified, WeightAccessibility);
        ProcessSignal(signals.DomVerified, WeightDom);
        ProcessSignal(signals.OcrVerified, WeightOcr);
        ProcessSignal(signals.HeuristicVisualVerified, WeightHeuristicVisual);

        if (checkCount == 0)
        {
            return 0.0;
        }

        double rawConfidence = weightedSum / weightTotal;

        // Weighted Epistemic Conservatism:
        // 1. If any checked signal fails, penalize confidence heavily.
        if (failCount > 0)
        {
            rawConfidence *= Math.Pow(0.5, failCount);
        }

        // 2. High-reliability gap penalty:
        // If we relied only on weak signals (OCR, Visual) and omitted high-reliability checks
        // that are relevant (e.g. structured API or filesystem checks), penalize the result.
        bool hasStrongChecks = signals.StructuredApiVerified.HasValue || 
                               signals.FilesystemVerified.HasValue || 
                               signals.AccessibilityVerified.HasValue;
        bool hasOnlyWeakChecks = !hasStrongChecks && (signals.OcrVerified.HasValue || signals.HeuristicVisualVerified.HasValue);

        if (hasOnlyWeakChecks)
        {
            rawConfidence *= 0.65; // Cap confidence if only weak signals are used
        }

        return Math.Clamp(rawConfidence, 0.0, 1.0);
    }
}
