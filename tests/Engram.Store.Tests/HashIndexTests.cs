using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for O(1) hash index.
/// </summary>
public class HashIndexTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void TryGet_ReturnsFalse_OnEmptyIndex()
    {
        Directory.CreateDirectory(_workspace.Paths.Raw);
        using var index = new HashIndex(_workspace.Paths.Raw);

        Assert.False(index.TryGet("nonexistent", out _));
    }

    [Fact]
    public void Add_ThenTryGet_ReturnsTrue()
    {
        Directory.CreateDirectory(_workspace.Paths.Raw);
        using var index = new HashIndex(_workspace.Paths.Raw);

        index.Add("abc123", "/path/to/event.json");

        Assert.True(index.TryGet("abc123", out var path));
        Assert.Equal("/path/to/event.json", path);
    }

    [Fact]
    public void Persist_SurvivesReload()
    {
        Directory.CreateDirectory(_workspace.Paths.Raw);

        using (var index = new HashIndex(_workspace.Paths.Raw))
        {
            index.Add("hash1", "/path1.json");
            index.Add("hash2", "/path2.json");
        }

        // New instance should load from disk
        using var index2 = new HashIndex(_workspace.Paths.Raw);
        Assert.True(index2.TryGet("hash1", out var p1));
        Assert.Equal("/path1.json", p1);
        Assert.True(index2.TryGet("hash2", out var p2));
        Assert.Equal("/path2.json", p2);
    }

    [Fact]
    public void Count_TracksEntries()
    {
        Directory.CreateDirectory(_workspace.Paths.Raw);
        using var index = new HashIndex(_workspace.Paths.Raw);

        Assert.Equal(0, index.Count);

        index.Add("a", "/a.json");
        index.Add("b", "/b.json");

        Assert.Equal(2, index.Count);
    }

    [Fact]
    public void Overwrite_UpdatesExistingHash()
    {
        Directory.CreateDirectory(_workspace.Paths.Raw);
        using var index = new HashIndex(_workspace.Paths.Raw);

        index.Add("hash", "/old.json");
        index.Add("hash", "/new.json");

        Assert.True(index.TryGet("hash", out var path));
        Assert.Equal("/new.json", path);
    }
}
