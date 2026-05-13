using Engram.Store.Wiki;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for wiki node persistence.
/// Production requirement: atomic writes, thread safety, corruption recovery.
/// </summary>
public class WikiNodeStoreTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new WikiNodeStore(_workspace.Paths);

        var node = CreateNode("test_node", "Test", WikiNodeType.Concept);
        store.Save(node);

        var loaded = store.Load("test_node");

        Assert.NotNull(loaded);
        Assert.Equal("Test", loaded!.Title);
        Assert.Equal(WikiNodeType.Concept, loaded.NodeType);
    }

    [Fact]
    public void Save_UsesAtomicWrite()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new WikiNodeStore(_workspace.Paths);

        store.Save(CreateNode("atomic_test", "Atomic", WikiNodeType.Project));

        var filePath = Path.Combine(_workspace.Paths.Wiki, "atomic_test.md");
        Assert.True(File.Exists(filePath));
        Assert.False(File.Exists(filePath + ".tmp"));
    }

    [Fact]
    public void Load_ReturnsNull_ForNonExistent()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new WikiNodeStore(_workspace.Paths);

        Assert.Null(store.Load("nonexistent_node_xyz"));
    }

    [Fact]
    public void Save_OverwritesExisting()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new WikiNodeStore(_workspace.Paths);

        store.Save(CreateNode("overwrite", "Original", WikiNodeType.Concept));
        store.Save(CreateNode("overwrite", "Updated", WikiNodeType.Concept));

        var loaded = store.Load("overwrite");
        Assert.Equal("Updated", loaded!.Title);
    }

    [Fact]
    public void LoadAll_ReturnsAllNodes()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new WikiNodeStore(_workspace.Paths);

        store.Save(CreateNode("node_a", "A", WikiNodeType.Person));
        store.Save(CreateNode("node_b", "B", WikiNodeType.Project));
        store.Save(CreateNode("node_c", "C", WikiNodeType.Goal));

        var all = store.LoadAll();
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public void LoadAll_SkipsIndexMd()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new WikiNodeStore(_workspace.Paths);

        store.Save(CreateNode("node_d", "D", WikiNodeType.Concept));

        // Write a fake index.md
        File.WriteAllText(Path.Combine(_workspace.Paths.Wiki, "index.md"), "# Index");

        var all = store.LoadAll();
        Assert.All(all, n => Assert.NotEqual("index", n.NodeId));
    }

    [Fact]
    public void LoadAll_SkipsMalformedFiles()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new WikiNodeStore(_workspace.Paths);

        store.Save(CreateNode("good_node", "Good", WikiNodeType.Concept));
        File.WriteAllText(Path.Combine(_workspace.Paths.Wiki, "bad.md"), "not a valid wiki node");

        var all = store.LoadAll();
        Assert.Single(all);
        Assert.Equal("good_node", all[0].NodeId);
    }

    [Fact]
    public void Exists_ReturnsTrueForSaved()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new WikiNodeStore(_workspace.Paths);

        store.Save(CreateNode("exists_test", "Exists", WikiNodeType.Concept));

        Assert.True(store.Exists("exists_test"));
        Assert.False(store.Exists("does_not_exist"));
    }

    [Fact]
    public void Save_CreatesWikiDirectory()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new WikiNodeStore(_workspace.Paths);

        store.Save(CreateNode("dir_test", "Dir", WikiNodeType.Concept));

        Assert.True(Directory.Exists(_workspace.Paths.Wiki));
    }

    private WikiNode CreateNode(string id, string title, WikiNodeType type)
    {
        return new WikiNode
        {
            NodeId = id,
            Title = title,
            NodeType = type,
            Summary = $"Test {type} node",
            Salience = 1.0,
            Confidence = 1.0
        };
    }
}
