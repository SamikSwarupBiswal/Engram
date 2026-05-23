using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Engram.Store.Wiki;
using Engram.Store.Metabolism;

namespace Engram.Store.Tests;

public class SemanticCompactorTests
{
    [Fact]
    public void GraphSalienceDecay_CalculatesCorrectDecay()
    {
        // Arrange: half-life of 30 days
        var decay = new GraphSalienceDecay(30.0);
        var now = DateTimeOffset.UtcNow;
        var touchedAt30DaysAgo = now.AddDays(-30);
        var touchedAt60DaysAgo = now.AddDays(-60);

        // Act
        var decayed30 = decay.CalculateDecay(1.0, touchedAt30DaysAgo, now);
        var decayed60 = decay.CalculateDecay(1.0, touchedAt60DaysAgo, now);

        // Assert
        Assert.Equal(0.0231, decay.Lambda, 4); // ln(2)/30 = 0.0231049
        Assert.Equal(0.5, decayed30, 2);
        Assert.Equal(0.25, decayed60, 2);
    }

    [Fact]
    public void FindMergePairs_IdentifiesOverlappingTitles()
    {
        // Arrange
        using var temp = new TempWorkspace();
        var compactor = new SemanticCompactor(new WikiNodeStore(temp.Paths));

        var nodes = new List<WikiNode>
        {
            new() { NodeId = "n1", Title = "Deep Learning Framework", NodeType = WikiNodeType.Concept },
            new() { NodeId = "n2", Title = "Deep Learning Frameworks", NodeType = WikiNodeType.Concept },
            new() { NodeId = "n3", Title = "Completely Different Topic", NodeType = WikiNodeType.Concept }
        };

        // Act
        // "deep learning framework" (3 words) vs "deep learning frameworks" (3 words)
        // union = 4 words ("deep", "learning", "framework", "frameworks")
        // intersection = 2 words ("deep", "learning")
        // similarity = 2/4 = 0.5
        var pairs = compactor.FindMergePairs(nodes, 0.4);

        // Assert
        Assert.Single(pairs);
        var pair = pairs[0];
        Assert.Equal("n1", pair.Item1.NodeId);
        Assert.Equal("n2", pair.Item2.NodeId);
    }

    [Fact]
    public async Task CompactGraphAsync_MergesEntitiesAndRedirectsReferences()
    {
        // Arrange
        using var temp = new TempWorkspace();
        var store = new WikiNodeStore(temp.Paths);
        var compactor = new SemanticCompactor(store);

        var nodeA = new WikiNode
        {
            NodeId = "concept_learning",
            Title = "Deep Learning Framework",
            NodeType = WikiNodeType.Concept,
            Summary = "A node about deep learning frameworks",
            Facts = new List<WikiFact>
            {
                new() { Text = "PyTorch is highly popular", LastConfirmedAt = DateTimeOffset.UtcNow }
            },
            Links = new List<string> { "project_engram" },
            Salience = 0.8,
            Confidence = 0.9,
            LastTouchedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };

        var nodeB = new WikiNode
        {
            NodeId = "concept_learnings",
            Title = "Deep Learning Frameworks",
            NodeType = WikiNodeType.Concept,
            Summary = "Node discussing deep learning systems",
            Facts = new List<WikiFact>
            {
                new() { Text = "TensorFlow is used in enterprise", LastConfirmedAt = DateTimeOffset.UtcNow }
            },
            Links = new List<string> { "project_core" },
            Salience = 0.6,
            Confidence = 0.7,
            LastTouchedAt = DateTimeOffset.UtcNow
        };

        // Node C references Node A
        var nodeC = new WikiNode
        {
            NodeId = "project_engram",
            Title = "Engram Project",
            NodeType = WikiNodeType.Project,
            Links = new List<string> { "concept_learning" }
        };

        store.Save(nodeA);
        store.Save(nodeB);
        store.Save(nodeC);

        // Act
        var mergeCount = await compactor.CompactGraphAsync(0.4);

        // Assert
        Assert.Equal(1, mergeCount);
        Assert.False(store.Exists("concept_learning"));
        Assert.False(store.Exists("concept_learnings"));

        var allNodes = store.LoadAll();
        var mergedNode = allNodes.FirstOrDefault(n => n.NodeId.StartsWith("concept_") && n.NodeId != "concept_learning" && n.NodeId != "concept_learnings");
        Assert.NotNull(mergedNode);
        Assert.Equal(WikiNodeType.Concept, mergedNode.NodeType);
        Assert.Equal(0.8, mergedNode.Salience);
        Assert.Equal(0.9, mergedNode.Confidence);

        // Verify facts merged
        Assert.Equal(2, mergedNode.Facts.Count);
        Assert.Contains(mergedNode.Facts, f => f.Text.Contains("PyTorch"));
        Assert.Contains(mergedNode.Facts, f => f.Text.Contains("TensorFlow"));

        // Verify links merged
        Assert.Contains("project_engram", mergedNode.Links);
        Assert.Contains("project_core", mergedNode.Links);

        // Verify C was updated to reference the new merged ID
        var updatedC = store.Load("project_engram");
        Assert.NotNull(updatedC);
        Assert.Single(updatedC.Links);
        Assert.Equal(mergedNode.NodeId, updatedC.Links[0]);
    }
}
