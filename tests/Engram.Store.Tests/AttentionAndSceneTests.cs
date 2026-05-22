using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Engram.Store.Events;
using Engram.Store.Reality;
using Engram.Store.Wiki;
using Xunit;

namespace Engram.Store.Tests;

public class AttentionAndSceneTests : IDisposable
{
    private readonly string _tempDir;
    private readonly WorkspacePaths _paths;
    private readonly WikiNodeStore _nodeStore;
    private readonly InMemoryEventBus _eventBus;

    public AttentionAndSceneTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram_attention_test_" + Guid.NewGuid().ToString("n")[..8]);
        _paths = new WorkspacePaths(_tempDir);
        _nodeStore = new WikiNodeStore(_paths);
        _eventBus = new InMemoryEventBus();
    }

    public void Dispose()
    {
        _eventBus.Dispose();
        _nodeStore.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ── GlobalAttentionOrchestrator Tests ──

    [Fact]
    public void AttentionOrchestrator_RecordAttention_IncreasesScoreAndDecays()
    {
        var orchestrator = new GlobalAttentionOrchestrator(_nodeStore)
        {
            AttentionHalfLifeSeconds = 1.0 // 1 second half-life for fast testing
        };

        orchestrator.RecordAttention("node_a", 0.5);
        Assert.Equal(0.5, orchestrator.GetAttention("node_a"), 2);

        // Record more attention
        orchestrator.RecordAttention("node_a", 0.3);
        Assert.Equal(0.8, orchestrator.GetAttention("node_a"), 2);

        // Sleep to verify decay
        Thread.Sleep(1100);
        double decayed = orchestrator.GetAttention("node_a");
        Assert.True(decayed < 0.5, $"Decayed score should be < 0.5, but was {decayed}");
    }

    [Fact]
    public void AttentionOrchestrator_GroupsStates()
    {
        _nodeStore.Save(new WikiNode
        {
            NodeId = "node_active",
            Title = "Active Node",
            NodeType = WikiNodeType.Concept
        });
        _nodeStore.Save(new WikiNode
        {
            NodeId = "node_stale_goal",
            Title = "Stale Goal",
            NodeType = WikiNodeType.Goal,
            Confidence = 0.4, // Low confidence -> requires intervention
            OpenQuestions = new List<string> { "Unresolved question" }
        });

        var orchestrator = new GlobalAttentionOrchestrator(_nodeStore);
        orchestrator.SetAttention("node_active", 0.95);
        orchestrator.SetAttention("node_stale_goal", 0.05);

        var summary = orchestrator.GetAttentionSummary();
        
        Assert.Contains("node_active", summary.ActiveNodeIds);
        Assert.Contains("node_stale_goal", summary.StaleNodeIds);
        Assert.Contains("node_stale_goal", summary.RequiresInterventionNodeIds);
    }

    // ── SemanticSceneConstructor Tests ──

    [Fact]
    public void SceneConstructor_ClassifiesCodingSession()
    {
        using var constructor = new SemanticSceneConstructor(_eventBus);
        
        constructor.SetState(
            process: "code", 
            title: "Program.cs - Engram - VS Code", 
            tabCount: 5, 
            interruptionCount: 0, 
            recentFileChanges: 1, 
            focusStarted: DateTimeOffset.UtcNow.AddMinutes(-2)
        );

        Assert.Equal("CodingSession", constructor.Classify());
    }

    [Fact]
    public void SceneConstructor_ClassifiesBurnoutSpiral()
    {
        using var constructor = new SemanticSceneConstructor(_eventBus);
        
        // Simulating high interruptions and distracting browsing
        constructor.SetState(
            process: "chrome", 
            title: "reddit: the front page of the internet - Google Chrome", 
            tabCount: 20, 
            interruptionCount: 6, 
            recentFileChanges: 0, 
            focusStarted: DateTimeOffset.UtcNow.AddSeconds(-30)
        );

        Assert.Equal("BurnoutSpiral", constructor.Classify());
    }

    [Fact]
    public void SceneConstructor_ClassifiesFinancialWorkflow()
    {
        using var constructor = new SemanticSceneConstructor(_eventBus);
        
        constructor.SetState(
            process: "chrome", 
            title: "Stripe Checkout: Pay Invoice", 
            tabCount: 5, 
            interruptionCount: 0, 
            recentFileChanges: 0, 
            focusStarted: DateTimeOffset.UtcNow
        );

        Assert.Equal("FinancialWorkflow", constructor.Classify());
    }

    [Fact]
    public void SceneConstructor_ClassifiesResearchArc()
    {
        using var constructor = new SemanticSceneConstructor(_eventBus);
        
        constructor.SetState(
            process: "firefox", 
            title: "Arxiv: Attention Is All You Need Paper", 
            tabCount: 16, 
            interruptionCount: 0, 
            recentFileChanges: 0, 
            focusStarted: DateTimeOffset.UtcNow
        );

        Assert.Equal("ResearchArc", constructor.Classify());
    }

    // ── AttentionStormGuard Tests ──

    [Fact]
    public void StormGuard_PreventsCyclesAndDepthOverflow()
    {
        var guard = new AttentionStormGuard
        {
            MaxPropagationDepth = 2
        };

        // Self-referential loop blocked
        Assert.False(guard.AllowPropagation("node_a", "node_a", 0));

        // Depth threshold blocked
        Assert.True(guard.AllowPropagation("node_a", "node_b", 1));
        Assert.False(guard.AllowPropagation("node_b", "node_c", 3));
    }

    [Fact]
    public void StormGuard_EnforcesCooldown()
    {
        var guard = new AttentionStormGuard
        {
            RefractoryCooldown = TimeSpan.FromSeconds(2)
        };

        Assert.True(guard.AllowPropagation("node_a", "node_b", 0));
        guard.RecordPropagation("node_a", "node_b", 0.5);

        // Immediate subsequent propagation is blocked
        Assert.False(guard.AllowPropagation("node_a", "node_c", 0));

        // Wait for cooldown
        Thread.Sleep(2100);
        Assert.True(guard.AllowPropagation("node_a", "node_c", 0));
    }

    // ── MemoryPropagationEngine Tests ──

    [Fact]
    public void PropagationEngine_PropagatesSalienceAlongEdges()
    {
        // Setup Node A with edge to Node B
        var nodeA = new WikiNode
        {
            NodeId = "node_a",
            Title = "Node A",
            NodeType = WikiNodeType.Concept,
            Edges = new List<WikiEdge>
            {
                new()
                {
                    TargetNodeId = "node_b",
                    RelationType = "related",
                    PropagationWeight = 0.5,
                    MaxInfluence = 1.0,
                    EvidenceThreshold = 0.1,
                    PropagationType = "operational"
                }
            }
        };

        var nodeB = new WikiNode
        {
            NodeId = "node_b",
            Title = "Node B",
            NodeType = WikiNodeType.Concept
        };

        _nodeStore.Save(nodeA);
        _nodeStore.Save(nodeB);

        var orchestrator = new GlobalAttentionOrchestrator(_nodeStore);
        var guard = new AttentionStormGuard { RefractoryCooldown = TimeSpan.Zero }; // No cooldown for test
        var engine = new MemoryPropagationEngine(_nodeStore, orchestrator, guard);

        orchestrator.SetAttention("node_a", 1.0);
        engine.Propagate("node_a", 1.0);

        // Node B attention should be: initialSalience (1.0) * PropagationWeight (0.5) * typeModifier (operational=1.0) = 0.5
        double bAttention = orchestrator.GetAttention("node_b");
        Assert.Equal(0.5, bAttention, 3);
    }
}
