using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Engram.Store.Events;
using Engram.Store.Reality;
using Engram.Store.Wiki;
using Xunit;

namespace Engram.Store.Tests;

public class Phase10DeepRealityTests : IDisposable
{
    private readonly string _tempDir;
    private readonly WorkspacePaths _paths;
    private readonly WikiNodeStore _nodeStore;

    public Phase10DeepRealityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram_phase10_deep_test_" + Guid.NewGuid().ToString("n")[..8]);
        _paths = new WorkspacePaths(_tempDir);
        _nodeStore = new WikiNodeStore(_paths);
    }

    public void Dispose()
    {
        _nodeStore.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void ConsistencyEngine_GroupsValuesCaseInsensitively()
    {
        var engine = new GlobalConsistencyEngine();

        var node = new WikiNode
        {
            NodeId = "test_node",
            Title = "Test Node",
            NodeType = WikiNodeType.Concept,
            Claims = new List<SemanticClaim>
            {
                new()
                {
                    ClaimId = "c1",
                    Property = "Status",
                    Value = "Active", // Uppercase A
                    Confidence = 0.5,
                    Source = "user_statement",
                    Timestamp = DateTimeOffset.UtcNow
                },
                new()
                {
                    ClaimId = "c2",
                    Property = "Status",
                    Value = "active", // Lowercase a
                    Confidence = 0.4,
                    Source = "user_statement",
                    Timestamp = DateTimeOffset.UtcNow
                }
            }
        };

        var analysis = engine.AnalyzeNode(node);

        // They should be grouped under "Active" (or "active") because of case insensitivity
        Assert.Single(analysis.DominantValues);
        var dominantValue = analysis.DominantValues["Status"];
        Assert.True(string.Equals(dominantValue, "Active", StringComparison.OrdinalIgnoreCase));

        // Tension should be 0.0 because there are no competing distinct values (case-insensitively)
        Assert.Equal(0.0, analysis.PropertyTensions["Status"]);
    }

    [Fact]
    public void ConsistencyEngine_IgnoresExpiredClaims()
    {
        var engine = new GlobalConsistencyEngine();

        var node = new WikiNode
        {
            NodeId = "test_node",
            Title = "Test Node",
            NodeType = WikiNodeType.Concept,
            Claims = new List<SemanticClaim>
            {
                new()
                {
                    ClaimId = "c1",
                    Property = "Status",
                    Value = "active",
                    Confidence = 0.9,
                    Source = "user_statement",
                    Timestamp = DateTimeOffset.UtcNow,
                    Expires = DateTimeOffset.UtcNow.AddMinutes(-5) // Expired 5 minutes ago
                },
                new()
                {
                    ClaimId = "c2",
                    Property = "Status",
                    Value = "cancelled",
                    Confidence = 0.8,
                    Source = "user_statement",
                    Timestamp = DateTimeOffset.UtcNow,
                    Expires = DateTimeOffset.UtcNow.AddMinutes(5) // Still valid
                }
            }
        };

        var analysis = engine.AnalyzeNode(node);

        // The active one is expired, so only cancelled remains
        Assert.Equal("cancelled", analysis.DominantValues["Status"]);
        Assert.Equal(0.0, analysis.PropertyTensions["Status"]);
    }

    [Fact]
    public void ConsistencyEngine_HandlesEmptyClaimsWithoutError()
    {
        var engine = new GlobalConsistencyEngine();
        var node = new WikiNode
        {
            NodeId = "test_node",
            NodeType = WikiNodeType.Concept,
            Claims = new List<SemanticClaim>()
        };

        var analysis = engine.AnalyzeNode(node);

        Assert.Empty(analysis.DominantValues);
        Assert.Empty(analysis.PropertyTensions);
        Assert.Empty(analysis.Escalations);
        Assert.Equal(1.0, analysis.AverageConfidence);
    }

    [Fact]
    public void CrossModalResolver_LongestPathPrefixMatching_ResolvesCorrectSpecificity()
    {
        _nodeStore.Save(new WikiNode
        {
            NodeId = "node_a",
            Title = "Node A",
            NodeType = WikiNodeType.Project,
            Facts = new List<WikiFact> { new() { Text = "path: c:\\projects\\app" } }
        });

        _nodeStore.Save(new WikiNode
        {
            NodeId = "node_b",
            Title = "Node B",
            NodeType = WikiNodeType.Project,
            Facts = new List<WikiFact> { new() { Text = "path: c:\\projects\\app\\src" } }
        });

        _nodeStore.Save(new WikiNode
        {
            NodeId = "node_c",
            Title = "Node C",
            NodeType = WikiNodeType.Project,
            Facts = new List<WikiFact> { new() { Text = "path: c:\\projects\\app\\src\\sub" } }
        });

        var resolver = new CrossModalResolver(_nodeStore);

        // Sub folder match (longest prefix node_c wins)
        Assert.Equal("node_c", resolver.ResolvePath("c:\\projects\\app\\src\\sub\\file.cs"));

        // Intermediate src folder match (node_b wins)
        Assert.Equal("node_b", resolver.ResolvePath("c:\\projects\\app\\src\\other.cs"));

        // Main folder match (node_a wins)
        Assert.Equal("node_a", resolver.ResolvePath("c:\\projects\\app\\main.cs"));

        // Outside path
        Assert.Null(resolver.ResolvePath("c:\\projects\\other\\main.cs"));
    }

    [Fact]
    public void CrossModalResolver_WildcardMatching_HandlesRegexSpecialCharacters()
    {
        _nodeStore.Save(new WikiNode
        {
            NodeId = "node_brackets",
            Title = "Node Brackets",
            NodeType = WikiNodeType.Concept,
            Facts = new List<WikiFact> { new() { Text = "window: *[Draft] - Main Window*" } }
        });

        var resolver = new CrossModalResolver(_nodeStore);

        Assert.Equal("node_brackets", resolver.ResolveWindow("Test [Draft] - Main Window (Active)"));
        Assert.Null(resolver.ResolveWindow("Test Draft - Main Window"));
    }

    [Fact]
    public void MemoryPropagationEngine_RespectsPropagationTypeModifiers()
    {
        var nodeA = new WikiNode
        {
            NodeId = "node_a",
            Title = "Node A",
            NodeType = WikiNodeType.Concept,
            Edges = new List<WikiEdge>
            {
                new() { TargetNodeId = "node_op", RelationType = "rel", PropagationWeight = 1.0, MaxInfluence = 1.0, EvidenceThreshold = 0.0, PropagationType = "operational" },
                new() { TargetNodeId = "node_id", RelationType = "rel", PropagationWeight = 1.0, MaxInfluence = 1.0, EvidenceThreshold = 0.0, PropagationType = "identity" },
                new() { TargetNodeId = "node_em", RelationType = "rel", PropagationWeight = 1.0, MaxInfluence = 1.0, EvidenceThreshold = 0.0, PropagationType = "emotional" },
                new() { TargetNodeId = "node_df", RelationType = "rel", PropagationWeight = 1.0, MaxInfluence = 1.0, EvidenceThreshold = 0.0, PropagationType = "unknown" }
            }
        };

        _nodeStore.Save(nodeA);
        _nodeStore.Save(new WikiNode { NodeId = "node_op", Title = "Node OP", NodeType = WikiNodeType.Concept });
        _nodeStore.Save(new WikiNode { NodeId = "node_id", Title = "Node ID", NodeType = WikiNodeType.Concept });
        _nodeStore.Save(new WikiNode { NodeId = "node_em", Title = "Node EM", NodeType = WikiNodeType.Concept });
        _nodeStore.Save(new WikiNode { NodeId = "node_df", Title = "Node DF", NodeType = WikiNodeType.Concept });

        var orchestrator = new GlobalAttentionOrchestrator(_nodeStore);
        var guard = new AttentionStormGuard { RefractoryCooldown = TimeSpan.Zero };
        var engine = new MemoryPropagationEngine(_nodeStore, orchestrator, guard);

        orchestrator.SetAttention("node_a", 1.0);
        engine.Propagate("node_a", 1.0);

        // modifiers: operational=1.0, identity=0.8, emotional=0.2, fallback=0.5
        Assert.Equal(1.0, orchestrator.GetAttention("node_op"), 3);
        Assert.Equal(0.8, orchestrator.GetAttention("node_id"), 3);
        Assert.Equal(0.2, orchestrator.GetAttention("node_em"), 3);
        Assert.Equal(0.5, orchestrator.GetAttention("node_df"), 3);
    }

    [Fact]
    public void AttentionStormGuard_EnforcesRulesCorrectly()
    {
        var guard = new AttentionStormGuard
        {
            RefractoryCooldown = TimeSpan.FromSeconds(5),
            MaxPropagationDepth = 3
        };

        // 1. Prevent self-propagation
        Assert.False(guard.AllowPropagation("node1", "node1", 0));

        // 2. Allow normal propagation
        Assert.True(guard.AllowPropagation("node1", "node2", 0));

        // 3. Enforce depth threshold
        Assert.True(guard.AllowPropagation("node1", "node2", 3));
        Assert.False(guard.AllowPropagation("node1", "node2", 4)); // Above MaxPropagationDepth (3)

        // 4. Refractory cooldown
        guard.RecordPropagation("node1", "node2", 0.5);
        Assert.False(guard.AllowPropagation("node1", "node3", 1)); // Source "node1" in refractory cooldown

        // Reset clears cooldowns
        guard.Reset();
        Assert.True(guard.AllowPropagation("node1", "node3", 1));
    }

    [Fact]
    public void SemanticSceneConstructor_ClassifiesScenesCorrectly()
    {
        // 1. CodingSession
        {
            var bus = new InMemoryEventBus();
            using var constructor = new SemanticSceneConstructor(bus);
            constructor.SetState(
                process: "devenv.exe",
                title: "Engram.Store.Tests - Visual Studio",
                tabCount: 5,
                interruptionCount: 0,
                recentFileChanges: 1,
                focusStarted: DateTimeOffset.UtcNow.AddMinutes(-2)
            );
            Assert.Equal("CodingSession", constructor.Classify());
        }

        // 2. BurnoutSpiral
        {
            var bus = new InMemoryEventBus();
            using var constructor = new SemanticSceneConstructor(bus);
            constructor.SetState(
                process: "chrome.exe",
                title: "Funny Memes on reddit - Google Chrome",
                tabCount: 20,
                interruptionCount: 6,
                recentFileChanges: 0,
                focusStarted: DateTimeOffset.UtcNow.AddSeconds(-30)
            );
            // Force mock window switches for the sliding window
            var now = DateTimeOffset.UtcNow;
            for (int i = 0; i < 7; i++)
            {
                bus.Publish(new EventEnvelope
                {
                    EventType = "perception.active_window_changed",
                    Payload = new { Process = "chrome.exe", Title = $"Title {i}" }
                });
            }
            Assert.Equal("BurnoutSpiral", constructor.Classify());
        }

        // 3. FinancialWorkflow
        {
            var bus = new InMemoryEventBus();
            using var constructor = new SemanticSceneConstructor(bus);
            constructor.SetState(
                process: "chrome.exe",
                title: "Stripe Billing Dashboard",
                tabCount: 3,
                interruptionCount: 0,
                recentFileChanges: 0,
                focusStarted: DateTimeOffset.UtcNow.AddMinutes(-1)
            );
            Assert.Equal("FinancialWorkflow", constructor.Classify());
        }

        // 4. ResearchArc
        {
            var bus = new InMemoryEventBus();
            using var constructor = new SemanticSceneConstructor(bus);
            constructor.SetState(
                process: "firefox.exe",
                title: "Temporal Fusion Engine - Wikipedia",
                tabCount: 16,
                interruptionCount: 0,
                recentFileChanges: 0,
                focusStarted: DateTimeOffset.UtcNow.AddMinutes(-1)
            );
            Assert.Equal("ResearchArc", constructor.Classify());
        }

        // 5. ProjectMomentum
        {
            var bus = new InMemoryEventBus();
            using var constructor = new SemanticSceneConstructor(bus);
            constructor.SetState(
                process: "rider.exe",
                title: "MyBigProject",
                tabCount: 5,
                interruptionCount: 1,
                recentFileChanges: 10,
                focusStarted: DateTimeOffset.UtcNow.AddMinutes(-15) // Focused long time
            );
            Assert.Equal("ProjectMomentum", constructor.Classify());
        }
    }

    [Fact]
    public void TemporalFusionEngine_PrioritizesResolutionsCorrectly()
    {
        // Set up mock resolving nodes
        _nodeStore.Save(new WikiNode { NodeId = "doc_node", Title = "Doc Node", NodeType = WikiNodeType.Document, Facts = new List<WikiFact> { new() { Text = "path: c:\\projects\\doc.md" } } });
        _nodeStore.Save(new WikiNode { NodeId = "window_node", Title = "Window Node", NodeType = WikiNodeType.Concept, Facts = new List<WikiFact> { new() { Text = "window: *Engram*" } } });
        _nodeStore.Save(new WikiNode { NodeId = "workflow_node", Title = "Workflow Node", NodeType = WikiNodeType.Workflow, Facts = new List<WikiFact> { new() { Text = "alias: wf-123" } } });

        var bus = new InMemoryEventBus();
        var resolver = new CrossModalResolver(_nodeStore);
        using var engine = new TemporalFusionEngine(resolver, bus);

        // Publish events to set internal state
        // 1. Workflow changed
        bus.Publish(new EventEnvelope
        {
            EventType = "automation.worldmodel.changed",
            Payload = new { Property = "ActiveWorkflow", Value = "wf-123" }
        });

        // 2. Window changed
        bus.Publish(new EventEnvelope
        {
            EventType = "perception.active_window_changed",
            Payload = new { Process = "devenv", Title = "Engram Code Studio" }
        });

        // 3. Document changed
        bus.Publish(new EventEnvelope
        {
            EventType = "perception.file_changed",
            Payload = new { FilePath = "c:\\projects\\doc.md" }
        });

        // Force fusion
        var entry = engine.ForceFusion();

        // The document path has highest priority (should resolve to doc_node)
        Assert.Equal("doc_node", entry.ResolvedNodeId);

        // Now remove document path
        bus.Publish(new EventEnvelope
        {
            EventType = "automation.worldmodel.changed",
            Payload = new { Property = "ActiveDocument", Value = "" }
        });
        
        entry = engine.ForceFusion();
        // Without active document, window title wins (resolves to window_node)
        Assert.Equal("window_node", entry.ResolvedNodeId);
    }

    [Fact]
    public void UnifiedWorldModelService_CoordinatesFlowSuccessfully()
    {
        // Save source node and target nodes
        var nodeA = new WikiNode
        {
            NodeId = "node_a",
            Title = "Node A",
            NodeType = WikiNodeType.Concept,
            Facts = new List<WikiFact> { new() { Text = "window: *ActiveTask*" } },
            Edges = new List<WikiEdge>
            {
                new() { TargetNodeId = "node_op", RelationType = "rel", PropagationWeight = 1.0, MaxInfluence = 1.0, EvidenceThreshold = 0.0, PropagationType = "operational" }
            }
        };

        _nodeStore.Save(nodeA);
        _nodeStore.Save(new WikiNode { NodeId = "node_op", Title = "Node OP", NodeType = WikiNodeType.Concept });

        var bus = new InMemoryEventBus();
        
        // Listen for escalated tension event
        var tensionEvents = new List<EventEnvelope>();
        bus.Subscribe("reality.tension_escalated", env => tensionEvents.Add(env));

        using var service = new UnifiedWorldModelService(_nodeStore, bus);

        // Publish event simulating user switching to window matching nodeA
        bus.Publish(new EventEnvelope
        {
            EventType = "perception.active_window_changed",
            Payload = new { Process = "explorer", Title = "Focusing on ActiveTask" }
        });

        // Let the asynchronous/synchronous handlers complete
        // Get attention on nodeA and node_op
        double attentionA = service.Orchestrator.GetAttention("node_a");
        double attentionOp = service.Orchestrator.GetAttention("node_op");

        Assert.Equal(1.0, attentionA, 3);
        Assert.Equal(1.0, attentionOp, 3); // operational weight is 1.0, propagation weight is 1.0
    }
}
