using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Engram.Store.Automation;
using Engram.Store.Events;
using Xunit;

namespace Engram.Store.Tests;

public class ExecutionReplayEngineTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();
    private readonly InMemoryEventBus _eventBus = new();
    private readonly OperationalTimeline _timeline;
    private readonly WorkflowPersistenceStore _persistenceStore;

    public ExecutionReplayEngineTests()
    {
        _timeline = new OperationalTimeline(_workspace.Root);
        _persistenceStore = new WorkflowPersistenceStore(_workspace.Root);
    }

    public void Dispose()
    {
        _workspace.Dispose();
    }

    [Fact]
    public async Task LoadReplayAsync_CalculatesReplayStatsCorrectly()
    {
        var engine = new ExecutionReplayEngine(_timeline, _persistenceStore);
        var workflowId = "wf1";

        // Save a checkpoint goal
        await _persistenceStore.SaveCheckpointAsync(new WorkflowCheckpoint
        {
            WorkflowId = workflowId,
            Goal = "Download PDF document"
        });

        var baseTime = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5);

        // Record events
        _timeline.RecordEvent(new TimelineEvent { WorkflowId = workflowId, EventType = "StepStarted", Description = "Start", Timestamp = baseTime });
        _timeline.RecordEvent(new TimelineEvent { WorkflowId = workflowId, EventType = "StepCompleted", Description = "Downloaded", Timestamp = baseTime + TimeSpan.FromMinutes(1) });
        _timeline.RecordEvent(new TimelineEvent { WorkflowId = workflowId, EventType = "WorkflowPause", Description = "Interrupted", Timestamp = baseTime + TimeSpan.FromMinutes(2) });
        _timeline.RecordEvent(new TimelineEvent { WorkflowId = workflowId, EventType = "StepFailed", Description = "Failed writing file", Timestamp = baseTime + TimeSpan.FromMinutes(3) });
        _timeline.RecordEvent(new TimelineEvent { WorkflowId = workflowId, EventType = "StepRetry", Description = "Retried write", Timestamp = baseTime + TimeSpan.FromMinutes(4) });
        _timeline.RecordEvent(new TimelineEvent { WorkflowId = workflowId, EventType = "StepCompleted", Description = "Success", Timestamp = baseTime + TimeSpan.FromMinutes(5) });

        var replay = await engine.LoadReplayAsync(workflowId);

        Assert.Equal("Download PDF document", replay.Goal);
        Assert.Equal(6, replay.Events.Count);
        Assert.Equal(TimeSpan.FromMinutes(5), replay.TotalDuration);
        Assert.Equal(2, replay.StepsCompleted);
        Assert.Equal(1, replay.StepsFailed);
        Assert.Equal(1, replay.Interruptions);
        Assert.Equal(1, replay.Recoveries);

        // Verify confidence trajectory has entries
        Assert.Equal(6, replay.ConfidenceTrajectory.Count);
        // The last confidence should reflect recoveries and completions
        Assert.True(replay.ConfidenceTrajectory[^1].Confidence > 0);
    }

    [Fact]
    public async Task CompareAsync_ComputesSimilarityAndDiffsCorrectly()
    {
        var engine = new ExecutionReplayEngine(_timeline, _persistenceStore);

        // Workflow 1
        await _persistenceStore.SaveCheckpointAsync(new WorkflowCheckpoint { WorkflowId = "wf1", Goal = "Goal A" });
        _timeline.RecordEvent(new TimelineEvent { WorkflowId = "wf1", EventType = "StepCompleted", Timestamp = DateTimeOffset.UtcNow });

        // Workflow 2 - Identical
        await _persistenceStore.SaveCheckpointAsync(new WorkflowCheckpoint { WorkflowId = "wf2", Goal = "Goal A" });
        _timeline.RecordEvent(new TimelineEvent { WorkflowId = "wf2", EventType = "StepCompleted", Timestamp = DateTimeOffset.UtcNow });

        // Workflow 3 - Different Goal and counts
        await _persistenceStore.SaveCheckpointAsync(new WorkflowCheckpoint { WorkflowId = "wf3", Goal = "Goal B" });
        _timeline.RecordEvent(new TimelineEvent { WorkflowId = "wf3", EventType = "StepFailed", Timestamp = DateTimeOffset.UtcNow });
        _timeline.RecordEvent(new TimelineEvent { WorkflowId = "wf3", EventType = "WorkflowPause", Timestamp = DateTimeOffset.UtcNow });

        // Compare identical
        var comparison1 = await engine.CompareAsync("wf1", "wf2");
        Assert.Equal(1.0, comparison1.SimilarityScore);
        Assert.Empty(comparison1.Differences);

        // Compare different
        var comparison2 = await engine.CompareAsync("wf1", "wf3");
        Assert.True(comparison2.SimilarityScore < 1.0);
        Assert.NotEmpty(comparison2.Differences);
        Assert.Contains(comparison2.Differences, d => d.Contains("Goal B"));
    }
}
