using Engram.Store.Wiki;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for wiki index generation.
/// Production requirement: grouped by type, [[links]], stale markers.
/// </summary>
public class IndexGeneratorTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void Generate_EmptyWiki_ProducesValidIndex()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new WikiNodeStore(_workspace.Paths);
        var generator = new IndexGenerator(store);

        var index = generator.Generate();

        Assert.Contains("# Engram Wiki Index", index);
        Assert.Contains("Total nodes: 0", index);
    }

    [Fact]
    public void Generate_GroupsByNodeType()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new WikiNodeStore(_workspace.Paths);

        store.Save(CreateNode("person_alice", "Alice", WikiNodeType.Person));
        store.Save(CreateNode("project_engram", "Engram", WikiNodeType.Project));
        store.Save(CreateNode("concept_ai", "AI", WikiNodeType.Concept));

        var generator = new IndexGenerator(store);
        var index = generator.Generate();

        Assert.Contains("## Person (1)", index);
        Assert.Contains("## Project (1)", index);
        Assert.Contains("## Concept (1)", index);
    }

    [Fact]
    public void Generate_IncludesWikiLinks()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new WikiNodeStore(_workspace.Paths);

        store.Save(CreateNode("test_link", "Test Link", WikiNodeType.Concept));

        var generator = new IndexGenerator(store);
        var index = generator.Generate();

        Assert.Contains("[[test_link]]", index);
    }

    [Fact]
    public void Generate_IncludesSummary()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new WikiNodeStore(_workspace.Paths);

        store.Save(CreateNode("with_summary", "Summary Test", WikiNodeType.Project, "A detailed project summary"));

        var generator = new IndexGenerator(store);
        var index = generator.Generate();

        Assert.Contains("A detailed project summary", index);
    }

    [Fact]
    public void Generate_MarksStaleNodes()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new WikiNodeStore(_workspace.Paths);

        var node = CreateNode("stale_node", "Stale", WikiNodeType.Concept);
        node.LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-60);
        store.Save(node);

        var generator = new IndexGenerator(store);
        var index = generator.Generate();

        Assert.Contains("⚠️", index);
    }

    [Fact]
    public void Generate_RecentChanges_Section()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new WikiNodeStore(_workspace.Paths);

        store.Save(CreateNode("recent_1", "Recent 1", WikiNodeType.Concept));
        store.Save(CreateNode("recent_2", "Recent 2", WikiNodeType.Project));

        var generator = new IndexGenerator(store);
        var index = generator.Generate();

        Assert.Contains("## Recently Updated", index);
        Assert.Contains("[[recent_1]]", index);
        Assert.Contains("[[recent_2]]", index);
    }

    [Fact]
    public void SaveIndex_WritesToWikiDirectory()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new WikiNodeStore(_workspace.Paths);

        store.Save(CreateNode("save_test", "Save", WikiNodeType.Concept));

        var generator = new IndexGenerator(store);
        generator.SaveIndex(_workspace.Paths.Wiki);

        var indexPath = Path.Combine(_workspace.Paths.Wiki, "index.md");
        Assert.True(File.Exists(indexPath));
        Assert.Contains("# Engram Wiki Index", File.ReadAllText(indexPath));
    }

    [Fact]
    public void SaveIndex_UsesAtomicWrite()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new WikiNodeStore(_workspace.Paths);

        var generator = new IndexGenerator(store);
        generator.SaveIndex(_workspace.Paths.Wiki);

        var indexPath = Path.Combine(_workspace.Paths.Wiki, "index.md");
        Assert.True(File.Exists(indexPath));
        Assert.False(File.Exists(indexPath + ".tmp"));
    }

    private WikiNode CreateNode(string id, string title, WikiNodeType type, string summary = "")
    {
        return new WikiNode
        {
            NodeId = id,
            Title = title,
            NodeType = type,
            Summary = string.IsNullOrWhiteSpace(summary) ? $"Test {type}" : summary,
            Salience = 1.0,
            Confidence = 1.0,
            LastTouchedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
