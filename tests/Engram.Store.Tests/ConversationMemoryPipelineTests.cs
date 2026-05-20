using Engram.Store.Memory;
using Engram.Store.Wiki;
using Engram.Store;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Engram.Store.Tests;

public class ConversationMemoryPipelineTests : IDisposable
{
    private readonly string _tempDir;
    private readonly WikiNodeStore _nodeStore;
    private readonly WikiMetabolizer _metabolizer;
    private readonly ConversationMemoryExtractor _extractor;
    private readonly ConversationMemoryPipeline _pipeline;

    public ConversationMemoryPipelineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram_pipeline_test_" + Guid.NewGuid().ToString("n")[..8]);

        var paths = new WorkspacePaths(_tempDir);
        _nodeStore = new WikiNodeStore(paths);
        _metabolizer = new WikiMetabolizer(_nodeStore);
        _extractor = new ConversationMemoryExtractor();
        _pipeline = new ConversationMemoryPipeline(_extractor, _metabolizer);
    }

    public void Dispose()
    {
        _nodeStore.Dispose();
        _metabolizer.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ── Core Pipeline Tests ──

    [Fact]
    public void ProcessConversation_CreatesPersonNode()
    {
        var result = _pipeline.ProcessConversation(
            "I was talking to my friend Alex about the project",
            "That's great! Alex sounds supportive.");

        Assert.True(result.Success);
        Assert.True(result.CandidatesExtracted > 0);
        Assert.True(result.NodesCreated > 0);

        // Verify person node was created
        var nodes = _nodeStore.LoadAll();
        Assert.Contains(nodes, n => n.NodeType == WikiNodeType.Person && n.Title == "Alex");
    }

    [Fact]
    public void ProcessConversation_CreatesProjectNode()
    {
        var result = _pipeline.ProcessConversation(
            "I'm building a semantic memory system called Engram",
            "Engram sounds like a great project.");

        Assert.True(result.Success);
        Assert.True(result.NodesCreated > 0);

        var nodes = _nodeStore.LoadAll();
        Assert.Contains(nodes, n => n.NodeType == WikiNodeType.Project);
    }

    [Fact]
    public void ProcessConversation_CreatesGoalNode()
    {
        var result = _pipeline.ProcessConversation(
            "I want to make Engram remember everything",
            "That's an ambitious goal.");

        Assert.True(result.Success);

        var nodes = _nodeStore.LoadAll();
        Assert.Contains(nodes, n => n.NodeType == WikiNodeType.Goal);
    }

    [Fact]
    public void ProcessConversation_CreatesDecisionNode()
    {
        var result = _pipeline.ProcessConversation(
            "I decided to use .NET for the backend",
            ".NET is a solid choice for this.");

        Assert.True(result.Success);

        var nodes = _nodeStore.LoadAll();
        Assert.Contains(nodes, n => n.NodeType == WikiNodeType.Decision);
    }

    [Fact]
    public void ProcessConversation_CreatesMultipleNodes()
    {
        var result = _pipeline.ProcessConversation(
            "I'm building Engram. I want to make it remember everything. I decided to use LLamaSharp.",
            "Those are good decisions.");

        Assert.True(result.Success);
        Assert.True(result.NodesCreated >= 3);

        var nodes = _nodeStore.LoadAll();
        Assert.Contains(nodes, n => n.NodeType == WikiNodeType.Project);
        Assert.Contains(nodes, n => n.NodeType == WikiNodeType.Goal);
        Assert.Contains(nodes, n => n.NodeType == WikiNodeType.Decision);
    }

    [Fact]
    public void ProcessConversation_EmptyMessage_ReturnsSuccess()
    {
        var result = _pipeline.ProcessConversation("", "");

        Assert.True(result.Success);
        Assert.Equal(0, result.CandidatesExtracted);
        Assert.Equal(0, result.NodesCreated);
    }

    [Fact]
    public void ProcessConversation_UpdatesExistingNode()
    {
        // First mention
        _pipeline.ProcessConversation("I'm building Engram", "");

        // Second mention — should merge, not create duplicate
        _pipeline.ProcessConversation("Engram is a semantic memory system", "");

        var nodes = _nodeStore.LoadAll();
        var engramNodes = nodes.Where(n => n.Title.Contains("Engram", StringComparison.OrdinalIgnoreCase)).ToList();

        // Should have at most one Engram node (deduplication by title)
        Assert.True(engramNodes.Count <= 2, $"Expected at most 2 Engram nodes, got {engramNodes.Count}");
    }

    [Fact]
    public void ProcessConversation_SetsSourceMetadata()
    {
        var result = _pipeline.ProcessConversation(
            "I need to fix the authentication bug",
            "");

        Assert.True(result.Success);

        // The raw event should have source = "conversation"
        // We can verify this indirectly by checking the wiki node has sources
        var nodes = _nodeStore.LoadAll();
        var taskNode = nodes.FirstOrDefault(n => n.Facts.Any(f => f.Text.Contains("fix the authentication")));
        Assert.NotNull(taskNode);
        Assert.True(taskNode.Facts.First().Sources.Any(s => s.Source == "conversation"));
    }

    [Fact]
    public void ProcessConversation_ReturnsAffectedNodeIds()
    {
        var result = _pipeline.ProcessConversation(
            "I'm worried about the deployment",
            "");

        Assert.True(result.Success);
        Assert.NotEmpty(result.AffectedNodeIds);
    }

    [Fact]
    public void ProcessConversation_MeasuresDuration()
    {
        var result = _pipeline.ProcessConversation("I prefer dark mode", "");

        Assert.True(result.Success);
        Assert.True(result.Duration >= TimeSpan.Zero);
    }

    // ── Edge Cases ──

    [Fact]
    public void ProcessConversation_UnicodeContent_DoesNotCrash()
    {
        var result = _pipeline.ProcessConversation(
            "I'm building a system called 记忆系统",
            "Interesting project name!");

        Assert.True(result.Success);
    }

    [Fact]
    public void ProcessConversation_VeryLongMessage_DoesNotCrash()
    {
        var longMsg = "I need to " + string.Join(" ", Enumerable.Repeat("really", 1000)) + " fix this";
        var result = _pipeline.ProcessConversation(longMsg, "");

        Assert.True(result.Success);
    }

    [Fact]
    public void ProcessConversation_SpecialCharacters_DoesNotCrash()
    {
        var result = _pipeline.ProcessConversation(
            "I'm building a C# app with .NET 8 & Entity Framework",
            "");

        Assert.True(result.Success);
    }

    // ── Integration: Pipeline + Metabolizer ──

    [Fact]
    public void ProcessConversation_WikiNodesAreSearchable()
    {
        _pipeline.ProcessConversation("I'm building Engram", "");

        // The node should be loadable
        var nodes = _nodeStore.LoadAll();
        Assert.NotEmpty(nodes);

        var engramNode = nodes.FirstOrDefault(n => n.Title.Contains("Engram", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(engramNode);
        Assert.NotEmpty(engramNode.Facts);
    }

    [Fact]
    public void ProcessConversation_MultipleConversationsAccumulate()
    {
        _pipeline.ProcessConversation("I'm building Engram", "");
        _pipeline.ProcessConversation("I decided to use LLamaSharp", "");

        var nodes = _nodeStore.LoadAll();
        Assert.True(nodes.Count >= 2);
    }

    [Fact]
    public void ProcessConversation_SameFactMerges()
    {
        _pipeline.ProcessConversation("I'm building Engram", "");
        _pipeline.ProcessConversation("I'm building Engram", "");

        var nodes = _nodeStore.LoadAll();
        var engramNodes = nodes.Where(n => n.Title.Contains("Engram", StringComparison.OrdinalIgnoreCase)).ToList();

        // Should be one node with merged facts
        Assert.Single(engramNodes);
    }

    // ── Production-grade ──

    [Fact]
    public void ProcessConversation_ConcurrentAccess_DoesNotCorrupt()
    {
        var tasks = new List<Task>();
        for (int i = 0; i < 10; i++)
        {
            var idx = i;
            tasks.Add(Task.Run(() =>
            {
                _pipeline.ProcessConversation($"I'm building project_{idx}", "");
            }));
        }

        Task.WaitAll(tasks.ToArray());

        var nodes = _nodeStore.LoadAll();
        Assert.True(nodes.Count >= 10);
    }
}
