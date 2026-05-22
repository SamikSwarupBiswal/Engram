using System;
using Microsoft.Extensions.Logging;
using Engram.Store.Events;

namespace Engram.Store.Automation;

public enum InterruptionType
{
    UrgentInterruption,
    TemporaryBreak,
    ContextSwitch,
    FatigueBreak,
    IntentionalAbandonment,
    PriorityOverride
}

public class InterruptionClassification
{
    public string WorkflowId { get; set; } = string.Empty;
    public InterruptionType Type { get; set; }
    public double Confidence { get; set; } = 1.0;
    public TimeSpan Duration { get; set; }
    public double ResumptionLikelihood { get; set; } = 1.0;
    public string SuggestedAction { get; set; } = "WaitAndResume";
    public DateTimeOffset ClassifiedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class InterruptionClassifier
{
    private readonly OperationalWorldModel _worldModel;
    private readonly IEventBus _eventBus;
    private readonly ILogger? _logger;

    public Func<DateTimeOffset> TimeProvider { get; set; } = () => DateTimeOffset.UtcNow;

    public InterruptionClassifier(OperationalWorldModel worldModel, IEventBus eventBus, ILogger? logger = null)
    {
        _worldModel = worldModel ?? throw new ArgumentNullException(nameof(worldModel));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logger = logger;
    }

    public InterruptionClassification Classify(string workflowId, TimeSpan pauseDuration, string? userActivity = null)
    {
        var classification = new InterruptionClassification
        {
            WorkflowId = workflowId,
            Duration = pauseDuration,
            ClassifiedAt = TimeProvider()
        };

        // Determine basic type and confidence decay based on pause duration
        // 5 min -> almost certainly temporary
        // 30 min -> uncertain
        // 4 hrs -> possible abandonment
        // 2 days -> likely dormant
        // 2 weeks -> probable abandonment
        if (pauseDuration <= TimeSpan.FromMinutes(5))
        {
            classification.Type = InterruptionType.TemporaryBreak;
            classification.Confidence = 0.95;
            classification.ResumptionLikelihood = 0.95;
            classification.SuggestedAction = "WaitAndResume";
        }
        else if (pauseDuration <= TimeSpan.FromMinutes(30))
        {
            classification.Type = InterruptionType.ContextSwitch;
            classification.Confidence = 0.70;
            classification.ResumptionLikelihood = 0.80;
            classification.SuggestedAction = "CheckpointAndSuspend";
        }
        else if (pauseDuration <= TimeSpan.FromHours(4))
        {
            classification.Type = InterruptionType.UrgentInterruption;
            classification.Confidence = 0.50;
            classification.ResumptionLikelihood = 0.60;
            classification.SuggestedAction = "CheckpointAndSuspend";
        }
        else if (pauseDuration <= TimeSpan.FromDays(2))
        {
            classification.Type = InterruptionType.FatigueBreak;
            classification.Confidence = 0.75;
            classification.ResumptionLikelihood = 0.40;
            classification.SuggestedAction = "AskUser";
        }
        else
        {
            classification.Type = InterruptionType.IntentionalAbandonment;
            classification.Confidence = 0.85;
            classification.ResumptionLikelihood = 0.10;
            classification.SuggestedAction = "Abandon";
        }

        // Apply Semantic Semantics Weighing
        
        // 1. Workflow Type detection
        bool isCodingWorkflow = workflowId.Contains("code", StringComparison.OrdinalIgnoreCase) || 
                                 workflowId.Contains("coding", StringComparison.OrdinalIgnoreCase) || 
                                 workflowId.Contains("develop", StringComparison.OrdinalIgnoreCase) || 
                                 workflowId.Contains("debug", StringComparison.OrdinalIgnoreCase);
        bool isTaxWorkflow = workflowId.Contains("tax", StringComparison.OrdinalIgnoreCase) ||
                             workflowId.Contains("file", StringComparison.OrdinalIgnoreCase) ||
                             workflowId.Contains("invoice", StringComparison.OrdinalIgnoreCase);

        if (isCodingWorkflow)
        {
            // Coding workflow paused overnight or for hours is NORMAL
            if (pauseDuration <= TimeSpan.FromHours(16))
            {
                classification.ResumptionLikelihood = Math.Min(1.0, classification.ResumptionLikelihood + 0.2);
                classification.Type = InterruptionType.TemporaryBreak;
                classification.SuggestedAction = "WaitAndResume";
            }
        }
        else if (isTaxWorkflow)
        {
            // Tax/financial workflow abandoned for days is likely abandoned or requires high caution
            if (pauseDuration > TimeSpan.FromDays(1))
            {
                classification.ResumptionLikelihood = Math.Max(0.0, classification.ResumptionLikelihood - 0.25);
                classification.Type = InterruptionType.IntentionalAbandonment;
                classification.SuggestedAction = "AskUser";
            }
        }

        // 2. Time-of-day checks (overnight is normal)
        var hour = TimeProvider().Hour;
        bool isNight = hour >= 20 || hour <= 6;
        if (isNight && pauseDuration < TimeSpan.FromHours(12))
        {
            classification.ResumptionLikelihood = Math.Min(1.0, classification.ResumptionLikelihood + 0.15);
            if (classification.Type == InterruptionType.UrgentInterruption || classification.Type == InterruptionType.FatigueBreak)
            {
                classification.Type = InterruptionType.TemporaryBreak;
            }
        }

        // 3. Urgency signals
        if (_worldModel.EnvironmentalConstraints.ContainsKey("high_priority_deadline"))
        {
            classification.ResumptionLikelihood = Math.Min(1.0, classification.ResumptionLikelihood + 0.1);
            classification.SuggestedAction = "WaitAndResume";
        }

        // Event emission
        _eventBus.Publish(new EventEnvelope
        {
            EventType = "automation.interruption.classified",
            Source = "interruption_classifier",
            Payload = classification
        });

        return classification;
    }
}
