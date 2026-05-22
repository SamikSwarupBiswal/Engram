using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Engram.Store.Events;

namespace Engram.Store.Automation;

public enum WorkflowVitalityState
{
    Active,
    Weakening,
    Dormant,
    ObsoleteCandidate,
    Suspended,
    Archived
}

public class WorkflowIntentStatus
{
    public string WorkflowId { get; set; } = string.Empty;
    public double IntentAlignment { get; set; } = 1.0;
    public double MomentumScore { get; set; } = 1.0;
    public WorkflowVitalityState VitalityState { get; set; } = WorkflowVitalityState.Active;
    public List<string> StaleSignals { get; set; } = new();
    public List<string> ContradictoryActions { get; set; } = new();
    public string Recommendation { get; set; } = "Continue";
    public DateTimeOffset EvaluatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Progressive Decay parameters
    public double AttentionAllocation => VitalityState switch
    {
        WorkflowVitalityState.Active => 1.0,
        WorkflowVitalityState.Weakening => 0.6,
        WorkflowVitalityState.Dormant => 0.3,
        WorkflowVitalityState.ObsoleteCandidate => 0.1,
        _ => 0.0
    };

    public double ExecutionSpeedFactor => VitalityState switch
    {
        WorkflowVitalityState.Active => 1.0,
        WorkflowVitalityState.Weakening => 0.5,
        WorkflowVitalityState.Dormant => 0.2,
        _ => 0.0
    };

    public bool SuppressInterventions => VitalityState >= WorkflowVitalityState.Dormant;
}

public class WorkflowIntentMonitor : IDisposable
{
    private readonly OperationalWorldModel _worldModel;
    private readonly IEventBus _eventBus;
    private readonly ILogger? _logger;
    private readonly IDisposable _subscriptionHandle;
    private readonly List<string> _contradictoryActions = new();
    private readonly object _lock = new();

    private DateTimeOffset _lastProgressTime = DateTimeOffset.UtcNow;
    private string _lastActiveDocument = string.Empty;
    private int _lastBrowserTabsCount;

    public WorkflowIntentMonitor(OperationalWorldModel worldModel, IEventBus eventBus, ILogger? logger = null)
    {
        _worldModel = worldModel ?? throw new ArgumentNullException(nameof(worldModel));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logger = logger;

        _subscriptionHandle = _eventBus.Subscribe("automation.worldmodel.changed", OnWorldModelChanged);
    }

    private void OnWorldModelChanged(EventEnvelope envelope)
    {
        if (envelope?.Payload == null) return;

        try
        {
            // Simple check using dynamic or reflection to read the changes
            var payloadStr = envelope.Payload.ToString() ?? string.Empty;
            
            // We can also extract values or check current state
            lock (_lock)
            {
                var doc = _worldModel.ActiveDocument;
                if (!string.IsNullOrEmpty(doc) && doc != _lastActiveDocument)
                {
                    _lastActiveDocument = doc;
                    // If active document has changed, and doesn't mention anything related to the workflow goal,
                    // we could track this as a potentially contradictory or context-shifting action.
                    var activeWorkflowId = _worldModel.ActiveWorkflow;
                    if (!string.IsNullOrEmpty(activeWorkflowId) && !doc.Contains("engram", StringComparison.OrdinalIgnoreCase))
                    {
                        // In a real environment, we'd check if the document is unrelated to the goal.
                        // Here we simulate detecting a context shift.
                    }
                }
                
                var tabs = _worldModel.BrowserTabsCount;
                if (tabs != _lastBrowserTabsCount)
                {
                    _lastBrowserTabsCount = tabs;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error processing world model change in WorkflowIntentMonitor");
        }
    }

    public void RegisterContradictoryAction(string description)
    {
        lock (_lock)
        {
            _contradictoryActions.Add(description);
        }
        _logger?.LogWarning("Contradictory action registered: {Description}", description);
    }

    public void RecordProgress()
    {
        lock (_lock)
        {
            _lastProgressTime = DateTimeOffset.UtcNow;
        }
    }

    public WorkflowIntentStatus EvaluateIntent(string workflowId, ExecutionPlan plan, ExecutionContext context)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));

        lock (_lock)
        {
            var status = new WorkflowIntentStatus
            {
                WorkflowId = workflowId,
                EvaluatedAt = DateTimeOffset.UtcNow
            };

            // 1. Analyze Contradictory Actions
            status.ContradictoryActions.AddRange(_contradictoryActions);
            double contradictionPenalty = status.ContradictoryActions.Count * 0.25;

            // 2. Analyze Momentum (Time since last progress)
            var idleTime = DateTimeOffset.UtcNow - _lastProgressTime;
            double momentum = 1.0;
            if (idleTime > TimeSpan.FromMinutes(60))
            {
                momentum = 0.1;
                status.StaleSignals.Add("Inactivity exceeded 60 minutes");
            }
            else if (idleTime > TimeSpan.FromMinutes(30))
            {
                momentum = 0.3;
                status.StaleSignals.Add("Inactivity exceeded 30 minutes");
            }
            else if (idleTime > TimeSpan.FromMinutes(10))
            {
                momentum = 0.6;
                status.StaleSignals.Add("Inactivity exceeded 10 minutes");
            }
            status.MomentumScore = momentum;

            // 3. Context Shift Analysis
            double contextPenalty = 0.0;
            if (!string.IsNullOrEmpty(_worldModel.ActiveWorkflow) && _worldModel.ActiveWorkflow != workflowId)
            {
                contextPenalty += 0.3;
                status.StaleSignals.Add("Another workflow is active");
            }

            // Calculate intent alignment
            double alignment = 1.0 - contradictionPenalty - (1.0 - momentum) * 0.4 - contextPenalty;
            status.IntentAlignment = Math.Clamp(alignment, 0.0, 1.0);

            // Determine vitality state based on intent alignment
            status.VitalityState = status.IntentAlignment switch
            {
                > 0.7 => WorkflowVitalityState.Active,
                > 0.5 => WorkflowVitalityState.Weakening,
                > 0.3 => WorkflowVitalityState.Dormant,
                > 0.1 => WorkflowVitalityState.ObsoleteCandidate,
                _ => WorkflowVitalityState.ObsoleteCandidate
            };

            // If explicitly paused in world model
            if (_worldModel.CurrentPhase == "Paused" || _worldModel.CurrentPhase == "Suspended")
            {
                status.VitalityState = WorkflowVitalityState.Suspended;
            }

            // Recommendation mapping
            status.Recommendation = status.VitalityState switch
            {
                WorkflowVitalityState.Active => "Continue",
                WorkflowVitalityState.Weakening => "ContinueWithDecay",
                WorkflowVitalityState.Dormant => "SuggestPause",
                WorkflowVitalityState.ObsoleteCandidate => "SuggestAbandonment",
                WorkflowVitalityState.Suspended => "Wait",
                WorkflowVitalityState.Archived => "Archive",
                _ => "Continue"
            };

            if (status.IntentAlignment < 0.5)
            {
                _eventBus.Publish(new EventEnvelope
                {
                    EventType = "automation.intent.decayed",
                    Source = "workflow_intent_monitor",
                    Payload = new
                    {
                        WorkflowId = workflowId,
                        VitalityState = status.VitalityState.ToString(),
                        IntentAlignment = status.IntentAlignment,
                        Timestamp = DateTimeOffset.UtcNow
                    }
                });
            }

            return status;
        }
    }

    public void Dispose()
    {
        _subscriptionHandle.Dispose();
    }
}
