using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for ReplayQuery filtering.
/// Derived from: D-014, D-015, REQ-006
///
/// Contract:
/// - Filter by date range (From, To)
/// - Filter by source
/// - Filter by processing_status (from sidecar)
/// - All filters optional, null = match all
/// - Deterministic ordering preserved
/// </summary>
public class ReplayQueryTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();
    private readonly ContentHasher _hasher = new();
    private readonly WorkspaceInitializer _init = new();

    public void Dispose() => _workspace.Dispose();

    private ReplayEnumerator CreateReplay()
    {
        _init.Initialize(_workspace.Paths);
        return new ReplayEnumerator(_workspace.Paths);
    }

    private RawEventWriter CreateWriter()
    {
        _init.Initialize(_workspace.Paths);
        return new RawEventWriter(_workspace.Paths, _hasher);
    }

    [Fact]
    public void Query_NoFilters_ReturnsAll()
    {
        var writer = CreateWriter();
        var replay = CreateReplay();

        writer.Write(TestEvents.Create(text: "a", capturedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        writer.Write(TestEvents.Create(text: "b", capturedAt: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)));

        var result = replay.Enumerate(new ReplayQuery());

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Query_FilterByFromDate_ExcludesEarlier()
    {
        var writer = CreateWriter();
        var replay = CreateReplay();

        writer.Write(TestEvents.Create(text: "jan", capturedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        writer.Write(TestEvents.Create(text: "june", capturedAt: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)));

        var result = replay.Enumerate(new ReplayQuery { FromDate = new DateOnly(2026, 3, 1) });

        Assert.Single(result);
        Assert.Equal("june", result[0].Text);
    }

    [Fact]
    public void Query_FilterByToDate_ExcludesLater()
    {
        var writer = CreateWriter();
        var replay = CreateReplay();

        writer.Write(TestEvents.Create(text: "jan", capturedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        writer.Write(TestEvents.Create(text: "june", capturedAt: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)));

        var result = replay.Enumerate(new ReplayQuery { ToDate = new DateOnly(2026, 3, 1) });

        Assert.Single(result);
        Assert.Equal("jan", result[0].Text);
    }

    [Fact]
    public void Query_FilterByDateRange_IncludesInclusive()
    {
        var writer = CreateWriter();
        var replay = CreateReplay();

        writer.Write(TestEvents.Create(text: "jan", capturedAt: new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero)));
        writer.Write(TestEvents.Create(text: "feb", capturedAt: new DateTimeOffset(2026, 2, 15, 0, 0, 0, TimeSpan.Zero)));
        writer.Write(TestEvents.Create(text: "mar", capturedAt: new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero)));

        var result = replay.Enumerate(new ReplayQuery
        {
            FromDate = new DateOnly(2026, 1, 1),
            ToDate = new DateOnly(2026, 2, 28)
        });

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Query_FilterBySource()
    {
        var writer = CreateWriter();
        var replay = CreateReplay();

        writer.Write(TestEvents.Create(source: "ocr", text: "from ocr"));
        writer.Write(TestEvents.Create(source: "file_watcher", text: "from file"));

        var result = replay.Enumerate(new ReplayQuery { Source = "ocr" });

        Assert.Single(result);
        Assert.Equal("from ocr", result[0].Text);
    }

    [Fact]
    public void Query_FilterBySource_CaseInsensitive()
    {
        var writer = CreateWriter();
        var replay = CreateReplay();

        writer.Write(TestEvents.Create(source: "OCR"));

        var result = replay.Enumerate(new ReplayQuery { Source = "ocr" });

        Assert.Single(result);
    }

    [Fact]
    public void Query_FilterByProcessingStatus()
    {
        var writer = CreateWriter();
        var replay = CreateReplay();
        var sidecar = new ProcessingSidecar(_workspace.Paths);

        var r1 = writer.Write(TestEvents.Create(text: "pending one"));
        var r2 = writer.Write(TestEvents.Create(text: "done one"));

        sidecar.Write(r1.FilePath, new ProcessingState { Status = "pending" });
        sidecar.Write(r2.FilePath, new ProcessingState { Status = "processed" });

        var result = replay.Enumerate(new ReplayQuery { ProcessingStatus = "pending" });

        Assert.Single(result);
        Assert.Equal("pending one", result[0].Text);
    }

    [Fact]
    public void Query_CombinedFilters()
    {
        var writer = CreateWriter();
        var replay = CreateReplay();
        var sidecar = new ProcessingSidecar(_workspace.Paths);

        var r1 = writer.Write(TestEvents.Create(source: "ocr", capturedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        var r2 = writer.Write(TestEvents.Create(source: "ocr", capturedAt: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)));
        var r3 = writer.Write(TestEvents.Create(source: "file", capturedAt: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)));

        sidecar.Write(r1.FilePath, new ProcessingState { Status = "processed" });
        sidecar.Write(r2.FilePath, new ProcessingState { Status = "pending" });
        sidecar.Write(r3.FilePath, new ProcessingState { Status = "pending" });

        var result = replay.Enumerate(new ReplayQuery
        {
            Source = "ocr",
            FromDate = new DateOnly(2026, 3, 1),
            ProcessingStatus = "pending"
        });

        Assert.Single(result);
    }

    [Fact]
    public void Query_EmptyStore_ReturnsEmpty()
    {
        var replay = CreateReplay();
        var result = replay.Enumerate(new ReplayQuery { Source = "anything" });

        Assert.Empty(result);
    }
}
