using Engram.Store.Salience;
using Engram.Store.Wiki;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for archive management.
/// Production requirement: stale nodes moved to archives/, restore works.
/// </summary>
public class ArchiveManagerTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void GetArchiveCandidates_ReturnsStaleNodes()
    {
        var (store, scorer, manager) = CreateStack();

        var stale = CreateNode("stale", "Stale", WikiNodeType.Concept);
        stale.LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-200);
        store.Save(stale);

        var fresh = CreateNode("fresh", "Fresh", WikiNodeType.Concept);
        fresh.LastTouchedAt = DateTimeOffset.UtcNow;
        store.Save(fresh);

        var candidates = manager.GetArchiveCandidates();

        Assert.Single(candidates);
        Assert.Equal("stale", candidates[0].NodeId);
    }

    [Fact]
    public void ArchiveNode_MovesToArchives()
    {
        var (store, scorer, manager) = CreateStack();

        var node = CreateNode("to_archive", "To Archive", WikiNodeType.Concept);
        node.LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-200);
        store.Save(node);

        var result = manager.ArchiveNode(node);

        Assert.True(result);
        Assert.False(store.Exists("to_archive")); // Removed from wiki
        Assert.True(File.Exists(Path.Combine(_workspace.Paths.Archives, "to_archive.md"))); // In archives
    }

    [Fact]
    public void ArchiveNode_PreservesContent()
    {
        var (store, scorer, manager) = CreateStack();

        var node = CreateNode("preserve", "Preserve Me", WikiNodeType.Project);
        node.Summary = "Important summary";
        node.Facts.Add(new WikiFact { Text = "Key fact" });
        node.LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-200);
        store.Save(node);

        manager.ArchiveNode(node);

        var archived = manager.ListArchived();
        Assert.Single(archived);
        Assert.Equal("Preserve Me", archived[0].Title);
        Assert.Equal("Important summary", archived[0].Summary);
    }

    [Fact]
    public void ArchiveStaleNodes_ArchivesAll()
    {
        var (store, scorer, manager) = CreateStack();

        for (int i = 0; i < 3; i++)
        {
            var node = CreateNode($"stale_{i}", $"Stale {i}", WikiNodeType.Concept);
            node.LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-200);
            store.Save(node);
        }

        var archived = manager.ArchiveStaleNodes();

        Assert.Equal(3, archived.Count);
        Assert.Empty(store.LoadAll()); // All moved
    }

    [Fact]
    public void RestoreFromArchive_MovesBackToWiki()
    {
        var (store, scorer, manager) = CreateStack();

        var node = CreateNode("restore_me", "Restore Me", WikiNodeType.Concept);
        node.LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-200);
        store.Save(node);

        manager.ArchiveNode(node);
        Assert.False(store.Exists("restore_me"));

        var restored = manager.RestoreFromArchive("restore_me");

        Assert.True(restored);
        Assert.True(store.Exists("restore_me"));

        var loaded = store.Load("restore_me");
        Assert.Equal(1.0, loaded!.Salience); // Reset on restore
    }

    [Fact]
    public void RestoreFromArchive_ReturnsFalse_ForNonExistent()
    {
        var (_, _, manager) = CreateStack();

        Assert.False(manager.RestoreFromArchive("nonexistent_xyz"));
    }

    [Fact]
    public void ListArchived_ReturnsArchivedNodes()
    {
        var (store, _, manager) = CreateStack();

        var node = CreateNode("archived", "Archived", WikiNodeType.Concept);
        node.LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-200);
        store.Save(node);

        manager.ArchiveNode(node);

        var archived = manager.ListArchived();
        Assert.Single(archived);
        Assert.Equal("Archived", archived[0].Title);
    }

    [Fact]
    public void ListArchived_SkipsIndexMd()
    {
        var (store, _, manager) = CreateStack();

        var node = CreateNode("test", "Test", WikiNodeType.Concept);
        node.LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-200);
        store.Save(node);

        manager.ArchiveNode(node);

        var archived = manager.ListArchived();
        Assert.All(archived, n => Assert.NotEqual("index", n.NodeId));
    }

    [Fact]
    public void ArchiveStaleNodes_GeneratesArchiveIndex()
    {
        var (store, _, manager) = CreateStack();

        var node = CreateNode("indexed", "Indexed", WikiNodeType.Concept);
        node.LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-200);
        store.Save(node);

        manager.ArchiveStaleNodes();

        var indexPath = Path.Combine(_workspace.Paths.Archives, "index.md");
        Assert.True(File.Exists(indexPath));
        Assert.Contains("Indexed", File.ReadAllText(indexPath));
    }

    private (WikiNodeStore store, SalienceScorer scorer, ArchiveManager manager) CreateStack()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        var store = new WikiNodeStore(_workspace.Paths);
        var scorer = new SalienceScorer();
        var manager = new ArchiveManager(store, scorer, _workspace.Paths);
        return (store, scorer, manager);
    }

    private WikiNode CreateNode(string id, string title, WikiNodeType type)
    {
        return new WikiNode
        {
            NodeId = id,
            Title = title,
            NodeType = type,
            Summary = "Test",
            Salience = 1.0,
            Confidence = 1.0,
            LastTouchedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
