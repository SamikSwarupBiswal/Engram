using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for the replay/import enumeration command.
/// Derived from: REQ-006, D-008, Implementation Plan §10
///
/// PRD Contract:
/// - "Provide a replay/import command that can read raw events
///    and hand them to later processing pipelines"
/// - "Replay enumerates multiple raw events in deterministic order"
/// - "Replay handles an empty raw store without failing"
/// - "Replay does not mutate raw event files"
/// </summary>
public class ReplayEnumeratorTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();
    private readonly ContentHasher _hasher = new();
    private readonly WorkspaceInitializer _init = new();

    public void Dispose() => _workspace.Dispose();

    private (RawEventWriter writer, ReplayEnumerator replay) CreatePair()
    {
        _init.Initialize(_workspace.Paths);
        return (new RawEventWriter(_workspace.Paths, _hasher),
                new ReplayEnumerator(_workspace.Paths));
    }

    [Fact]
    public void EnumerateAll_EmptyStore_ReturnsEmptyList()
    {
        // D-008: "Replay handles an empty raw store without failing"
        var (_, replay) = CreatePair();

        var result = replay.EnumerateAll();

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void EnumerateAll_ReturnsWrittenEvents()
    {
        var (writer, replay) = CreatePair();
        var evt = TestEvents.Create(text: "replay test");
        writer.Write(evt);

        var result = replay.EnumerateAll();

        Assert.Single(result);
        Assert.Equal("replay test", result[0].Text);
    }

    [Fact]
    public void EnumerateAll_PreservesAllFields()
    {
        var (writer, replay) = CreatePair();
        var original = TestEvents.CreateWithMetadata();
        writer.Write(original);

        var result = replay.EnumerateAll();
        var replayed = result[0];

        Assert.Equal(original.EventId, replayed.EventId);
        Assert.Equal(original.EventType, replayed.EventType);
        Assert.Equal(original.Source, replayed.Source);
        Assert.Equal(original.Text, replayed.Text);
        Assert.Equal(original.SourceUri, replayed.SourceUri);
        Assert.Equal(original.ActiveWindow, replayed.ActiveWindow);
        Assert.Equal(original.PrivacyClass, replayed.PrivacyClass);
        Assert.NotNull(replayed.Metadata);
        Assert.Equal("report.pdf", replayed.Metadata!["file_name"]);
    }

    [Fact]
    public void EnumerateAll_DeterministicOrder()
    {
        // D-008: "expose them to later processing without requiring passive capture sources"
        // Same input must always produce same order
        var (writer, replay) = CreatePair();

        var evt1 = TestEvents.Create(text: "first", capturedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var evt2 = TestEvents.Create(text: "second", capturedAt: new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));
        var evt3 = TestEvents.Create(text: "third", capturedAt: new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero));

        writer.Write(evt1);
        writer.Write(evt2);
        writer.Write(evt3);

        var result1 = replay.EnumerateAll();
        var result2 = replay.EnumerateAll();

        Assert.Equal(result1.Count, result2.Count);
        for (int i = 0; i < result1.Count; i++)
        {
            Assert.Equal(result1[i].EventId, result2[i].EventId);
        }
    }

    [Fact]
    public void EnumerateAll_DoesNotMutateFiles()
    {
        // D-008: "Replay does not mutate raw event files"
        var (writer, replay) = CreatePair();
        var evt = TestEvents.Create();
        var writeResult = writer.Write(evt);

        var originalContent = File.ReadAllText(writeResult.FilePath);
        var originalWriteTime = File.GetLastWriteTimeUtc(writeResult.FilePath);

        Thread.Sleep(50);
        replay.EnumerateAll();

        var afterContent = File.ReadAllText(writeResult.FilePath);
        var afterWriteTime = File.GetLastWriteTimeUtc(writeResult.FilePath);

        Assert.Equal(originalContent, afterContent);
        Assert.Equal(originalWriteTime, afterWriteTime);
    }

    [Fact]
    public void EnumerateAll_SkipsNonJsonFiles()
    {
        var (writer, replay) = CreatePair();
        var evt = TestEvents.Create();
        writer.Write(evt);

        // Drop a non-JSON file in the raw directory
        var dateDir = Directory.GetDirectories(_workspace.Paths.Raw).First();
        File.WriteAllText(Path.Combine(dateDir, "readme.txt"), "not an event");

        var result = replay.EnumerateAll();

        Assert.Single(result); // Only the JSON event, not the txt file
    }

    [Fact]
    public void EnumerateAll_SkipsMalformedJson()
    {
        var (writer, replay) = CreatePair();
        var evt = TestEvents.Create();
        writer.Write(evt);

        // Drop a malformed JSON file
        var dateDir = Directory.GetDirectories(_workspace.Paths.Raw).First();
        File.WriteAllText(Path.Combine(dateDir, "bad.json"), "{ not valid json !!!");

        var result = replay.EnumerateAll();

        Assert.Single(result); // Only the valid event
    }

    [Fact]
    public void EnumerateAll_MultipleDays_EnumeratesAll()
    {
        var (writer, replay) = CreatePair();

        var evt1 = TestEvents.Create(capturedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var evt2 = TestEvents.Create(capturedAt: new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero));
        var evt3 = TestEvents.Create(capturedAt: new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero));

        writer.Write(evt1);
        writer.Write(evt2);
        writer.Write(evt3);

        var result = replay.EnumerateAll();

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void EnumerateAll_OrdersByDateThenFileName()
    {
        // Deterministic order: sorted by date folder, then by event ID within folder
        var (writer, replay) = CreatePair();

        var jan = TestEvents.Create(text: "jan", capturedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var dec = TestEvents.Create(text: "dec", capturedAt: new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero));

        // Write in reverse order
        writer.Write(dec);
        writer.Write(jan);

        var result = replay.EnumerateAll();

        // Should be sorted by date, so January comes first
        Assert.Equal("jan", result[0].Text);
        Assert.Equal("dec", result[1].Text);
    }
}
