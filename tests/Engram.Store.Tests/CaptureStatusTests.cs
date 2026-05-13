using Engram.Store.Search;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for capture status tracking.
/// Production requirement: pause/resume, event counting, per-source state.
/// </summary>
public class CaptureStatusTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void InitialState_NotPaused()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        var status = new CaptureStatus(_workspace.Paths);

        Assert.False(status.IsPaused);
    }

    [Fact]
    public void Pause_SetsPaused()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        var status = new CaptureStatus(_workspace.Paths);

        status.Pause();

        Assert.True(status.IsPaused);
    }

    [Fact]
    public void Resume_ClearsPaused()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        var status = new CaptureStatus(_workspace.Paths);

        status.Pause();
        status.Resume();

        Assert.False(status.IsPaused);
    }

    [Fact]
    public void PauseResume_PersistsAcrossInstances()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);

        var status1 = new CaptureStatus(_workspace.Paths);
        status1.Pause();

        var status2 = new CaptureStatus(_workspace.Paths);
        Assert.True(status2.IsPaused);
    }

    [Fact]
    public void RecordEvent_IncrementsCounter()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        var status = new CaptureStatus(_workspace.Paths);

        status.RecordEvent("file_watcher");
        status.RecordEvent("file_watcher");
        status.RecordEvent("clipboard");

        Assert.Equal(3, status.CurrentState.TotalEventsCaptured);
        Assert.Equal(2, status.CurrentState.EventsBySource["file_watcher"]);
        Assert.Equal(1, status.CurrentState.EventsBySource["clipboard"]);
    }

    [Fact]
    public void RecordDrop_IncrementsCounter()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        var status = new CaptureStatus(_workspace.Paths);

        status.RecordDrop("rate_limited");
        status.RecordDrop("rate_limited");
        status.RecordDrop("excluded");

        Assert.Equal(3, status.CurrentState.TotalEventsDropped);
        Assert.Equal(2, status.CurrentState.DropsByReason["rate_limited"]);
    }

    [Fact]
    public void SetSourceEnabled_TogglesSource()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        var status = new CaptureStatus(_workspace.Paths);

        status.SetSourceEnabled("clipboard", false);

        Assert.False(status.IsSourceEnabled("clipboard"));
        Assert.True(status.IsSourceEnabled("file_watcher")); // Others unaffected
    }

    [Fact]
    public void IsSourceEnabled_FalseWhenPaused()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        var status = new CaptureStatus(_workspace.Paths);

        status.SetSourceEnabled("file_watcher", true);
        status.Pause();

        Assert.False(status.IsSourceEnabled("file_watcher"));
    }

    [Fact]
    public void ResetCounters_ClearsAll()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        var status = new CaptureStatus(_workspace.Paths);

        status.RecordEvent("test");
        status.RecordDrop("test");
        status.ResetCounters();

        Assert.Equal(0, status.CurrentState.TotalEventsCaptured);
        Assert.Equal(0, status.CurrentState.TotalEventsDropped);
    }

    [Fact]
    public void LastEventAt_Tracked()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        var status = new CaptureStatus(_workspace.Paths);

        status.RecordEvent("file_watcher");

        Assert.NotNull(status.CurrentState.LastEventAt);
        Assert.Equal("file_watcher", status.CurrentState.LastEventSource);
    }

    [Fact]
    public void LastPausedAt_Tracked()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        var status = new CaptureStatus(_workspace.Paths);

        status.Pause();

        Assert.NotNull(status.CurrentState.LastPausedAt);
    }

    [Fact]
    public void LastResumedAt_Tracked()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        var status = new CaptureStatus(_workspace.Paths);

        status.Pause();
        status.Resume();

        Assert.NotNull(status.CurrentState.LastResumedAt);
    }
}
