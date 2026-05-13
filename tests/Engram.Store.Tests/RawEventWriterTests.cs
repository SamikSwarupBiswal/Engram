using System.Text.Json;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for the append-only raw event writer.
/// Derived from: REQ-004, REQ-005, D-006, D-007
///
/// PRD Contract:
/// - "Structure: .engram/raw/[YYYY-MM-DD]/[Event_ID].json"
/// - "Files are append-only"
/// - "Duplicate detection uses deterministic content hashing"
/// - "must not rewrite existing raw payload files"
/// - "A new event creates one JSON file in the date folder"
/// - "Rewriting the same event does not update the existing file timestamp or payload"
/// </summary>
public class RawEventWriterTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();
    private readonly ContentHasher _hasher = new();
    private readonly WorkspaceInitializer _init = new();

    public void Dispose() => _workspace.Dispose();

    private RawEventWriter CreateWriter()
    {
        _init.Initialize(_workspace.Paths);
        return new RawEventWriter(_workspace.Paths, _hasher);
    }

    [Fact]
    public void Write_CreatesJsonFile_InDatePartitionedDirectory()
    {
        // REQ-004: .engram/raw/YYYY-MM-DD/[event_id].json
        var writer = CreateWriter();
        var evt = TestEvents.Create(
            capturedAt: new DateTimeOffset(2026, 5, 13, 14, 30, 0, TimeSpan.Zero));

        var result = writer.Write(evt);

        Assert.Equal(WriteOutcome.Created, result.Outcome);
        Assert.True(File.Exists(result.FilePath), "Event file must exist");

        // Verify date partitioning
        Assert.Contains(Path.Combine("raw", "2026-05-13"), result.FilePath);
        Assert.EndsWith($"{evt.EventId}.json", result.FilePath);
    }

    [Fact]
    public void Write_CreatedFile_ContainsValidJson()
    {
        var writer = CreateWriter();
        var evt = TestEvents.Create(text: "test content for JSON validation");

        var result = writer.Write(evt);
        var json = File.ReadAllText(result.FilePath);
        var deserialized = JsonSerializer.Deserialize<RawEvent>(json,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });

        Assert.NotNull(deserialized);
        Assert.Equal(evt.EventId, deserialized.EventId);
        Assert.Equal("test content for JSON validation", deserialized.Text);
    }

    [Fact]
    public void Write_CreatedFile_HasHashPopulated()
    {
        // D-007: Hash must be computed and stored
        var writer = CreateWriter();
        var evt = TestEvents.Create();

        var result = writer.Write(evt);

        Assert.False(string.IsNullOrEmpty(result.Hash));
        Assert.NotEqual(string.Empty, result.Hash);
    }

    [Fact]
    public void Write_SameContentTwice_ReturnsDuplicate()
    {
        // REQ-005, D-007: "prevent duplicate raw events"
        var writer = CreateWriter();
        var evt = TestEvents.Create(text: "duplicate test");
        evt.CapturedAt = new DateTimeOffset(2026, 5, 13, 10, 0, 0, TimeSpan.Zero);
        evt.EventType = "test";
        evt.Source = "test";

        var first = writer.Write(evt);
        var second = writer.Write(evt);

        Assert.Equal(WriteOutcome.Created, first.Outcome);
        Assert.Equal(WriteOutcome.Duplicate, second.Outcome);
    }

    [Fact]
    public void Write_Duplicate_DoesNotRewriteExistingFile()
    {
        // D-007: "must not rewrite existing raw payload files"
        // PRD: "Rewriting the same event does not update the existing file timestamp or payload"
        var writer = CreateWriter();
        var evt = TestEvents.Create(text: "immutable test");
        evt.CapturedAt = new DateTimeOffset(2026, 5, 13, 10, 0, 0, TimeSpan.Zero);
        evt.EventType = "test";
        evt.Source = "test";

        var first = writer.Write(evt);
        var originalContent = File.ReadAllText(first.FilePath);
        var originalWriteTime = File.GetLastWriteTimeUtc(first.FilePath);

        // Small delay to ensure timestamp would differ if file were rewritten
        Thread.Sleep(50);

        writer.Write(evt);

        var afterContent = File.ReadAllText(first.FilePath);
        var afterWriteTime = File.GetLastWriteTimeUtc(first.FilePath);

        Assert.Equal(originalContent, afterContent);
        Assert.Equal(originalWriteTime, afterWriteTime);
    }

    [Fact]
    public void Write_Duplicate_ReturnsSameFilePath()
    {
        var writer = CreateWriter();
        var evt = TestEvents.Create();
        evt.CapturedAt = new DateTimeOffset(2026, 5, 13, 10, 0, 0, TimeSpan.Zero);
        evt.EventType = "test";
        evt.Source = "test";

        var first = writer.Write(evt);
        var second = writer.Write(evt);

        Assert.Equal(first.FilePath, second.FilePath);
    }

    [Fact]
    public void Write_DifferentContent_CreatesSeparateFiles()
    {
        var writer = CreateWriter();
        var evt1 = TestEvents.Create(text: "event A");
        var evt2 = TestEvents.Create(text: "event B");

        var result1 = writer.Write(evt1);
        var result2 = writer.Write(evt2);

        Assert.Equal(WriteOutcome.Created, result1.Outcome);
        Assert.Equal(WriteOutcome.Created, result2.Outcome);
        Assert.NotEqual(result1.FilePath, result2.FilePath);
        Assert.True(File.Exists(result1.FilePath));
        Assert.True(File.Exists(result2.FilePath));
    }

    [Fact]
    public void Write_DifferentDates_PartitionsCorrectly()
    {
        var writer = CreateWriter();
        var evt1 = TestEvents.Create(capturedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var evt2 = TestEvents.Create(capturedAt: new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero));

        var result1 = writer.Write(evt1);
        var result2 = writer.Write(evt2);

        Assert.Contains("2026-01-01", result1.FilePath);
        Assert.Contains("2026-12-31", result2.FilePath);
    }

    [Fact]
    public void Write_SetsHashOnReturnedEvent()
    {
        var writer = CreateWriter();
        var evt = TestEvents.Create();

        var result = writer.Write(evt);

        // Read back the file and verify hash is populated
        var json = File.ReadAllText(result.FilePath);
        var deserialized = JsonSerializer.Deserialize<RawEvent>(json,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;

        Assert.False(string.IsNullOrEmpty(deserialized.Hash));
        Assert.Equal(result.Hash, deserialized.Hash);
    }

    [Fact]
    public void Write_ThrowsOnNullEvent()
    {
        var writer = CreateWriter();
        Assert.Throws<ArgumentNullException>(() => writer.Write(null!));
    }

    [Fact]
    public void Write_CreatesDateDirectory_IfNotExists()
    {
        var writer = CreateWriter();
        var evt = TestEvents.Create(
            capturedAt: new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero));

        var result = writer.Write(evt);

        var expectedDir = Path.Combine(_workspace.Paths.Raw, "2026-03-15");
        Assert.True(Directory.Exists(expectedDir));
    }

    [Fact]
    public void Write_MultipleEventsOnSameDay_AllPersist()
    {
        var writer = CreateWriter();
        var date = new DateTimeOffset(2026, 5, 13, 0, 0, 0, TimeSpan.Zero);

        var events = Enumerable.Range(0, 10)
            .Select(i => TestEvents.Create(
                text: $"event {i}",
                capturedAt: date.AddHours(i)))
            .ToList();

        var results = events.Select(e => writer.Write(e)).ToList();

        Assert.All(results, r => Assert.Equal(WriteOutcome.Created, r.Outcome));
        Assert.Equal(10, results.Select(r => r.FilePath).Distinct().Count());
    }
}
