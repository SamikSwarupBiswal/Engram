using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Engram.Store.Automation;
using Engram.Store.Events;
using Xunit;

namespace Engram.Store.Tests;

public class OperationalDriftEngineTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();
    private readonly InMemoryEventBus _eventBus = new();
    private readonly OperationalWorldModel _worldModel;
    private readonly ExecutionTelemetryEngine _telemetry;
    private readonly OperationalTimeline _timeline;

    public OperationalDriftEngineTests()
    {
        _worldModel = new OperationalWorldModel(_eventBus);
        _telemetry = new ExecutionTelemetryEngine(_workspace.Root);
        _timeline = new OperationalTimeline(_workspace.Root);
    }

    public void Dispose()
    {
        _workspace.Dispose();
    }

    private OperationalDriftEngine CreateEngine()
    {
        return new OperationalDriftEngine(
            _worldModel,
            _telemetry,
            _timeline,
            _eventBus,
            _workspace.Root);
    }

    [Fact]
    public void DetectDrift_WithNoEvents_ReturnsEmpty()
    {
        var engine = CreateEngine();
        var alerts = engine.DetectDrift("w1");
        Assert.Empty(alerts);
    }

    [Fact]
    public void DetectDrift_WithAbandonedWorkflow_DetectsDrift()
    {
        var engine = CreateEngine();

        // 1. Add progress event from 40 minutes ago
        _timeline.RecordEvent(new TimelineEvent
        {
            WorkflowId = "w1",
            EventType = "Progress",
            Description = "Workflow step started",
            Timestamp = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(40)
        });

        // 2. Set world model as active elsewhere
        _worldModel.UpdateState("Running", "w2", "UserDoc.docx", 2, new Dictionary<string, string>());

        var alerts = engine.DetectDrift("w1");

        var abandoned = alerts.FirstOrDefault(a => a.Type == OperationalDriftType.AbandonedWorkflow);
        Assert.NotNull(abandoned);
        Assert.Equal(DriftSeverity.High, abandoned!.Severity);
        Assert.Equal("SuggestPause", abandoned.Recommendation);
    }

    [Fact]
    public void DetectDrift_WithExecutionStagnation_DetectsDrift()
    {
        var engine = CreateEngine();

        // Add 4 retry events for the same step
        for (int i = 0; i < 4; i++)
        {
            _timeline.RecordEvent(new TimelineEvent
            {
                WorkflowId = "w1",
                EventType = "StepRetry",
                Description = "Failed to load page: timeout",
                Timestamp = DateTimeOffset.UtcNow
            });
        }

        var alerts = engine.DetectDrift("w1");

        var stagnation = alerts.FirstOrDefault(a => a.Type == OperationalDriftType.ExecutionStagnation);
        Assert.NotNull(stagnation);
        Assert.Equal(DriftSeverity.Critical, stagnation!.Severity);
    }

    [Fact]
    public void DetectDrift_WithContextCollapse_DetectsDrift()
    {
        var engine = CreateEngine();

        // 1. Add a dummy event to ensure the workflow is tracked
        _timeline.RecordEvent(new TimelineEvent
        {
            WorkflowId = "w1",
            EventType = "Progress",
            Description = "Running",
            Timestamp = DateTimeOffset.UtcNow
        });

        // 2. Set offline constraint in world model
        _worldModel.UpdateState("Blocked", "w1", "", 0, new Dictionary<string, string>
        {
            ["network_offline"] = "True"
        });

        var alerts = engine.DetectDrift("w1");

        var collapse = alerts.FirstOrDefault(a => a.Type == OperationalDriftType.ContextCollapse);
        Assert.NotNull(collapse);
        Assert.Equal(DriftSeverity.Critical, collapse!.Severity);
    }

    [Fact]
    public void DetectDrift_WithWorkflowLoop_DetectsDrift()
    {
        var engine = CreateEngine();

        // Add 4 pause/resume cycles (8 events total)
        for (int i = 0; i < 4; i++)
        {
            _timeline.RecordEvent(new TimelineEvent { WorkflowId = "w1", EventType = "WorkflowPause", Description = "Paused", Timestamp = DateTimeOffset.UtcNow });
            _timeline.RecordEvent(new TimelineEvent { WorkflowId = "w1", EventType = "WorkflowResume", Description = "Resumed", Timestamp = DateTimeOffset.UtcNow });
        }

        var alerts = engine.DetectDrift("w1");

        var loop = alerts.FirstOrDefault(a => a.Type == OperationalDriftType.WorkflowLoop);
        Assert.NotNull(loop);
        Assert.Equal(DriftSeverity.Medium, loop!.Severity);
    }

    [Fact]
    public void AlertLifecycle_AcceptAndDismiss_UpdatesAlertStatus()
    {
        var engine = CreateEngine();

        _timeline.RecordEvent(new TimelineEvent
        {
            WorkflowId = "w1",
            EventType = "StepRetry",
            Description = "Failed step",
            Timestamp = DateTimeOffset.UtcNow
        });
        _timeline.RecordEvent(new TimelineEvent { WorkflowId = "w1", EventType = "StepRetry", Description = "Failed step", Timestamp = DateTimeOffset.UtcNow });
        _timeline.RecordEvent(new TimelineEvent { WorkflowId = "w1", EventType = "StepRetry", Description = "Failed step", Timestamp = DateTimeOffset.UtcNow });
        _timeline.RecordEvent(new TimelineEvent { WorkflowId = "w1", EventType = "StepRetry", Description = "Failed step", Timestamp = DateTimeOffset.UtcNow });

        var alerts = engine.DetectDrift("w1");
        Assert.NotEmpty(alerts);

        var alert = alerts[0];
        Assert.Equal(DriftAlertStatus.Pending, alert.Status);

        // Dismiss alert
        engine.DismissAlert(alert.AlertId);
        alerts = engine.GetAlerts("w1");
        Assert.Equal(DriftAlertStatus.Dismissed, alerts[0].Status);

        // Accept alert
        engine.AcceptAlert(alert.AlertId);
        alerts = engine.GetAlerts("w1");
        Assert.Equal(DriftAlertStatus.Accepted, alerts[0].Status);
    }
}
