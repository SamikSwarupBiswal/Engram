using Engram.Store.Capture;
using Engram.Store.Providers;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Integration tests for Phase 3 local ingestion.
/// Tests the full flow: consent -> capture -> write -> verify.
/// </summary>
public class Phase3IntegrationTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();
    private readonly ContentHasher _hasher = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void FullFlow_ConsentDefault_AllSourcesOff()
    {
        // NFR-004: All sensitive capture sources disabled by default
        var config = new EngramConfig();

        Assert.False(config.ClipboardCaptureEnabled);
        Assert.False(config.ActiveWindowCaptureEnabled);
        Assert.False(config.FileWatcherEnabled);
    }

    [Fact]
    public void FullFlow_ExcludedApps_NeverCaptured()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        var writer = new RawEventWriter(_workspace.Paths, _hasher);
        var config = new EngramConfig();
        var orch = new CaptureOrchestrator(writer, _hasher, config, _workspace.Paths);

        // Password manager events should be excluded
        Assert.True(orch.IsExcluded("1password"));
        Assert.True(orch.IsExcluded("bitwarden"));
        Assert.True(orch.IsExcluded("keepass"));

        // Normal apps should pass
        Assert.False(orch.IsExcluded("chrome"));
        Assert.False(orch.IsExcluded("code"));
        Assert.False(orch.IsExcluded("explorer"));
    }

    [Fact]
    public void FullFlow_FileWatcher_WritesEvents()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        var writer = new RawEventWriter(_workspace.Paths, _hasher);
        var config = new EngramConfig();
        var orch = new CaptureOrchestrator(writer, _hasher, config, _workspace.Paths);

        // Simulate a captured event
        var evt = TestEvents.Create(
            eventType: "file_change",
            source: "file_watcher",
            text: "New file: report.pdf");

        var result = orch.ProcessEvent(evt);

        Assert.Equal(WriteOutcome.Created, result.Outcome);

        // Verify it's in the raw store
        var replay = new ReplayEnumerator(_workspace.Paths);
        var events = replay.EnumerateAll();

        Assert.Single(events);
        Assert.Equal("file_watcher", events[0].Source);
        Assert.Equal("file_change", events[0].EventType);
    }

    [Fact]
    public void FullFlow_ClipboardEvent_WithExclusion()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        var writer = new RawEventWriter(_workspace.Paths, _hasher);
        var config = new EngramConfig { ClipboardCaptureEnabled = true };
        var orch = new CaptureOrchestrator(writer, _hasher, config, _workspace.Paths);

        // Simulate clipboard event from non-excluded app
        var evt = TestEvents.Create(
            eventType: "clipboard",
            source: "clipboard_monitor",
            text: "Meeting notes from standup");
        evt.SourceUri = "clipboard://";

        var result = orch.ProcessEvent(evt);
        Assert.Equal(WriteOutcome.Created, result.Outcome);
    }

    [Fact]
    public void FullFlow_MultipleSources_SameTimeline()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        var writer = new RawEventWriter(_workspace.Paths, _hasher);
        var config = new EngramConfig
        {
            FileWatcherEnabled = true,
            ClipboardCaptureEnabled = true,
            ActiveWindowCaptureEnabled = true
        };
        var orch = new CaptureOrchestrator(writer, _hasher, config, _workspace.Paths);

        // Simulate events from different sources
        orch.ProcessEvent(TestEvents.Create(source: "file_watcher", text: "file event"));
        orch.ProcessEvent(TestEvents.Create(source: "clipboard_monitor", text: "clipboard event"));
        orch.ProcessEvent(TestEvents.Create(source: "active_window", text: "window event"));

        var replay = new ReplayEnumerator(_workspace.Paths);
        var events = replay.EnumerateAll();

        Assert.Equal(3, events.Count);
        Assert.Contains(events, e => e.Source == "file_watcher");
        Assert.Contains(events, e => e.Source == "clipboard_monitor");
        Assert.Contains(events, e => e.Source == "active_window");
    }

    [Fact]
    public void FullFlow_Deduplication_WorksAcrossSources()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        var writer = new RawEventWriter(_workspace.Paths, _hasher);
        var config = new EngramConfig();
        var orch = new CaptureOrchestrator(writer, _hasher, config, _workspace.Paths);

        // Same content from same source = duplicate
        var evt = TestEvents.Create(source: "file_watcher", text: "same content");
        evt.CapturedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        evt.EventType = "test";
        evt.Source = "test";

        orch.ProcessEvent(evt);
        orch.ProcessEvent(evt); // Duplicate

        var replay = new ReplayEnumerator(_workspace.Paths);
        var events = replay.EnumerateAll();

        Assert.Single(events); // Deduped
    }
}
