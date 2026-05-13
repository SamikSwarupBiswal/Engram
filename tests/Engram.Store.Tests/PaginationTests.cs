using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for pagination on replay.
/// </summary>
public class PaginationTests : IDisposable
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
    public void Limit_ReturnsOnlyRequestedCount()
    {
        var (writer, replay) = CreatePair();

        for (int i = 0; i < 10; i++)
            writer.Write(TestEvents.Create(text: $"event {i}"));

        var result = replay.Enumerate(new ReplayQuery { Limit = 3 });
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Offset_SkipsCorrectCount()
    {
        var (writer, replay) = CreatePair();

        for (int i = 0; i < 10; i++)
            writer.Write(TestEvents.Create(text: $"event {i}"));

        var all = replay.Enumerate(new ReplayQuery());
        var page = replay.Enumerate(new ReplayQuery { Offset = 5 });

        Assert.Equal(5, page.Count);
    }

    [Fact]
    public void LimitAndOffset_WorksTogether()
    {
        var (writer, replay) = CreatePair();

        for (int i = 0; i < 20; i++)
            writer.Write(TestEvents.Create(text: $"event {i}"));

        var page = replay.Enumerate(new ReplayQuery { Offset = 5, Limit = 3 });
        Assert.Equal(3, page.Count);
    }

    [Fact]
    public void Offset_BeyondTotal_ReturnsEmpty()
    {
        var (writer, replay) = CreatePair();
        writer.Write(TestEvents.Create(text: "only one"));

        var result = replay.Enumerate(new ReplayQuery { Offset = 100 });
        Assert.Empty(result);
    }

    [Fact]
    public void NoPagination_ReturnsAll()
    {
        var (writer, replay) = CreatePair();

        for (int i = 0; i < 5; i++)
            writer.Write(TestEvents.Create(text: $"event {i}"));

        var result = replay.Enumerate(new ReplayQuery());
        Assert.Equal(5, result.Count);
    }
}
