using System;
using System.Collections.Generic;
using Engram.Store.Automation;
using Engram.Store.Events;
using Xunit;

using ExecutionContext = Engram.Store.Automation.ExecutionContext;

namespace Engram.Store.Tests;

public class WorkflowIntentMonitorTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();
    private readonly InMemoryEventBus _eventBus = new();
    private readonly OperationalWorldModel _worldModel;

    public WorkflowIntentMonitorTests()
    {
        _worldModel = new OperationalWorldModel(_eventBus);
    }

    public void Dispose()
    {
        _workspace.Dispose();
    }

    [Fact]
    public void EvaluateIntent_WithNoContradictions_ReturnsActiveState()
    {
        var monitor = new WorkflowIntentMonitor(_worldModel, _eventBus);
        var plan = new ExecutionPlan();
        var context = new ExecutionContext();

        var status = monitor.EvaluateIntent("w1", plan, context);

        Assert.Equal(WorkflowVitalityState.Active, status.VitalityState);
        Assert.Equal(1.0, status.IntentAlignment);
        Assert.Equal(1.0, status.AttentionAllocation);
        Assert.Equal(1.0, status.ExecutionSpeedFactor);
        Assert.False(status.SuppressInterventions);
    }

    [Fact]
    public void EvaluateIntent_WithContradictoryActions_AppliesPenalty()
    {
        var monitor = new WorkflowIntentMonitor(_worldModel, _eventBus);
        var plan = new ExecutionPlan();
        var context = new ExecutionContext();

        monitor.RegisterContradictoryAction("User opened social media");
        var status = monitor.EvaluateIntent("w1", plan, context);

        // Contradictory action penalty: 0.25
        Assert.Equal(0.75, status.IntentAlignment);
        Assert.Equal(WorkflowVitalityState.Active, status.VitalityState);

        monitor.RegisterContradictoryAction("User closed editor");
        status = monitor.EvaluateIntent("w1", plan, context);

        // 2 contradictions -> 0.5 penalty
        Assert.Equal(0.5, status.IntentAlignment);
        Assert.Equal(WorkflowVitalityState.Dormant, status.VitalityState);
        Assert.True(status.SuppressInterventions);
    }

    [Fact]
    public void EvaluateIntent_WithInactivity_AppliesMomentumDecay()
    {
        var monitor = new WorkflowIntentMonitor(_worldModel, _eventBus);
        var plan = new ExecutionPlan();
        var context = new ExecutionContext();

        // Simulate 40 minutes inactivity by hacking the internal state via progress record + reflection or wait?
        // Wait, since we can't easily wait 40 minutes in a test, let's verify if we can set last progress time.
        // Wait, WorkflowIntentMonitor._lastProgressTime is private. We can use reflection to set it for the test.
        var field = typeof(WorkflowIntentMonitor).GetField("_lastProgressTime", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(field);

        // 15 mins inactivity
        field!.SetValue(monitor, DateTimeOffset.UtcNow - TimeSpan.FromMinutes(15));
        var status = monitor.EvaluateIntent("w1", plan, context);
        Assert.Equal(0.6, status.MomentumScore, 5);
        Assert.Equal(0.84, status.IntentAlignment, 5); // 1.0 - (1 - 0.6) * 0.4 = 0.84

        // 35 mins inactivity
        field.SetValue(monitor, DateTimeOffset.UtcNow - TimeSpan.FromMinutes(35));
        status = monitor.EvaluateIntent("w1", plan, context);
        Assert.Equal(0.3, status.MomentumScore, 5);
        Assert.Equal(0.72, status.IntentAlignment, 5);

        // 65 mins inactivity
        field.SetValue(monitor, DateTimeOffset.UtcNow - TimeSpan.FromMinutes(65));
        status = monitor.EvaluateIntent("w1", plan, context);
        Assert.Equal(0.1, status.MomentumScore, 5);
        Assert.Equal(0.64, status.IntentAlignment, 5);
        Assert.Equal(WorkflowVitalityState.Weakening, status.VitalityState);
    }

    [Fact]
    public void EvaluateIntent_WithActiveWorkflowShift_AppliesPenalty()
    {
        var monitor = new WorkflowIntentMonitor(_worldModel, _eventBus);
        var plan = new ExecutionPlan();
        var context = new ExecutionContext();

        _worldModel.UpdateState("Running", "w2", "docs.txt", 3, new Dictionary<string, string>());
        var status = monitor.EvaluateIntent("w1", plan, context);

        // Other workflow active -> 0.3 penalty
        Assert.Equal(0.7, status.IntentAlignment);
        Assert.Equal(WorkflowVitalityState.Weakening, status.VitalityState);
    }

    [Fact]
    public void EvaluateIntent_WhenWorldModelSuspended_ReturnsSuspendedState()
    {
        var monitor = new WorkflowIntentMonitor(_worldModel, _eventBus);
        var plan = new ExecutionPlan();
        var context = new ExecutionContext();

        _worldModel.UpdateState("Suspended", "w1", "docs.txt", 3, new Dictionary<string, string>());
        var status = monitor.EvaluateIntent("w1", plan, context);

        Assert.Equal(WorkflowVitalityState.Suspended, status.VitalityState);
        Assert.Equal("Wait", status.Recommendation);
    }

    [Fact]
    public void EvaluateIntent_PublishesDecayEvent_WhenAlignmentBelowHalf()
    {
        var monitor = new WorkflowIntentMonitor(_worldModel, _eventBus);
        var plan = new ExecutionPlan();
        var context = new ExecutionContext();

        bool eventFired = false;
        _eventBus.Subscribe("automation.intent.decayed", env =>
        {
            eventFired = true;
            Assert.Contains("w1", env.Payload.ToString());
        });

        // Trigger decay below 0.5 via 2 contradictory actions
        monitor.RegisterContradictoryAction("Action 1");
        monitor.RegisterContradictoryAction("Action 2");
        monitor.RegisterContradictoryAction("Action 3");

        var status = monitor.EvaluateIntent("w1", plan, context);

        Assert.True(eventFired);
        Assert.True(status.IntentAlignment < 0.5);
    }
}
