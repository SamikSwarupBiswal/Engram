using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for write-ahead log crash recovery.
/// </summary>
public class WriteAheadLogTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void GetUncommittedWrites_EmptyWaleturnsEmpty()
    {
        Directory.CreateDirectory(_workspace.Paths.Raw);
        using var wal = new WriteAheadLog(_workspace.Paths.Raw);

        Assert.Empty(wal.GetUncommittedWrites());
    }

    [Fact]
    public void GetUncommittedWrites_FindsUncommittedWrite()
    {
        Directory.CreateDirectory(_workspace.Paths.Raw);
        using var wal = new WriteAheadLog(_workspace.Paths.Raw);

        wal.LogWrite("evt-1", "hash-1", "/path/evt-1.json");

        var uncommitted = wal.GetUncommittedWrites();
        Assert.Single(uncommitted);
        Assert.Equal("evt-1", uncommitted[0].EventId);
    }

    [Fact]
    public void GetUncommittedWrites_CommittedWriteNotReturned()
    {
        Directory.CreateDirectory(_workspace.Paths.Raw);
        using var wal = new WriteAheadLog(_workspace.Paths.Raw);

        wal.LogWrite("evt-1", "hash-1", "/path/evt-1.json");
        wal.LogCommit("evt-1");

        Assert.Empty(wal.GetUncommittedWrites());
    }

    [Fact]
    public void GetUncommittedWrites_MixedCommittedAndUncommitted()
    {
        Directory.CreateDirectory(_workspace.Paths.Raw);
        using var wal = new WriteAheadLog(_workspace.Paths.Raw);

        wal.LogWrite("evt-1", "h1", "/p1.json");
        wal.LogCommit("evt-1");
        wal.LogWrite("evt-2", "h2", "/p2.json"); // no commit

        var uncommitted = wal.GetUncommittedWrites();
        Assert.Single(uncommitted);
        Assert.Equal("evt-2", uncommitted[0].EventId);
    }

    [Fact]
    public void Clear_RemovesWaleFile()
    {
        Directory.CreateDirectory(_workspace.Paths.Raw);
        using var wal = new WriteAheadLog(_workspace.Paths.Raw);

        wal.LogWrite("evt-1", "h1", "/p1.json");
        wal.Clear();

        Assert.Empty(wal.GetUncommittedWrites());
    }

    [Fact]
    public void SurvivesRestart()
    {
        Directory.CreateDirectory(_workspace.Paths.Raw);

        using (var wal = new WriteAheadLog(_workspace.Paths.Raw))
        {
            wal.LogWrite("evt-1", "h1", "/p1.json");
            wal.LogCommit("evt-1");
            wal.LogWrite("evt-2", "h2", "/p2.json"); // crash before commit
        }

        // Simulate restart
        using var wal2 = new WriteAheadLog(_workspace.Paths.Raw);
        var uncommitted = wal2.GetUncommittedWrites();

        Assert.Single(uncommitted);
        Assert.Equal("evt-2", uncommitted[0].EventId);
    }
}
