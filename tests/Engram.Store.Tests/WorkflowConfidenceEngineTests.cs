using System;
using System.Collections.Generic;
using Engram.Store.Automation;
using Engram.Store.Events;
using Xunit;

using ExecutionContext = Engram.Store.Automation.ExecutionContext;

namespace Engram.Store.Tests;

public class WorkflowConfidenceEngineTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();
    private readonly InMemoryEventBus _eventBus = new();
    private readonly ExecutionTelemetryEngine _telemetry;
    private readonly ProceduralMemoryEngine _proceduralMemory;

    public WorkflowConfidenceEngineTests()
    {
        _telemetry = new ExecutionTelemetryEngine(_workspace.Root);
        _proceduralMemory = new ProceduralMemoryEngine(_workspace.Root);
    }

    public void Dispose()
    {
        _workspace.Dispose();
    }

    [Fact]
    public void ComputeConfidence_WithNoTelemetryOrFailures_ReturnsHighOverallConfidence()
    {
        var engine = new WorkflowConfidenceEngine(_telemetry, _proceduralMemory, _eventBus);
        var plan = new ExecutionPlan();
        var context = new ExecutionContext();
        
        plan.Steps["s1"] = new ExecutionStep { Id = "s1", Status = StepStatus.Completed };
        plan.Steps["s2"] = new ExecutionStep { Id = "s2", Status = StepStatus.Completed };

        var confidence = engine.ComputeConfidence("w1", plan, context);

        Assert.True(confidence.OverallConfidence > 0.7);
        Assert.Equal(1.0, confidence.ExecutionConfidence);
        Assert.Equal(ConfidenceTrend.Stable, confidence.Trend);
    }

    [Fact]
    public void ComputeConfidence_WithFailedStepsAndAmbiguities_DeductsConfidence()
    {
        var engine = new WorkflowConfidenceEngine(_telemetry, _proceduralMemory, _eventBus);
        var plan = new ExecutionPlan();
        var context = new ExecutionContext();

        // 1 completed, 1 failed, 1 unresolved variable
        plan.Steps["s1"] = new ExecutionStep { Id = "s1", Status = StepStatus.Completed };
        plan.Steps["s2"] = new ExecutionStep { Id = "s2", Status = StepStatus.Failed };
        plan.Steps["s3"] = new ExecutionStep 
        { 
            Id = "s3", 
            Status = StepStatus.Pending, 
            Action = new AutomationAction { Type = ActionType.Navigate, Value = "${missing_url}" } 
        };

        var confidence = engine.ComputeConfidence("w1", plan, context);

        Assert.True(confidence.ExecutionConfidence < 1.0);
        Assert.Equal(0.2, confidence.AmbiguityScore); // 1 missing var * 0.2
        Assert.True(confidence.OverallConfidence < 0.7);
    }

    [Fact]
    public void ComputeConfidence_TracksTrendAndFiresCollapseEvent()
    {
        var engine = new WorkflowConfidenceEngine(_telemetry, _proceduralMemory, _eventBus);
        var context = new ExecutionContext();

        bool collapseEventFired = false;
        _eventBus.Subscribe("automation.confidence.collapsed", env =>
        {
            collapseEventFired = true;
        });

        // Compute multiple times to populate history and trigger declining/collapsing trend
        WorkflowConfidence lastConfidence = null!;
        for (int i = 0; i < 5; i++)
        {
            var loopPlan = new ExecutionPlan();
            for (int j = 0; j <= i; j++)
            {
                loopPlan.Steps[$"s{j}"] = new ExecutionStep { Id = $"s{j}", Status = StepStatus.Failed };
            }
            var intentStatus = new WorkflowIntentStatus { IntentAlignment = 1.0 - (i * 0.25) };
            lastConfidence = engine.ComputeConfidence("w1", loopPlan, context, intentStatus);
        }

        Assert.True(lastConfidence.OverallConfidence < 0.3);
        Assert.True(collapseEventFired);
        Assert.True(lastConfidence.Trend == ConfidenceTrend.Collapsing || lastConfidence.Trend == ConfidenceTrend.Declining);
    }

    [Fact]
    public void DetermineMultiFactorVitality_EnforcesProgressiveDecayAndGracefulSuspension()
    {
        var engine = new WorkflowConfidenceEngine(_telemetry, _proceduralMemory, _eventBus);
        var confidence = new WorkflowConfidence();
        var intent = new WorkflowIntentStatus { IntentAlignment = 1.0 };

        // Test Case 1: High confidence, active
        confidence.OverallConfidence = 0.8;
        intent.IntentAlignment = 0.8;
        var state1 = engine.DetermineMultiFactorVitality(confidence, intent, TimeSpan.FromMinutes(2), false, 0);
        Assert.Equal(WorkflowVitalityState.Active, state1);

        // Test Case 2: Weakening (0.5 - 0.7)
        confidence.OverallConfidence = 0.6;
        var state2 = engine.DetermineMultiFactorVitality(confidence, intent, TimeSpan.FromMinutes(5), false, 0);
        Assert.Equal(WorkflowVitalityState.Weakening, state2);

        // Test Case 3: Dormant Candidate (0.3 - 0.5)
        confidence.OverallConfidence = 0.4;
        var state3 = engine.DetermineMultiFactorVitality(confidence, intent, TimeSpan.FromMinutes(15), false, 0);
        Assert.Equal(WorkflowVitalityState.Dormant, state3);

        // Test Case 4: Passive Suspension Suggestion (0.1 - 0.3)
        confidence.OverallConfidence = 0.2;
        var state4 = engine.DetermineMultiFactorVitality(confidence, intent, TimeSpan.FromMinutes(20), false, 0);
        Assert.Equal(WorkflowVitalityState.ObsoleteCandidate, state4);

        // Test Case 5: Low confidence (< 0.1) but NO other factors -> remains ObsoleteCandidate (graceful degradation)
        confidence.OverallConfidence = 0.05;
        var state5 = engine.DetermineMultiFactorVitality(confidence, intent, TimeSpan.FromMinutes(25), false, 0);
        Assert.Equal(WorkflowVitalityState.ObsoleteCandidate, state5);

        // Test Case 6: Low confidence (< 0.1) AND time threshold met AND other factors -> Suspend
        var state6 = engine.DetermineMultiFactorVitality(confidence, intent, TimeSpan.FromMinutes(35), true, 1);
        Assert.Equal(WorkflowVitalityState.Suspended, state6);
    }
}
