using Engram.Store.Search;
using Engram.Store.Wiki;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for local search engine.
/// Production requirement: keyword search, ranking, multi-word, edge cases.
/// </summary>
public class SearchEngineTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void Search_EmptyQuery_ReturnsEmpty()
    {
        var engine = CreateEngine();
        var response = engine.Search("");
        Assert.Empty(response.Results);
    }

    [Fact]
    public void Search_NullQuery_ReturnsEmpty()
    {
        var engine = CreateEngine();
        var response = engine.Search(null!);
        Assert.Empty(response.Results);
    }

    [Fact]
    public void Search_EmptyWiki_ReturnsEmpty()
    {
        var engine = CreateEngine();
        var response = engine.Search("anything");
        Assert.Empty(response.Results);
    }

    [Fact]
    public void Search_MatchesTitle()
    {
        var engine = CreateEngineWithNodes(
            CreateNode("project_engram", "Engram Project", WikiNodeType.Project, "A semantic layer"),
            CreateNode("person_alice", "Alice Smith", WikiNodeType.Person, "A developer"));

        var response = engine.Search("engram");

        Assert.Single(response.Results);
        Assert.Equal("project_engram", response.Results[0].Node.NodeId);
    }

    [Fact]
    public void Search_MatchesSummary()
    {
        var engine = CreateEngineWithNodes(
            CreateNode("test", "Test Node", WikiNodeType.Concept, "Semantic memory layer for Windows"));

        var response = engine.Search("windows");

        Assert.Single(response.Results);
        Assert.Contains("summary", response.Results[0].MatchedFields);
    }

    [Fact]
    public void Search_MatchesFacts()
    {
        var node = CreateNode("test", "Test", WikiNodeType.Concept, "Summary");
        node.Facts.Add(new WikiFact { Text = "The project uses .NET 8 for Windows development" });

        var engine = CreateEngineWithNodes(node);
        var response = engine.Search("windows");

        Assert.Single(response.Results);
        Assert.Contains("facts", response.Results[0].MatchedFields);
        Assert.Single(response.Results[0].MatchingFacts);
    }

    [Fact]
    public void Search_CaseInsensitive()
    {
        var engine = CreateEngineWithNodes(
            CreateNode("test", "Engram Project", WikiNodeType.Project, "A test"));

        var response = engine.Search("ENGRAM");
        Assert.Single(response.Results);

        response = engine.Search("engram");
        Assert.Single(response.Results);
    }

    [Fact]
    public void Search_MultiWord_ANDSemantics()
    {
        var engine = CreateEngineWithNodes(
            CreateNode("a", "Engram Project", WikiNodeType.Project, "A semantic layer"),
            CreateNode("b", "Other Project", WikiNodeType.Project, "Something else entirely"));

        var response = engine.Search("engram semantic");

        Assert.Single(response.Results);
        Assert.Equal("a", response.Results[0].Node.NodeId);
    }

    [Fact]
    public void Search_MultiWord_NoMatch_IfMissingTerm()
    {
        var engine = CreateEngineWithNodes(
            CreateNode("a", "Engram Project", WikiNodeType.Project, "A semantic layer"));

        var response = engine.Search("engram nonexistent_term_xyz");

        Assert.Empty(response.Results);
    }

    [Fact]
    public void Search_RanksByRelevance()
    {
        var node1 = CreateNode("a", "Engram", WikiNodeType.Project, "Engram is a project");
        node1.Facts.Add(new WikiFact { Text = "Engram does semantic search" });

        var node2 = CreateNode("b", "Other", WikiNodeType.Concept, "Mentions engram once");

        var engine = CreateEngineWithNodes(node1, node2);
        var response = engine.Search("engram");

        Assert.Equal(2, response.Results.Count);
        Assert.Equal("a", response.Results[0].Node.NodeId); // Higher relevance
    }

    [Fact]
    public void Search_TitleMatch_RanksHigherThanSummary()
    {
        var node1 = CreateNode("a", "Engram", WikiNodeType.Project, "Something");
        var node2 = CreateNode("b", "Other", WikiNodeType.Concept, "Engram is mentioned here");

        var engine = CreateEngineWithNodes(node1, node2);
        var response = engine.Search("engram");

        Assert.Equal("a", response.Results[0].Node.NodeId); // Title match wins
    }

    [Fact]
    public void Search_MaxResults_LimitsOutput()
    {
        var nodes = Enumerable.Range(0, 50)
            .Select(i => CreateNode($"n{i}", $"Node {i} match", WikiNodeType.Concept, "test"))
            .ToArray();

        var engine = CreateEngineWithNodes(nodes);
        var response = engine.Search("match", maxResults: 5);

        Assert.Equal(5, response.Results.Count);
    }

    [Fact]
    public void Search_InvalidateIndex_RebuildsOnNextSearch()
    {
        var engine = CreateEngineWithNodes(
            CreateNode("a", "First", WikiNodeType.Concept, "test"));

        engine.Search("first"); // Builds index

        // Add a new node
        var store = new WikiNodeStore(_workspace.Paths);
        store.Save(CreateNode("b", "Second", WikiNodeType.Concept, "test"));

        engine.InvalidateIndex();
        var response = engine.Search("second");

        Assert.Single(response.Results);
        Assert.Equal("b", response.Results[0].Node.NodeId);
    }

    [Fact]
    public void Search_Duration_Tracked()
    {
        var engine = CreateEngineWithNodes(
            CreateNode("a", "Test", WikiNodeType.Concept, "test"));

        var response = engine.Search("test");

        Assert.True(response.Duration >= TimeSpan.Zero);
    }

    [Fact]
    public void Search_NodesSearched_Tracked()
    {
        var engine = CreateEngineWithNodes(
            CreateNode("a", "A", WikiNodeType.Concept, "test"),
            CreateNode("b", "B", WikiNodeType.Concept, "test"));

        var response = engine.Search("test");

        Assert.Equal(2, response.NodesSearched);
    }

    [Fact]
    public void Search_ShortTerms_Skipped()
    {
        var engine = CreateEngineWithNodes(
            CreateNode("a", "Test Node", WikiNodeType.Concept, "test"));

        // Single char terms should be skipped
        var response = engine.Search("a");
        Assert.Empty(response.Results);
    }

    private SearchEngine CreateEngine()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        var store = new WikiNodeStore(_workspace.Paths);
        return new SearchEngine(store);
    }

    private SearchEngine CreateEngineWithNodes(params WikiNode[] nodes)
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        var store = new WikiNodeStore(_workspace.Paths);
        foreach (var node in nodes)
            store.Save(node);
        return new SearchEngine(store);
    }

    private WikiNode CreateNode(string id, string title, WikiNodeType type, string summary)
    {
        return new WikiNode
        {
            NodeId = id,
            Title = title,
            NodeType = type,
            Summary = summary,
            Salience = 1.0,
            Confidence = 1.0
        };
    }
}
