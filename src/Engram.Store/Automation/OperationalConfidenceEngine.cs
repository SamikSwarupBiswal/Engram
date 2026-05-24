using System;

namespace Engram.Store.Automation;

public class OperationalConfidenceEngine
{
    private readonly CoexistenceMetricsTracker _coexistenceTracker;
    private readonly CapabilityConfidenceDecay _capabilityDecay;
    private readonly TemporalExecutionDegradationModel _temporalModel;

    public OperationalConfidenceEngine(
        CoexistenceMetricsTracker coexistenceTracker,
        CapabilityConfidenceDecay capabilityDecay,
        TemporalExecutionDegradationModel temporalModel)
    {
        _coexistenceTracker = coexistenceTracker ?? throw new ArgumentNullException(nameof(coexistenceTracker));
        _capabilityDecay = capabilityDecay ?? throw new ArgumentNullException(nameof(capabilityDecay));
        _temporalModel = temporalModel ?? throw new ArgumentNullException(nameof(temporalModel));
    }

    public double ComputeAuthorityScore(
        string workflowId, 
        string appName, 
        string appVersion,
        TimeSpan elapsed, 
        int stepCount,
        double environmentVolatility = 0.0)
    {
        // 1. App capability base confidence
        double appConfidence = _capabilityDecay.GetAppCapabilityConfidence(appName, appVersion);

        // 2. Temporal execution degradation factor (entropy over duration)
        double temporalFactor = _temporalModel.ComputeTemporalDecayFactor(workflowId, elapsed, stepCount);

        // 3. User interruption density factor
        double interruptionFactor = 1.0;
        var metrics = _coexistenceTracker.CalculateMetrics();
        if (metrics.InterruptionIrritation > 0)
        {
            // More interruptions reduce our authority/autonomy score
            interruptionFactor = Math.Max(0.2, 1.0 - metrics.InterruptionIrritation);
        }

        // 4. Combine factors
        double score = appConfidence * temporalFactor * interruptionFactor - (environmentVolatility * 0.15);

        return Math.Clamp(score, 0.0, 1.0);
    }
}
