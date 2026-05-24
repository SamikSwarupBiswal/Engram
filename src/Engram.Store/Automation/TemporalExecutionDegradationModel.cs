using System;

namespace Engram.Store.Automation;

public class TemporalExecutionDegradationModel
{
    private const double DecayPerMinute = 0.02;
    private const double DecayPerStep = 0.01;

    public double ComputeTemporalDecayFactor(string workflowId, TimeSpan elapsed, int stepCount)
    {
        double minutes = elapsed.TotalMinutes;
        double timeDecay = minutes * DecayPerMinute;
        double stepDecay = stepCount * DecayPerStep;

        double totalDecay = timeDecay + stepDecay;

        // Return a scale multiplier between 0.2 and 1.0
        return Math.Max(0.2, 1.0 - totalDecay);
    }
}
