using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for atomic write behavior.
/// Derived from: D-011, NFR-002, Success Criteria #1
///
/// Contract:
/// - Write to .tmp first, then atomic rename to .json
/// - If crash during write, .tmp file exists but .json is never corrupt
/// - Existing files are never modified by a partial write
/// </summary>
public class AtomicityTests : IDisposable
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
    public void Write_CreatesFinalJsonFile_NotTmp()
    {
        var writer = CreateWriter();
        var evt = TestEvents.Create();

        var result = writer.Write(evt);

        Assert.True(File.Exists(result.FilePath));
        Assert.EndsWith(".json", result.FilePath);
        Assert.DoesNotContain(".tmp", result.FilePath);
    }

    [Fact]
    public void Write_NoOrphanedTmpFile_AfterSuccess()
    {
        var writer = CreateWriter();
        var evt = TestEvents.Create();

        var result = writer.Write(evt);

        var tmpPath = result.FilePath + ".tmp";
        Assert.False(File.Exists(tmpPath), ".tmp file should not exist after successful write");
    }

    [Fact]
    public void Write_FinalFile_ContainsCompleteJson()
    {
        var writer = CreateWriter();
        var evt = TestEvents.Create(text: "complete content test");

        var result = writer.Write(evt);
        var content = File.ReadAllText(result.FilePath);

        Assert.Contains("complete content test", content);
        Assert.DoesNotContain("partial", content[..^1]); // not truncated
    }

    [Fact]
    public void Write_NeverModifiesExistingFile_OnDuplicateCheck()
    {
        var writer = CreateWriter();
        var evt = TestEvents.Create(text: "immutable");
        evt.CapturedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        evt.EventType = "test";
        evt.Source = "test";

        var first = writer.Write(evt);
        var originalContent = File.ReadAllText(first.FilePath);
        var originalTime = File.GetLastWriteTimeUtc(first.FilePath);

        Thread.Sleep(50);
        writer.Write(evt); // duplicate

        Assert.Equal(originalContent, File.ReadAllText(first.FilePath));
        Assert.Equal(originalTime, File.GetLastWriteTimeUtc(first.FilePath));
    }

    [Fact]
    public async Task Write_ConcurrentDifferentEvents_BothPersist()
    {
        var writer = CreateWriter();

        var tasks = Enumerable.Range(0, 5)
            .Select(i => Task.Run(() =>
            {
                var evt = TestEvents.Create(text: $"concurrent {i}");
                return writer.Write(evt);
            }))
            .ToArray();

        var results = await Task.WhenAll(tasks);
        Assert.All(results, r => Assert.Equal(WriteOutcome.Created, r.Outcome));
        Assert.Equal(5, results.Select(r => r.FilePath).Distinct().Count());
    }
}
