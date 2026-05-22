using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Engram.Store.Events;

namespace Engram.Store.Automation;

public enum ConfidenceTrend
{
    Rising,
    Stable,
    Declining,
    Collapsing
}

public class WorkflowConfidence
{
    public string WorkflowId { get; set; } = string.Empty;
    public double OverallConfidence { get; set; } = 1.0;
    public double ExecutionConfidence { get; set; } = 1.0;
    public double RelevanceConfidence { get; set; } = 1.0;
    public double CompletionProbability { get; set; } = 1.0;
    public double InterruptionRisk { get; set; } = 0.0;
    public double AmbiguityScore { get; set; } = 0.0;
    public ConfidenceTrend Trend { get; set; } = ConfidenceTrend.Stable;
    public DateTimeOffset ComputedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class WorkflowConfidenceEngine
{
    private readonly ExecutionTelemetryEngine _telemetry;
    private readonly ProceduralMemoryEngine _proceduralMemory;
    private readonly IEventBus? _eventBus;
    private readonly ILogger? _logger;
    private readonly ConcurrentDictionary<string, List<double>> _confidenceHistory = new();

    public WorkflowConfidenceEngine(
        ExecutionTelemetryEngine telemetry, 
        ProceduralMemoryEngine proceduralMemory, 
        IEventBus? eventBus = null,
        ILogger? logger = null)
    {
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _proceduralMemory = proceduralMemory ?? throw new ArgumentNullException(nameof(proceduralMemory));
        _eventBus = eventBus;
        _logger = logger;
    }

    public WorkflowConfidence ComputeConfidence(string workflowId, ExecutionPlan plan, ExecutionContext context, WorkflowIntentStatus? intentStatus = null)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));

        var confidence = new WorkflowConfidence
        {
            WorkflowId = workflowId,
            ComputedAt = DateTimeOffset.UtcNow
        };

        // 1. Compute ExecutionConfidence
        double stepSuccessRate = 1.0;
        int totalSteps = plan.Steps.Count;
        if (totalSteps > 0)
        {
            int failedSteps = plan.Steps.Values.Count(s => s.Status == StepStatus.Failed);
            int completedSteps = plan.Steps.Values.Count(s => s.Status == StepStatus.Completed);
            stepSuccessRate = (double)(totalSteps - failedSteps) / totalSteps;
            
            // Deduct for failed steps
            confidence.ExecutionConfidence = Math.Clamp(stepSuccessRate - (failedSteps * 0.15), 0.0, 1.0);
        }

        // 2. RelevanceConfidence
        confidence.RelevanceConfidence = intentStatus?.IntentAlignment ?? 1.0;

        // 3. CompletionProbability
        double telemetrySuccessRate = 0.85; // Default baseline
        var summary = _telemetry.GetSummary();
        if (summary.SuccessRate > 0)
        {
            telemetrySuccessRate = summary.SuccessRate;
        }
        
        double progressRatio = totalSteps > 0 
            ? (double)plan.Steps.Values.Count(s => s.Status == StepStatus.Completed) / totalSteps 
            : 0.0;
        confidence.CompletionProbability = Math.Clamp(progressRatio * 0.5 + telemetrySuccessRate * 0.5, 0.0, 1.0);

        // 4. InterruptionRisk
        // InterruptionRisk increases with the number of variables or pause cycles
        double retryRate = summary.RetryFrequency > 0 ? Math.Min(1.0, (double)summary.RetryFrequency / 10.0) : 0.0;
        confidence.InterruptionRisk = Math.Clamp(retryRate * 0.5, 0.0, 1.0);

        // 5. AmbiguityScore
        // Unresolved variables or dynamic params
        int missingVars = plan.Steps.Values.Count(s => s.Action?.Value != null && s.Action.Value.StartsWith("${") && s.Action.Value.EndsWith("}"));
        confidence.AmbiguityScore = Math.Clamp(missingVars * 0.2, 0.0, 1.0);

        // 6. Overall Confidence Fusion
        double overall = (confidence.ExecutionConfidence * 0.4) + 
                         (confidence.RelevanceConfidence * 0.3) + 
                         (confidence.CompletionProbability * 0.2) - 
                         (confidence.InterruptionRisk * 0.1) - 
                         (confidence.AmbiguityScore * 0.15);
        confidence.OverallConfidence = Math.Clamp(overall, 0.0, 1.0);

        // 7. Track History & Determine Trend
        var history = _confidenceHistory.GetOrAdd(workflowId, _ => new List<double>());
        lock (history)
        {
            history.Add(confidence.OverallConfidence);
            if (history.Count > 10) history.RemoveAt(0);

            if (history.Count >= 3)
            {
                double first = history[^3];
                double middle = history[^2];
                double last = history[^1];

                if (last < 0.15 && last < middle)
                {
                    confidence.Trend = ConfidenceTrend.Collapsing;
                }
                else if (last < middle && middle < first)
                {
                    confidence.Trend = ConfidenceTrend.Declining;
                }
                else if (last > middle && middle > first)
                {
                    confidence.Trend = ConfidenceTrend.Rising;
                }
                else
                {
                    confidence.Trend = ConfidenceTrend.Stable;
                }
            }
        }

        // Publish collapse event if needed
        if (confidence.OverallConfidence < 0.1 && _eventBus != null)
        {
            _eventBus.Publish(new EventEnvelope
            {
                EventType = "automation.confidence.collapsed",
                Source = "workflow_confidence_engine",
                Payload = new
                {
                    WorkflowId = workflowId,
                    OverallConfidence = confidence.OverallConfidence,
                    Trend = confidence.Trend.ToString(),
                    Timestamp = DateTimeOffset.UtcNow
                }
            });
        }

        return confidence;
    }

    /// <summary>
    /// Multi-factor Vitality State determination that replaces confidence-only suspension.
    /// LOW confidence alone does NOT suspend. Combines low confidence, elapsed inactivity, 
    /// contradiction evidence, operational drift, and failed resumptions.
    /// </summary>
    public WorkflowVitalityState DetermineMultiFactorVitality(
        WorkflowConfidence confidence, 
        WorkflowIntentStatus intent, 
        TimeSpan inactivityTime, 
        bool hasDriftAlerts, 
        int failedResumptions)
    {
        if (confidence == null) throw new ArgumentNullException(nameof(confidence));
        if (intent == null) throw new ArgumentNullException(nameof(intent));

        // Start with intent's initial vitality state assessment
        double score = confidence.OverallConfidence;

        // If overall confidence and intent are extremely high, we are Active
        if (score > 0.7 && intent.IntentAlignment > 0.7 && inactivityTime < TimeSpan.FromMinutes(10))
        {
            return WorkflowVitalityState.Active;
        }

        // 0.5 - 0.7: Weakening
        if (score > 0.5 && score <= 0.7)
        {
            return WorkflowVitalityState.Weakening;
        }

        // 0.3 - 0.5: Dormant Candidate
        if (score > 0.3 && score <= 0.5)
        {
            return WorkflowVitalityState.Dormant;
        }

        // 0.1 - 0.3: Passive Suspension Suggestion
        if (score > 0.1 && score <= 0.3)
        {
            return WorkflowVitalityState.ObsoleteCandidate;
        }

        // < 0.1: Soft suspend ONLY after time threshold combined with other indicators
        if (score <= 0.1)
        {
            bool hasMultipleFactors = hasDriftAlerts || failedResumptions > 0 || intent.ContradictoryActions.Count > 0;
            bool timeThresholdMet = inactivityTime > TimeSpan.FromMinutes(30);

            if (timeThresholdMet && hasMultipleFactors)
            {
                return WorkflowVitalityState.Suspended;
            }
            
            // Otherwise, keep as ObsoleteCandidate/Dormant to avoid premature hard suspension
            return WorkflowVitalityState.ObsoleteCandidate;
        }

        return intent.VitalityState;
    }
}
