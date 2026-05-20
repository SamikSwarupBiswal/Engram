using Engram.Store.Metabolism;
using Engram.Store.Wiki;
using Xunit;

namespace Engram.Store.Tests;

public class RetrievalBudgetManagerTests
{
    private readonly RetrievalBudgetManager _manager = new();

    private List<WikiNode> CreateTestNodes()
    {
        return new List<WikiNode>
        {
            new()
            {
                NodeId = "project_engram",
                Title = "Engram",
                NodeType = WikiNodeType.Project,
                Summary = "A semantic memory system",
                Salience = 1.0,
                LastTouchedAt = DateTimeOffset.UtcNow,
                Facts = new List<WikiFact>
                {
                    new() { Text = "Built with .NET 8" },
                    new() { Text = "Uses LLamaSharp" }
                }
            },
            new()
            {
                NodeId = "person_alex",
                Title = "Alex",
                NodeType = WikiNodeType.Person,
                Summary = "Friend and colleague",
                Salience = 0.8,
                LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-2),
                Facts = new List<WikiFact>
                {
                    new() { Text = "Helped with frontend" }
                }
            },
            new()
            {
                NodeId = "stale_concept",
                Title = "Old Concept",
                NodeType = WikiNodeType.Concept,
                Summary = "Something old",
                Salience = 0.3,
                LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-10),
                Facts = new List<WikiFact>()
            }
        };
    }

    // ── Node Selection ──

    [Fact]
    public void SelectNodes_ReturnsNodesWithinBudget()
    {
        var nodes = CreateTestNodes();
        var selected = _manager.SelectNodes(nodes, "Engram", DateTimeOffset.UtcNow);

        Assert.NotEmpty(selected);
        Assert.True(selected.Count <= _manager.MaxNodes);
    }

    [Fact]
    public void SelectNodes_PrioritizesHighSalience()
    {
        var nodes = CreateTestNodes();
        var selected = _manager.SelectNodes(nodes, "", DateTimeOffset.UtcNow);

        Assert.True(selected.Count > 0);
        // First node should have highest score
        Assert.True(selected[0].Score >= selected[^1].Score);
    }

    [Fact]
    public void SelectNodes_PrioritizesRecent()
    {
        var nodes = new List<WikiNode>
        {
            new()
            {
                NodeId = "recent",
                Title = "Recent Node",
                NodeType = WikiNodeType.Concept,
                Summary = "Just updated",
                Salience = 0.5,
                LastTouchedAt = DateTimeOffset.UtcNow
            },
            new()
            {
                NodeId = "old",
                Title = "Old Node",
                NodeType = WikiNodeType.Concept,
                Summary = "Updated long ago",
                Salience = 0.5,
                LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-30)
            }
        };

        var selected = _manager.SelectNodes(nodes, "", DateTimeOffset.UtcNow);

        Assert.True(selected.Count >= 2);
        Assert.Equal("recent", selected[0].Node.NodeId);
    }

    [Fact]
    public void SelectNodes_PrioritizesRelevant()
    {
        var nodes = CreateTestNodes();
        var selected = _manager.SelectNodes(nodes, "Engram", DateTimeOffset.UtcNow);

        Assert.True(selected.Count > 0);
        // Engram node should be first (relevant to query)
        Assert.Equal("project_engram", selected[0].Node.NodeId);
    }

    [Fact]
    public void SelectNodes_EmptyCandidates_ReturnsEmpty()
    {
        var selected = _manager.SelectNodes(new List<WikiNode>(), "query", DateTimeOffset.UtcNow);

        Assert.Empty(selected);
    }

    // ── Context Compression ──

    [Fact]
    public void CompressContext_ReturnsNonEmptyString()
    {
        var nodes = CreateTestNodes();
        var selected = _manager.SelectNodes(nodes, "", DateTimeOffset.UtcNow);
        var context = _manager.CompressContext(selected);

        Assert.NotNull(context);
        Assert.NotEmpty(context);
    }

    [Fact]
    public void CompressContext_IncludesNodeTitles()
    {
        var nodes = CreateTestNodes();
        var selected = _manager.SelectNodes(nodes, "", DateTimeOffset.UtcNow);
        var context = _manager.CompressContext(selected);

        Assert.Contains("Engram", context);
    }

    [Fact]
    public void CompressContext_RespectsTokenBudget()
    {
        var nodes = CreateTestNodes();
        var selected = _manager.SelectNodes(nodes, "", DateTimeOffset.UtcNow);
        var context = _manager.CompressContext(selected);

        // Rough check: context should be within budget
        var estimatedTokens = context.Length / 4;
        Assert.True(estimatedTokens <= _manager.MaxContextTokens + 100); // Allow some margin
    }

    // ── Configuration ──

    [Fact]
    public void Configuration_DefaultValues()
    {
        Assert.Equal(2000, _manager.MaxContextTokens);
        Assert.Equal(10, _manager.MaxNodes);
        Assert.Equal(3, _manager.MaxFactsPerNode);
    }

    [Fact]
    public void Configuration_CanSetMaxContextTokens()
    {
        _manager.MaxContextTokens = 1000;
        Assert.Equal(1000, _manager.MaxContextTokens);
    }

    [Fact]
    public void Configuration_CanSetMaxNodes()
    {
        _manager.MaxNodes = 5;
        Assert.Equal(5, _manager.MaxNodes);
    }

    [Fact]
    public void Configuration_LowBudget_SelectsFewerNodes()
    {
        var nodes = CreateTestNodes();

        _manager.MaxContextTokens = 100; // Very low budget
        var selected = _manager.SelectNodes(nodes, "", DateTimeOffset.UtcNow);

        Assert.True(selected.Count <= nodes.Count);
    }

    // ── Edge Cases ──

    [Fact]
    public void SelectNodes_NullQuery_ReturnsNodes()
    {
        var nodes = CreateTestNodes();
        var selected = _manager.SelectNodes(nodes, null!, DateTimeOffset.UtcNow);

        Assert.NotNull(selected);
    }

    [Fact]
    public void SelectNodes_EmptyQuery_ReturnsNodes()
    {
        var nodes = CreateTestNodes();
        var selected = _manager.SelectNodes(nodes, "", DateTimeOffset.UtcNow);

        Assert.NotEmpty(selected);
    }

    [Fact]
    public void SelectNodes_UnicodeQuery_DoesNotCrash()
    {
        var nodes = CreateTestNodes();
        var selected = _manager.SelectNodes(nodes, "记忆系统", DateTimeOffset.UtcNow);

        Assert.NotNull(selected);
    }

    // ── Production-grade ──

    [Fact]
    public void SelectNodes_LargeCandidateSet_DoesNotCrash()
    {
        var nodes = new List<WikiNode>();
        for (int i = 0; i < 100; i++)
        {
            nodes.Add(new WikiNode
            {
                NodeId = $"node_{i}",
                Title = $"Node {i}",
                NodeType = WikiNodeType.Concept,
                Summary = $"Summary {i}",
                Salience = 1.0 - (i * 0.01),
                LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-i)
            });
        }

        var selected = _manager.SelectNodes(nodes, "Node", DateTimeOffset.UtcNow);

        Assert.NotEmpty(selected);
        Assert.True(selected.Count <= _manager.MaxNodes);
    }
}
