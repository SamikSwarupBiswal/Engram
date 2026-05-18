using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// End-to-end integration tests for the Phase 1 memory spine.
/// These verify the complete flow: init -> write -> dedupe -> replay.
///
/// Derived from: Phase 1 Success Criteria, Quality Gate Policy
///
/// Success Criteria:
/// 1. A fresh clone can build and test a .NET solution without cloud credentials.
/// 2. A command can initialize .engram with required local folders and config.
/// 3. Raw events are written as append-only JSON under .engram/raw/YYYY-MM-DD/[event_id].json.
/// 4. Duplicate raw events are detected by deterministic content hash without rewriting existing files.
/// 5. A replay/import command can enumerate raw events for later processing.
/// </summary>
public class IntegrationTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void FullFlow_Init_Write_Dedupe_Replay()
    {
        // This test exercises the entire Phase 1 memory spine end-to-end.
        // If this passes, the core contract is fulfilled.

        // Step 1: Initialize workspace
        var init = new WorkspaceInitializer();
        init.Initialize(_workspace.Paths);

        Assert.True(init.IsInitialized(_workspace.Paths));

        // Step 2: Write events
        var hasher = new ContentHasher();
        var writer = new RawEventWriter(_workspace.Paths, hasher);

        var evt1 = TestEvents.Create(
            eventType: "file_change",
            source: "file_watcher",
            text: "New document: ProjectProposal.docx",
            capturedAt: new DateTimeOffset(2026, 5, 13, 9, 0, 0, TimeSpan.Zero));

        var evt2 = TestEvents.Create(
            eventType: "clipboard",
            source: "clipboard_monitor",
            text: "Meeting notes from standup",
            capturedAt: new DateTimeOffset(2026, 5, 13, 10, 0, 0, TimeSpan.Zero));

        var result1 = writer.Write(evt1);
        var result2 = writer.Write(evt2);

        Assert.Equal(WriteOutcome.Created, result1.Outcome);
        Assert.Equal(WriteOutcome.Created, result2.Outcome);

        // Step 3: Verify dedupe — writing same events again returns Duplicate
        var dupe1 = writer.Write(evt1);
        var dupe2 = writer.Write(evt2);

        Assert.Equal(WriteOutcome.Duplicate, dupe1.Outcome);
        Assert.Equal(WriteOutcome.Duplicate, dupe2.Outcome);

        // Step 4: Replay returns all events
        var replay = new ReplayEnumerator(_workspace.Paths);
        var allEvents = replay.EnumerateAll();

        Assert.Equal(2, allEvents.Count);

        // Step 5: Verify file structure
        var rawDir = Path.Combine(_workspace.Paths.Raw, "2026-05-13");
        Assert.True(Directory.Exists(rawDir));
        Assert.Equal(2, Directory.GetFiles(rawDir, "*.json").Length);
    }

    [Fact]
    public void FullFlow_MultipleDays_CrossDayReplay()
    {
        var init = new WorkspaceInitializer();
        init.Initialize(_workspace.Paths);

        var hasher = new ContentHasher();
        var writer = new RawEventWriter(_workspace.Paths, hasher);

        // Write events across different days
        var day1 = TestEvents.Create(
            text: "Day 1 event",
            capturedAt: new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var day2 = TestEvents.Create(
            text: "Day 2 event",
            capturedAt: new DateTimeOffset(2026, 1, 2, 12, 0, 0, TimeSpan.Zero));
        var day3 = TestEvents.Create(
            text: "Day 3 event",
            capturedAt: new DateTimeOffset(2026, 1, 3, 12, 0, 0, TimeSpan.Zero));

        writer.Write(day1);
        writer.Write(day2);
        writer.Write(day3);

        // Verify directory structure
        Assert.True(Directory.Exists(Path.Combine(_workspace.Paths.Raw, "2026-01-01")));
        Assert.True(Directory.Exists(Path.Combine(_workspace.Paths.Raw, "2026-01-02")));
        Assert.True(Directory.Exists(Path.Combine(_workspace.Paths.Raw, "2026-01-03")));

        // Replay all
        var replay = new ReplayEnumerator(_workspace.Paths);
        var all = replay.EnumerateAll();

        Assert.Equal(3, all.Count);
        Assert.Equal("Day 1 event", all[0].Text);
        Assert.Equal("Day 2 event", all[1].Text);
        Assert.Equal("Day 3 event", all[2].Text);
    }

    [Fact]
    public void FullFlow_WithMetadata_MetadataPreserved()
    {
        var init = new WorkspaceInitializer();
        init.Initialize(_workspace.Paths);

        var hasher = new ContentHasher();
        var writer = new RawEventWriter(_workspace.Paths, hasher);

        var evt = TestEvents.CreateWithMetadata();
        writer.Write(evt);

        var replay = new ReplayEnumerator(_workspace.Paths);
        var result = replay.EnumerateAll();

        Assert.Single(result);
        Assert.NotNull(result[0].Metadata);
        Assert.Equal(2, result[0].Metadata!.Count);
        Assert.Equal("report.pdf", result[0].Metadata!["file_name"]);
        Assert.Equal("1024", result[0].Metadata!["file_size"]);
        Assert.Equal("file_watcher", result[0].Source);
        Assert.Equal("file:///C:/Users/Samik/Documents/report.pdf", result[0].SourceUri);
    }

    [Fact]
    public void FullFlow_DuplicateDetection_AcrossReplay()
    {
        // Write event, replay, write duplicate, replay again
        // Second replay should still return same count
        var init = new WorkspaceInitializer();
        init.Initialize(_workspace.Paths);

        var hasher = new ContentHasher();
        var writer = new RawEventWriter(_workspace.Paths, hasher);

        var evt = TestEvents.Create(text: "unique event");
        writer.Write(evt);

        var replay = new ReplayEnumerator(_workspace.Paths);
        var first = replay.EnumerateAll();
        Assert.Single(first);

        // Try to write duplicate
        var dupeResult = writer.Write(evt);
        Assert.Equal(WriteOutcome.Duplicate, dupeResult.Outcome);

        // Replay again — same count
        var second = replay.EnumerateAll();
        Assert.Single(second);
    }

    [Fact]
    public void FullFlow_NoCloudCredentials_Required()
    {
        // NFR-003: Tests must run locally without cloud credentials
        // This test verifies the full flow works with zero external dependencies
        // If this compiles and runs, no cloud APIs are needed

        var init = new WorkspaceInitializer();
        init.Initialize(_workspace.Paths);

        var hasher = new ContentHasher();
        var writer = new RawEventWriter(_workspace.Paths, hasher);
        var replay = new ReplayEnumerator(_workspace.Paths);

        var evt = TestEvents.Create(text: "no cloud needed");
        writer.Write(evt);

        var result = replay.EnumerateAll();
        Assert.Single(result);
    }

    [Fact]
    public void FullFlow_HashConsistency_AcrossWriteAndReplay()
    {
        var init = new WorkspaceInitializer();
        init.Initialize(_workspace.Paths);

        var hasher = new ContentHasher();
        var writer = new RawEventWriter(_workspace.Paths, hasher);

        var evt = TestEvents.Create();
        var writeResult = writer.Write(evt);

        var replay = new ReplayEnumerator(_workspace.Paths);
        var replayed = replay.EnumerateAll()[0];

        // Hash computed during write matches hash stored in file
        Assert.Equal(writeResult.Hash, replayed.Hash);

        // Hash is deterministic — computing again gives same result
        var recomputed = hasher.ComputeHash(replayed);
        Assert.Equal(writeResult.Hash, recomputed);
    }
}
