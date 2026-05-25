using System;
using System.Collections.Generic;

namespace Engram.Store.Automation;

public class TemporalExecutionDegradationModel
{
    private const double DecayPerMinute = 0.02;
    private const double DecayPerStep = 0.01;

    public double ComputeTemporalDecayFactor(string workflowId, TimeSpan elapsed, int stepCount)
    {
        return ComputeTemporalDecayFactor(workflowId, elapsed, stepCount, 0);
    }

    public double ComputeTemporalDecayFactor(string workflowId, TimeSpan elapsed, int stepCount, int uncertaintyEventCount)
    {
        var vector = ComputeFatigueVector(workflowId, elapsed, stepCount, null, 0, 0, 0.0);
        vector.VerificationErosion = Math.Min(1.0, uncertaintyEventCount * 0.05);
        return vector.GetAggregateFatigueIndex();
    }

    public EpistemicFatigueVector ComputeFatigueVector(
        string workflowId,
        TimeSpan elapsed,
        int stepCount,
        List<UncertaintyEvent>? uncertaintyLog,
        int unresolvedPropagationCount,
        int humanCollisionCount,
        double driftIndex)
    {
        int verificationFails = 0;
        int environmentalInterrupts = 0;

        if (uncertaintyLog != null)
        {
            foreach (var evt in uncertaintyLog)
            {
                var r = evt.Reason ?? "";
                if (r.Contains("Verification", StringComparison.OrdinalIgnoreCase) || 
                    evt.Level == UncertaintyLevel.U1_Observational)
                {
                    verificationFails++;
                }
                else if (r.Contains("interrupt", StringComparison.OrdinalIgnoreCase) || 
                         r.Contains("modal", StringComparison.OrdinalIgnoreCase))
                {
                    environmentalInterrupts++;
                }
            }
        }

        return new EpistemicFatigueVector
        {
            TemporalEntropy = Math.Min(1.0, (elapsed.TotalMinutes * DecayPerMinute) + (stepCount * DecayPerStep)),
            VerificationErosion = Math.Min(1.0, verificationFails * 0.15),
            EnvironmentalInstability = Math.Min(1.0, environmentalInterrupts * 0.20),
            PropagationAmbiguity = Math.Min(1.0, unresolvedPropagationCount * 0.25),
            HumanCollisionDensity = Math.Min(1.0, humanCollisionCount * 0.20),
            ProceduralDivergence = Math.Min(1.0, driftIndex)
        };
    }
}
