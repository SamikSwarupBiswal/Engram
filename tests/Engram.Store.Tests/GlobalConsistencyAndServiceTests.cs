using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Engram.Store.Events;
using Engram.Store.Perception;
using Engram.Store.Reality;
using Engram.Store.Wiki;
using Xunit;

namespace Engram.Store.Tests;

public class GlobalConsistencyAndServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly WorkspacePaths _paths;
    private readonly WikiNodeStore _nodeStore;
    private readonly InMemoryEventBus _eventBus;

    public GlobalConsistencyAndServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram_consistency_test_" + Guid.NewGuid().ToString("n")[..8]);
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

    [Fact]
    public void ConsistencyEngine_EvaluatesClaimsWithSourceWeightAndDecay()
    {
        var engine = new GlobalConsistencyEngine
        {
            ClaimDecayHalfLifeSeconds = 86400.0 // 1 day
        };

        var node = new WikiNode
        {
            NodeId = "node_a",
            Title = "Node A",
            NodeType = WikiNodeType.Concept,
            Claims = new List<SemanticClaim>
            {
                // Active claim from user_statement: weight = 0.8 * 1.0 * 1.0 = 0.8
                new()
                {
                    ClaimId = "c1",
                    Property = "Status",
                    Value = "active",
                    Confidence = 0.8,
                    Source = "user_statement",
                    Timestamp = DateTimeOffset.UtcNow
                },
                // Active claim from inferred_inactivity: weight = 0.9 * 0.2 * 1.0 = 0.18
                new()
                {
                    ClaimId = "c2",
                    Property = "Status",
                    Value = "cancelled",
                    Confidence = 0.9,
                    Source = "inferred_inactivity",
                    Timestamp = DateTimeOffset.UtcNow
                }
            }
        };

        var analysis = engine.AnalyzeNode(node);

        Assert.Equal("active", analysis.DominantValues["Status"]);
        // tension = 0.18 / 0.8 = 0.225
        Assert.Equal(0.225, analysis.PropertyTensions["Status"], 3);

        // Now test decay. Set the "active" user statement 5 days ago so it decays significantly.
        // 5 days = 5 half-lives -> decay factor = 1 / (2^5) = 1/32 = 0.03125
        // Decayed weight = 0.8 * 1.0 * 0.03125 = 0.025
        // Now "cancelled" (weight = 0.18) should dominate.
        node.Claims[0].Timestamp = DateTimeOffset.UtcNow.AddDays(-5);

        var decayedAnalysis = engine.AnalyzeNode(node);
        Assert.Equal("cancelled", decayedAnalysis.DominantValues["Status"]);
        // dominant = cancelled (0.18), second = active (0.025)
        // tension = 0.025 / 0.18 = 0.1388...
        Assert.Equal(0.139, decayedAnalysis.PropertyTensions["Status"], 3);
    }

    [Fact]
    public void ConsistencyEngine_EscalatesWhenTensionBreachesExecutionBounds()
    {
        var engine = new GlobalConsistencyEngine
        {
            TensionEscalationThreshold = 0.5,
            ConfidenceCollapseThreshold = 0.3
        };

        // Workflow node (execution-critical) with competing claims
        var workflowNode = new WikiNode
        {
            NodeId = "workflow_test",
            Title = "Workflow Test",
            NodeType = WikiNodeType.Workflow,
            Claims = new List<SemanticClaim>
            {
                new()
                {
                    ClaimId = "c1",
                    Property = "State",
                    Value = "running",
                    Confidence = 0.8, // weight = 0.8 * 0.8 = 0.64
                    Source = "workflow_activity",
                    Timestamp = DateTimeOffset.UtcNow
                },
                new()
                {
                    ClaimId = "c2",
                    Property = "State",
                    Value = "paused",
                    Confidence = 0.7, // weight = 0.7 * 0.8 = 0.56
                    Source = "workflow_activity",
                    Timestamp = DateTimeOffset.UtcNow
                }
            }
        };

        var workflowAnalysis = engine.AnalyzeNode(workflowNode);
        // dominant state is running (0.64), paused is 0.56. tension = 0.56/0.64 = 0.875 >= 0.5
        // NodeType = Workflow -> should escalate
        Assert.Single(workflowAnalysis.Escalations);
        Assert.Equal("State", workflowAnalysis.Escalations[0].Property);
        Assert.Contains("impacts executing node", workflowAnalysis.Escalations[0].Reason);

        // Concept node (non-critical) with same claims.
        // By default, it shouldn't escalate because it's not a critical NodeType and dominant weight is 0.64 >= 0.3 (no confidence collapse)
        var conceptNode = new WikiNode
        {
            NodeId = "concept_test",
            Title = "Concept Test",
            NodeType = WikiNodeType.Concept,
            Claims = workflowNode.Claims
        };

        var conceptAnalysis = engine.AnalyzeNode(conceptNode, affectsExecution: false);
        Assert.Empty(conceptAnalysis.Escalations);

        // Now test affectsExecution = true on the Concept node
        var conceptExecutionAnalysis = engine.AnalyzeNode(conceptNode, affectsExecution: true);
        Assert.Single(conceptExecutionAnalysis.Escalations);
        Assert.Contains("impacts executing node", conceptExecutionAnalysis.Escalations[0].Reason);
    }

    [Fact]
    public void ConsistencyEngine_EscalatesOnConfidenceCollapse()
    {
        var engine = new GlobalConsistencyEngine
        {
            TensionEscalationThreshold = 0.5,
            ConfidenceCollapseThreshold = 0.3
        };

        // Concept node with high tension but very low confidence (both weights < 0.3)
        var conceptNode = new WikiNode
        {
            NodeId = "concept_low_confidence",
            Title = "Low Confidence Concept",
            NodeType = WikiNodeType.Concept,
            Claims = new List<SemanticClaim>
            {
                new()
                {
                    ClaimId = "c1",
                    Property = "Reality",
                    Value = "real",
                    Confidence = 0.9, // weight = 0.9 * 0.2 = 0.18 (source: inferred_inactivity)
                    Source = "inferred_inactivity",
                    Timestamp = DateTimeOffset.UtcNow
                },
                new()
                {
                    ClaimId = "c2",
                    Property = "Reality",
                    Value = "fake",
                    Confidence = 0.8, // weight = 0.8 * 0.2 = 0.16 (source: inferred_inactivity)
                    Source = "inferred_inactivity",
                    Timestamp = DateTimeOffset.UtcNow
                }
            }
        };

        var analysis = engine.AnalyzeNode(conceptNode);
        // dominant is real (0.18), second is fake (0.16). tension = 0.16/0.18 = 0.888 >= 0.5.
        // dominant weight = 0.18 < 0.3 (confidence collapse) -> should escalate!
        Assert.Single(analysis.Escalations);
        Assert.Contains("Confidence collapse", analysis.Escalations[0].Reason);
    }

    [Fact]
    public void UnifiedWorldModelService_EndToEndIntegration()
    {
        // 1. Setup service
        using var service = new UnifiedWorldModelService(_nodeStore, _eventBus);

        // 2. Setup target nodes
        var nodeA = new WikiNode
        {
            NodeId = "proj_engram",
            Title = "Engram Project",
            NodeType = WikiNodeType.Project,
            Facts = new List<WikiFact>
            {
                new() { Text = "path: c:\\projects\\engram" }
            },
            Edges = new List<WikiEdge>
            {
                new()
                {
                    TargetNodeId = "concept_reality",
                    RelationType = "uses",
                    PropagationWeight = 0.8,
                    MaxInfluence = 1.0,
                    EvidenceThreshold = 0.1,
                    PropagationType = "operational"
                }
            },
            Claims = new List<SemanticClaim>
            {
                new()
                {
                    ClaimId = "c1",
                    Property = "Status",
                    Value = "active",
                    Confidence = 0.9,
                    Source = "user_statement",
                    Timestamp = DateTimeOffset.UtcNow
                }
            }
        };

        var nodeB = new WikiNode
        {
            NodeId = "concept_reality",
            Title = "Reality Modeling",
            NodeType = WikiNodeType.Concept
        };

        _nodeStore.Save(nodeA);
        _nodeStore.Save(nodeB);

        // Refresh the resolver to index the new nodes
        service.Resolver.Refresh();

        // 3. Setup event listeners
        ConsistencyAnalysis? tensionEscalatedPayload = null;
        using var sub = _eventBus.Subscribe("reality.tension_escalated", envelope =>
        {
            tensionEscalatedPayload = envelope.Payload as ConsistencyAnalysis;
        });

        // 4. Publish a file change event inside the project path
        var fileEvent = new SemanticFileEvent
        {
            FilePath = "c:\\projects\\engram\\src\\main.cs",
            FileName = "main.cs",
            Timestamp = DateTimeOffset.UtcNow
        };

        _eventBus.Publish(new EventEnvelope
        {
            EventType = "perception.file_changed",
            Source = "file_watcher",
            Payload = fileEvent
        });

        // Sleep to let event processing finish
        Thread.Sleep(200);

        // 5. Verify the attention states
        double attentionA = service.Orchestrator.GetAttention("proj_engram");
        double attentionB = service.Orchestrator.GetAttention("concept_reality");

        Assert.Equal(1.0, attentionA, 2);
        // attentionB should be decayed from: 1.0 * 0.8 * 1.0 = 0.8
        Assert.Equal(0.8, attentionB, 2);

        // Verify nodes salience on disk
        var reloadedA = _nodeStore.Load("proj_engram");
        var reloadedB = _nodeStore.Load("concept_reality");

        Assert.NotNull(reloadedA);
        Assert.NotNull(reloadedB);
        Assert.Equal(attentionA, reloadedA.Salience, 2);
        Assert.Equal(attentionB, reloadedB.Salience, 2);

        // Since nodeA has status = active (confidence 0.9, weight 0.9) with no competing claims, no escalation should happen.
        Assert.Null(tensionEscalatedPayload);

        // 6. Introduce competing claim to nodeA to trigger tension and escalation
        reloadedA.Claims.Add(new SemanticClaim
        {
            ClaimId = "c2",
            Property = "Status",
            Value = "cancelled",
            Confidence = 0.8,
            Source = "user_statement",
            Timestamp = DateTimeOffset.UtcNow
        });
        _nodeStore.Save(reloadedA);

        // Set an active workflow ID to simulate executing context
        var modelChangedPayload = new
        {
            Property = "ActiveWorkflow",
            Value = "workflow_1"
        };
        _eventBus.Publish(new EventEnvelope
        {
            EventType = "automation.worldmodel.changed",
            Source = "test",
            Payload = modelChangedPayload
        });

        // Trigger fusion by updating file path again
        _eventBus.Publish(new EventEnvelope
        {
            EventType = "perception.file_changed",
            Source = "file_watcher",
            Payload = fileEvent
        });

        // Sleep to let event processing finish
        Thread.Sleep(200);

        // Now escalation should be triggered
        Assert.NotNull(tensionEscalatedPayload);
        Assert.Equal("proj_engram", tensionEscalatedPayload.NodeId);
        Assert.Single(tensionEscalatedPayload.Escalations);
        Assert.Equal("Status", tensionEscalatedPayload.Escalations[0].Property);
    }
}
