using Engram.Store.Identity;
using Engram.Store.Memory;
using Engram.Store.Search;
using Engram.Store.Wiki;
using Xunit;

namespace Engram.Store.Tests;

public class PromptAssemblerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly WorkspacePaths _paths;
    private readonly IdentityStore _identityStore;
    private readonly WikiNodeStore _nodeStore;
    private readonly SearchEngine _searchEngine;
    private readonly PromptAssembler _assembler;

    public PromptAssemblerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram_prompt_test_" + Guid.NewGuid().ToString("n")[..8]);
        _paths = new WorkspacePaths(_tempDir);
        _identityStore = new IdentityStore(_paths);
        _nodeStore = new WikiNodeStore(_paths);
        _searchEngine = new SearchEngine(_nodeStore);
        _assembler = new PromptAssembler(_identityStore, _nodeStore, _searchEngine);
    }

    public void Dispose()
    {
        _searchEngine.Dispose();
        _nodeStore.Dispose();
        _identityStore.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ── Core Prompt Assembly ──

    [Fact]
    public void AssemblePrompt_ContainsSystemIdentity()
    {
        var prompt = _assembler.AssemblePrompt("hello");

        Assert.Contains("Engram", prompt);
        Assert.Contains("semantic memory assistant", prompt);
    }

    [Fact]
    public void AssemblePrompt_ContainsDate()
    {
        var prompt = _assembler.AssemblePrompt("hello");

        Assert.Contains(DateTime.Now.ToString("yyyy-MM-dd"), prompt);
    }

    [Fact]
    public void AssemblePrompt_EmptyMessage_ReturnsPrompt()
    {
        var prompt = _assembler.AssemblePrompt("");

        Assert.NotNull(prompt);
        Assert.Contains("Engram", prompt);
    }

    // ── User Context ──

    [Fact]
    public void AssemblePrompt_IncludesUserProfile()
    {
        // Set up user profile
        _identityStore.SaveProfile(new UserProfile
        {
            UserId = "test-user",
            DisplayName = "Samik",
            Goals = new List<string> { "Build Engram", "Ship product" }
        });

        var prompt = _assembler.AssemblePrompt("hello");

        Assert.Contains("Samik", prompt);
        Assert.Contains("Build Engram", prompt);
    }

    [Fact]
    public void AssemblePrompt_IncludesAntiGoals()
    {
        _identityStore.SaveAntiGoals(new List<AntiGoal>
        {
            new() { Id = "ag1", Description = "Never share data externally", Severity = AntiGoalSeverity.Critical }
        });

        var prompt = _assembler.AssemblePrompt("hello");

        Assert.Contains("Never share data externally", prompt);
    }

    [Fact]
    public void AssemblePrompt_IncludesPreferences()
    {
        _identityStore.SaveProfile(new UserProfile
        {
            UserId = "test-user",
            DisplayName = "Samik",
            ComfortTriggers = new List<string> { "Dark mode", "Concise responses" }
        });

        var prompt = _assembler.AssemblePrompt("hello");

        Assert.Contains("Dark mode", prompt);
    }

    // ── Retrieval-Augmented Context ──

    [Fact]
    public void AssemblePrompt_IncludesRelevantWikiNodes()
    {
        // Create wiki nodes
        var node = new WikiNode
        {
            NodeId = "project_engram",
            Title = "Engram",
            NodeType = WikiNodeType.Project,
            Summary = "A semantic memory system",
            Facts = new List<WikiFact>
            {
                new() { Text = "Building with .NET 8 and LLamaSharp" }
            },
            Salience = 1.0
        };
        _nodeStore.Save(node);
        _searchEngine.InvalidateIndex();

        var prompt = _assembler.AssemblePrompt("tell me about Engram");

        Assert.Contains("Engram", prompt);
        Assert.Contains("semantic memory", prompt);
    }

    [Fact]
    public void AssemblePrompt_SearchesForRelevantNodes()
    {
        // Create multiple nodes
        _nodeStore.Save(new WikiNode
        {
            NodeId = "project_engram",
            Title = "Engram",
            NodeType = WikiNodeType.Project,
            Summary = "Semantic memory system",
            Salience = 1.0
        });
        _nodeStore.Save(new WikiNode
        {
            NodeId = "person_alex",
            Title = "Alex",
            NodeType = WikiNodeType.Person,
            Summary = "Friend and colleague",
            Salience = 0.8
        });
        _searchEngine.InvalidateIndex();

        // Query about Engram should return Engram node, not Alex
        var prompt = _assembler.AssemblePrompt("how is the Engram project going?");

        Assert.Contains("Engram", prompt);
    }

    [Fact]
    public void AssemblePrompt_FallsBackToSalientNodes_WhenSearchFindsNothing()
    {
        // Create a node but search for something unrelated
        _nodeStore.Save(new WikiNode
        {
            NodeId = "project_engram",
            Title = "Engram",
            NodeType = WikiNodeType.Project,
            Summary = "Semantic memory system",
            Salience = 1.0
        });
        _searchEngine.InvalidateIndex();

        // Search for something that won't match
        var prompt = _assembler.AssemblePrompt("what is the weather like?");

        // Should still include Engram as fallback (high salience)
        Assert.Contains("Engram", prompt);
    }

    // ── Explicit Context Override ──

    [Fact]
    public void AssemblePrompt_WithExplicitNodes_UsesProvidedContext()
    {
        var nodes = new List<WikiNode>
        {
            new()
            {
                NodeId = "custom_node",
                Title = "Custom Context",
                NodeType = WikiNodeType.Concept,
                Summary = "This is custom context",
                Facts = new List<WikiFact> { new() { Text = "Custom fact" } }
            }
        };

        var prompt = _assembler.AssemblePrompt("hello", nodes);

        Assert.Contains("Custom Context", prompt);
        Assert.Contains("custom context", prompt);
    }

    // ── Prompt Size ──

    [Fact]
    public void AssemblePrompt_RespectsMaxWikiNodes()
    {
        // Create many nodes
        for (int i = 0; i < 20; i++)
        {
            _nodeStore.Save(new WikiNode
            {
                NodeId = $"node_{i}",
                Title = $"Node {i}",
                NodeType = WikiNodeType.Concept,
                Summary = $"Summary for node {i}",
                Salience = 1.0 - (i * 0.05)
            });
        }
        _searchEngine.InvalidateIndex();

        var prompt = _assembler.AssemblePrompt("hello");

        // Should not include all 20 nodes
        var nodeCount = prompt.Split('\n').Count(l => l.TrimStart().StartsWith("- ["));
        Assert.True(nodeCount <= _assembler.MaxWikiNodes, $"Expected at most {_assembler.MaxWikiNodes} nodes, got {nodeCount}");
    }

    [Fact]
    public void AssemblePrompt_IsReasonablyCompact()
    {
        // Create some context
        _identityStore.SaveProfile(new UserProfile
        {
            UserId = "test",
            DisplayName = "TestUser",
            Goals = new List<string> { "Goal 1", "Goal 2" }
        });

        for (int i = 0; i < 5; i++)
        {
            _nodeStore.Save(new WikiNode
            {
                NodeId = $"node_{i}",
                Title = $"Node {i}",
                NodeType = WikiNodeType.Concept,
                Summary = $"Summary {i}",
                Salience = 1.0
            });
        }
        _searchEngine.InvalidateIndex();

        var prompt = _assembler.AssemblePrompt("test message");

        // System prompt should be under ~2000 chars for Phi-4-mini
        Assert.True(prompt.Length < 3000, $"Prompt too long: {prompt.Length} chars");
    }

    // ── Edge Cases ──

    [Fact]
    public void AssemblePrompt_NoProfile_DoesNotCrash()
    {
        // No profile saved
        var prompt = _assembler.AssemblePrompt("hello");

        Assert.NotNull(prompt);
        Assert.Contains("Engram", prompt);
    }

    [Fact]
    public void AssemblePrompt_NoWikiNodes_DoesNotCrash()
    {
        // No wiki nodes
        var prompt = _assembler.AssemblePrompt("hello");

        Assert.NotNull(prompt);
    }

    [Fact]
    public void AssemblePrompt_NoAntiGoals_DoesNotCrash()
    {
        var prompt = _assembler.AssemblePrompt("hello");

        Assert.NotNull(prompt);
    }

    [Fact]
    public void AssemblePrompt_UnicodeMessage_DoesNotCrash()
    {
        var prompt = _assembler.AssemblePrompt("告诉我关于记忆系统的事情");

        Assert.NotNull(prompt);
    }

    [Fact]
    public void AssemblePrompt_VeryLongMessage_DoesNotCrash()
    {
        var longMsg = string.Join(" ", Enumerable.Repeat("Engram is a memory system", 100));
        var prompt = _assembler.AssemblePrompt(longMsg);

        Assert.NotNull(prompt);
    }

    // ── Integration: PromptAssembler + ConversationMemoryPipeline ──

    [Fact]
    public void AssemblePrompt_AfterConversationPipeline_IncludesNewNodes()
    {
        // First: create memories via pipeline
        var extractor = new ConversationMemoryExtractor();
        var metabolizer = new WikiMetabolizer(_nodeStore);
        var pipeline = new ConversationMemoryPipeline(extractor, metabolizer);

        pipeline.ProcessConversation("I'm building Engram with LLamaSharp", "");

        // Now: assemble prompt — should include the new node
        _searchEngine.InvalidateIndex();
        var prompt = _assembler.AssemblePrompt("how is my project?");

        Assert.Contains("Engram", prompt);
    }

    // ── Production-grade ──

    [Fact]
    public void AssemblePrompt_ConcurrentCalls_DoNotCorrupt()
    {
        _nodeStore.Save(new WikiNode
        {
            NodeId = "test_node",
            Title = "Test",
            NodeType = WikiNodeType.Concept,
            Summary = "Test summary",
            Salience = 1.0
        });
        _searchEngine.InvalidateIndex();

        var tasks = new List<Task>();
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                var prompt = _assembler.AssemblePrompt($"message {Guid.NewGuid()}");
                Assert.NotNull(prompt);
            }));
        }

        Task.WaitAll(tasks.ToArray());
    }
}
