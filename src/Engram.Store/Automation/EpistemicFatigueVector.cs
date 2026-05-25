using System;

namespace Engram.Store.Automation;

public class EpistemicFatigueVector
{
    public double TemporalEntropy { get; set; }        // elapsed time decay
    public double VerificationErosion { get; set; }     // verifier retries / failures
    public double EnvironmentalInstability { get; set; } // interrupts / modals
    public double PropagationAmbiguity { get; set; }   // unconfirmed external states
    public double HumanCollisionDensity { get; set; }   // cursor/sovereignty yields
    public double ProceduralDivergence { get; set; }    // timing / selector drift

    public double GetAggregateFatigueIndex()
    {
        // Compute weighted aggregate index where 1.0 is fresh, 0.0 is completely fatigued
        double totalPenalty = (TemporalEntropy * 0.15) + 
                              (VerificationErosion * 0.25) + 
                              (EnvironmentalInstability * 0.15) + 
                              (PropagationAmbiguity * 0.20) + 
                              (HumanCollisionDensity * 0.10) + 
                              (ProceduralDivergence * 0.15);

        return Math.Max(0.1, 1.0 - totalPenalty);
    }
}
