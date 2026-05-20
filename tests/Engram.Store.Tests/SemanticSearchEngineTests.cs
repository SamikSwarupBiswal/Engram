using Engram.Store.Search;
using Engram.Store.Wiki;
using Xunit;

namespace Engram.Store.Tests;

public class SemanticSearchEngineTests : IDisposable
{
    private readonly string _tempDir;
    private readonly WorkspacePaths _paths;
    private readonly WikiNodeStore _nodeStore;
    private readonly SemanticSearchEngine _search;

    public SemanticSearchEngineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram_semsearch_test_" + Guid.NewGuid().ToString("n")[..8]);
        _paths = new WorkspacePaths(_tempDir);
        _nodeStore = new WikiNodeStore(_paths);
        _search = new SemanticSearchEngine(_nodeStore);
    }

    public void Dispose()
    {
        _search.Dispose();
        _nodeStore.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private void SeedTestData()
    {
        _nodeStore.Save(new WikiNode
        {
            NodeId = "project_engram",
            Title = "Engram",
            NodeType = WikiNodeType.Project,
            Summary = "A semantic memory system for personal AI",
            Facts = new List<WikiFact>
            {
                new() { Text = "Built with .NET 8 and LLamaSharp" },
                new() { Text = "Uses Phi-4-mini for local inference" }
            },
            Salience = 1.0
        });

        _nodeStore.Save(new WikiNode
        {
            NodeId = "person_alex",
            Title = "Alex",
            NodeType = WikiNodeType.Person,
            Summary = "Friend and colleague",
            Facts = new List<WikiFact>
            {
                new() { Text = "Helped with the frontend design" }
            },
            Salience = 0.8
        });

        _nodeStore.Save(new WikiNode
        {
            NodeId = "goal_ship",
            Title = "Ship Engram v1",
            NodeType = WikiNodeType.Goal,
            Summary = "Ship the first version of Engram",
            Facts = new List<WikiFact>
            {
                new() { Text = "Target: Q2 2026" }
            },
            Salience = 0.9
        });

        _nodeStore.Save(new WikiNode
        {
            NodeId = "decision_dotnet",
            Title = "Use .NET for backend",
            NodeType = WikiNodeType.Decision,
            Summary = "Decided to use .NET 8 for the API sidecar",
            Facts = new List<WikiFact>
            {
                new() { Text = "Better performance than Node.js for this use case" }
            },
            Salience = 0.7
        });

        _search.InvalidateIndex();
    }

    // ── Basic Search ──

    [Fact]
    public void Search_FindsExactTitleMatch()
    {
        SeedTestData();

        var results = _search.Search("Engram");

        Assert.True(results.Results.Count > 0);
        Assert.Contains(results.Results, r => r.Node.Title == "Engram");
    }

    [Fact]
    public void Search_FindsFactMatch()
    {
        SeedTestData();

        var results = _search.Search("LLamaSharp");

        Assert.True(results.Results.Count > 0);
        Assert.Contains(results.Results, r => r.MatchingFacts.Any(f => f.Text.Contains("LLamaSharp")));
    }

    [Fact]
    public void Search_FindsSummaryMatch()
    {
        SeedTestData();

        var results = _search.Search("semantic memory");

        Assert.True(results.Results.Count > 0);
        Assert.Contains(results.Results, r => r.Node.Summary.Contains("semantic memory"));
    }

    // ── Salience Weighting ──

    [Fact]
    public void Search_HigherSalienceRanksHigher()
    {
        // Create nodes with same term but different salience
        _nodeStore.Save(new WikiNode
        {
            NodeId = "high_sal",
            Title = "Alpha Project",
            NodeType = WikiNodeType.Project,
            Summary = "The alpha widget system",
            Facts = new List<WikiFact> { new() { Text = "alpha is important" } },
            Salience = 1.0
        });
        _nodeStore.Save(new WikiNode
        {
            NodeId = "low_sal",
            Title = "Alpha Notes",
            NodeType = WikiNodeType.Concept,
            Summary = "Old notes about alpha",
            Facts = new List<WikiFact> { new() { Text = "alpha mentioned once" } },
            Salience = 0.3
        });
        _search.InvalidateIndex();

        var results = _search.Search("alpha");

        Assert.True(results.Results.Count >= 2);
        // Higher salience node should rank higher
        Assert.Equal("high_sal", results.Results[0].Node.NodeId);
    }

    // ── Type Boosting ──

    [Fact]
    public void Search_PersonTypeGetsBoost()
    {
        // Create two nodes with same relevance but different types
        _nodeStore.Save(new WikiNode
        {
            NodeId = "person_sam",
            Title = "Sam",
            NodeType = WikiNodeType.Person,
            Summary = "A person named Sam",
            Salience = 0.5
        });
        _nodeStore.Save(new WikiNode
        {
            NodeId = "doc_sam",
            Title = "Sam document",
            NodeType = WikiNodeType.Document,
            Summary = "A document about Sam",
            Salience = 0.5
        });
        _search.InvalidateIndex();

        var results = _search.Search("Sam");

        Assert.True(results.Results.Count >= 2);
        // Person should rank higher than Document
        Assert.Equal(WikiNodeType.Person, results.Results[0].Node.NodeType);
    }

    // ── Exact Phrase Matching ──

    [Fact]
    public void Search_ExactPhraseRanksHigher()
    {
        _nodeStore.Save(new WikiNode
        {
            NodeId = "exact",
            Title = "Exact phrase test",
            NodeType = WikiNodeType.Concept,
            Summary = "This node contains the phrase semantic memory system",
            Salience = 1.0
        });
        _nodeStore.Save(new WikiNode
        {
            NodeId = "scattered",
            Title = "Scattered words",
            NodeType = WikiNodeType.Concept,
            Summary = "This has semantic and memory and system separately",
            Salience = 1.0
        });
        _search.InvalidateIndex();

        var results = _search.Search("semantic memory system");

        Assert.True(results.Results.Count >= 2);
        // Exact phrase should rank higher
        Assert.Equal("exact", results.Results[0].Node.NodeId);
    }

    // ── Multi-term Queries ──

    [Fact]
    public void Search_MultipleTerms_AllMustMatch()
    {
        SeedTestData();

        var results = _search.Search("Engram LLamaSharp");

        Assert.True(results.Results.Count > 0);
        // Both terms must be present
        Assert.Contains(results.Results, r =>
            r.Node.Facts.Any(f => f.Text.Contains("LLamaSharp")));
    }

    [Fact]
    public void Search_NoMatch_ReturnsEmpty()
    {
        SeedTestData();

        var results = _search.Search("nonexistent_term_xyz");

        Assert.Empty(results.Results);
    }

    // ── Edge Cases ──

    [Fact]
    public void Search_EmptyQuery_ReturnsEmpty()
    {
        SeedTestData();

        var results = _search.Search("");

        Assert.Empty(results.Results);
    }

    [Fact]
    public void Search_NullQuery_ReturnsEmpty()
    {
        SeedTestData();

        var results = _search.Search(null!);

        Assert.Empty(results.Results);
    }

    [Fact]
    public void Search_ShortQuery_ReturnsEmpty()
    {
        SeedTestData();

        var results = _search.Search("a");

        Assert.Empty(results.Results);
    }

    [Fact]
    public void Search_NoNodes_ReturnsEmpty()
    {
        var results = _search.Search("anything");

        Assert.Empty(results.Results);
    }

    // ── Relevance Scores ──

    [Fact]
    public void Search_RelevanceBetween0And1()
    {
        SeedTestData();

        var results = _search.Search("Engram");

        foreach (var result in results.Results)
        {
            Assert.InRange(result.Relevance, 0.0, 1.0);
        }
    }

    [Fact]
    public void Search_ResultsSortedByRelevance()
    {
        SeedTestData();

        var results = _search.Search("Engram");

        for (int i = 1; i < results.Results.Count; i++)
        {
            Assert.True(results.Results[i - 1].Relevance >= results.Results[i].Relevance,
                $"Results not sorted: [{i-1}]={results.Results[i-1].Relevance} < [{i}]={results.Results[i].Relevance}");
        }
    }

    // ── Index Management ──

    [Fact]
    public void InvalidateIndex_RebuildsOnNextSearch()
    {
        SeedTestData();

        var results1 = _search.Search("Engram");
        Assert.True(results1.Results.Count > 0);

        // Add a new node
        _nodeStore.Save(new WikiNode
        {
            NodeId = "new_node",
            Title = "Brand New Node",
            NodeType = WikiNodeType.Concept,
            Summary = "Just added",
            Salience = 1.0
        });

        _search.InvalidateIndex();
        var results2 = _search.Search("Brand New Node");

        Assert.True(results2.Results.Count > 0);
        Assert.Contains(results2.Results, r => r.Node.NodeId == "new_node");
    }

    // ── Production-grade ──

    [Fact]
    public void Search_ConcurrentSearches_DoNotCorrupt()
    {
        SeedTestData();

        var tasks = new List<Task>();
        for (int i = 0; i < 20; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                var results = _search.Search("Engram");
                Assert.NotNull(results);
            }));
        }

        Task.WaitAll(tasks.ToArray());
    }

    [Fact]
    public void Search_UnicodeQuery_DoesNotCrash()
    {
        SeedTestData();

        var results = _search.Search("记忆系统");

        Assert.NotNull(results);
    }
}
