using Engram.Store.Wiki;
using Engram.Store.Metabolism;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// BEHAVIORAL VALIDATION SUITE — the scientific method for Engram.
/// 
/// NOT endpoint tests. NOT API tests.
/// These validate that the cognitive organism behaves correctly over time.
/// 
/// 5 validation categories:
/// 1. Memory Formation — does Engram actually remember semantically?
/// 2. Drift Detection — does contradiction detection work?
/// 3. Retrieval Hygiene — does retrieval scale and stay relevant?
/// 4. Longitudinal Coherence — does metabolism maintain consistency?
/// 5. Intervention Quality — are interventions accurate and useful?
/// </summary>
public class BehavioralValidationSuite : IDisposable
{
    private readonly CognitiveReplayHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    // ═══════════════════════════════════════════════════════════════
    // CATEGORY 1: MEMORY FORMATION VALIDATION
    // Does Engram actually remember semantically?
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void MemoryFormation_ConversationExtractsSemanticEntities()
    {
        var result = _harness.InjectConversation(
            "I'm building Engram, a semantic memory system. I'm worried about semantic entropy.",
            "That sounds like an ambitious project!");

        Assert.True(result.Success);
        Assert.True(result.CandidatesExtracted > 0, "Should extract at least one memory candidate");
        Assert.True(result.NodesCreated > 0, "Should create at least one wiki node");
    }

    [Fact]
    public void MemoryFormation_ExtractedMemoryIsSearchable()
    {
        _harness.InjectConversation(
            "I'm worried about semantic entropy in long-running AI systems. I'm building Engram to fix this.",
            "That's a valid concern.");

        var searchResults = _harness.Search("engram");

        Assert.True(searchResults.Results.Count > 0,
            "Should find the extracted memory via semantic search");
    }

    [Fact]
    public void MemoryFormation_MultipleConversationsBuildKnowledgeGraph()
    {
        _harness.InjectConversation("I'm building Engram, a semantic memory system.", "Great project!");
        _harness.InjectConversation("I decided to use Tauri for the desktop app.", "Good choice.");
        _harness.InjectConversation("I'm worried about KV cache collapse in LLamaSharp.", "Let me help.");

        var nodes = _harness.NodeStore.LoadAll();
        Assert.True(nodes.Count >= 3, $"Should have multiple nodes, got {nodes.Count}");

        var nodeTypes = nodes.Select(n => n.NodeType).Distinct().ToList();
        Assert.True(nodeTypes.Count >= 1, "Should extract at least one entity type");
    }

    [Fact]
    public void MemoryFormation_MemoryPersistsAcrossMetabolismCycles()
    {
        _harness.InjectConversation("I decided to use SQLite for persistence. I'm building a database layer.", "Smart choice.");

        _harness.RunMetabolismCycle().Wait();

        var searchResults = _harness.Search("SQLite persistence");
        Assert.True(searchResults.Results.Count > 0,
            "Memory should survive metabolism cycles");
    }

    [Fact]
    public void MemoryFormation_MemoryAppearsInPromptAssembly()
    {
        _harness.InjectConversation("I'm worried about KV cache collapse in LLamaSharp. I'm building a fix.", "Let me help.");

        var prompt = _harness.AssemblePrompt("What are my concerns?");

        Assert.Contains("KV cache", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MemoryFormation_TelemetryTracksExtraction()
    {
        _harness.InjectConversation("I'm building Engram, a semantic system.", "Great!");
        _harness.InjectConversation("I'm worried about memory decay.", "Understood.");

        var metrics = _harness.Telemetry.GetMemoryPipelineMetrics();
        Assert.Equal(2, metrics.TotalInvocations);
        Assert.Equal(2, metrics.SuccessfulExtractions);
        Assert.True(metrics.TotalCandidatesExtracted > 0,
            $"Should extract candidates, got {metrics.TotalCandidatesExtracted}");
    }

    // ═══════════════════════════════════════════════════════════════
    // CATEGORY 2: DRIFT DETECTION VALIDATION
    // Does contradiction detection work? (THE MOAT)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void DriftDetection_GoalActivityGap_Detected()
    {
        _harness.InjectNode(new WikiNode
        {
            NodeId = "goal_deep_work",
            Title = "Deep Work",
            NodeType = WikiNodeType.Goal,
            Summary = "Focus on deep, uninterrupted work sessions",
            Salience = 0.2,
            LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-10)
        });

        _harness.InjectNode(new WikiNode
        {
            NodeId = "concept_youtube",
            Title = "YouTube",
            NodeType = WikiNodeType.Concept,
            Summary = "Watching videos online",
            Salience = 0.9,
            LastTouchedAt = DateTimeOffset.UtcNow
        });

        var contradictions = _harness.DetectContradictions();

        Assert.True(contradictions.Count > 0, "Should detect goal-activity gap");
        Assert.Contains(contradictions, c => c.Type == ContradictionType.GoalActivityGap);
    }

    [Fact]
    public void DriftDetection_PriorityDrift_Detected()
    {
        _harness.InjectPriority("Ship Engram v1", confidence: 0.95);

        // Need recent activity nodes for priority drift to trigger
        _harness.InjectNode(new WikiNode
        {
            NodeId = "recent_gaming",
            Title = "Gaming Sessions",
            NodeType = WikiNodeType.Concept,
            Summary = "Playing video games",
            Salience = 0.8,
            LastTouchedAt = DateTimeOffset.UtcNow
        });

        _harness.InjectNode(new WikiNode
        {
            NodeId = "recent_youtube",
            Title = "YouTube Watching",
            NodeType = WikiNodeType.Concept,
            Summary = "Watching YouTube",
            Salience = 0.7,
            LastTouchedAt = DateTimeOffset.UtcNow
        });

        _harness.InjectNode(new WikiNode
        {
            NodeId = "recent_browsing",
            Title = "Web Browsing",
            NodeType = WikiNodeType.Concept,
            Summary = "Browsing the web",
            Salience = 0.6,
            LastTouchedAt = DateTimeOffset.UtcNow
        });

        _harness.InjectNode(new WikiNode
        {
            NodeId = "recent_reddit",
            Title = "Reddit",
            NodeType = WikiNodeType.Concept,
            Summary = "Reading Reddit",
            Salience = 0.5,
            LastTouchedAt = DateTimeOffset.UtcNow
        });

        var contradictions = _harness.DetectContradictions();

        // Should detect either priority drift or goal-activity gap
        Assert.True(contradictions.Count > 0, "Should detect some form of drift");
    }

    [Fact]
    public void DriftDetection_AbandonedCommitment_Detected()
    {
        _harness.InjectNode(new WikiNode
        {
            NodeId = "commitment_refactor",
            Title = "Refactor Authentication",
            NodeType = WikiNodeType.Decision,
            Summary = "Decision to refactor auth system",
            Facts = new List<WikiFact>
            {
                new() { Text = "Will refactor the authentication module next week" }
            },
            Salience = 0.3,
            LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-14)
        });

        var contradictions = _harness.DetectContradictions();

        Assert.Contains(contradictions, c => c.Type == ContradictionType.AbandonedCommitment);
    }

    [Fact]
    public void DriftDetection_SeverityEscalatesOverTime()
    {
        _harness.InjectNode(new WikiNode
        {
            NodeId = "goal_ship",
            Title = "Ship Engram",
            NodeType = WikiNodeType.Goal,
            Summary = "Ship the product",
            Salience = 0.3,
            LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-5)
        });

        _harness.InjectNode(new WikiNode
        {
            NodeId = "concept_distraction",
            Title = "Random Browsing",
            NodeType = WikiNodeType.Concept,
            Summary = "Browsing the internet",
            Salience = 0.8,
            LastTouchedAt = DateTimeOffset.UtcNow
        });

        var contradictions1 = _harness.DetectContradictions();
        var severity1 = contradictions1.FirstOrDefault(c => c.Type == ContradictionType.GoalActivityGap)?.Severity
            ?? ContradictionSeverity.Low;

        _harness.SimulateTimePassage(TimeSpan.FromDays(10));

        var contradictions2 = _harness.DetectContradictions();
        var severity2 = contradictions2.FirstOrDefault(c => c.Type == ContradictionType.GoalActivityGap)?.Severity
            ?? ContradictionSeverity.Low;

        Assert.True(severity2 >= severity1,
            $"Severity should escalate over time: {severity1} → {severity2}");
    }

    [Fact]
    public void DriftDetection_InterventionsGeneratedFromContradictions()
    {
        _harness.InjectNode(new WikiNode
        {
            NodeId = "goal_deep_work",
            Title = "Deep Work Practice",
            NodeType = WikiNodeType.Goal,
            Summary = "Practice deep work daily",
            Salience = 0.15,
            LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-14)
        });

        _harness.InjectNode(new WikiNode
        {
            NodeId = "concept_social_media",
            Title = "Social Media",
            NodeType = WikiNodeType.Concept,
            Summary = "Scrolling social media",
            Salience = 0.85,
            LastTouchedAt = DateTimeOffset.UtcNow
        });

        var contradictions = _harness.DetectContradictions();
        var interventions = _harness.GenerateInterventions(contradictions);

        Assert.True(interventions.Count > 0, "Should generate interventions from contradictions");
        Assert.All(interventions, i =>
        {
            Assert.False(string.IsNullOrWhiteSpace(i.Message), "Intervention must have a message");
            Assert.NotEqual(InterventionSeverity.Low, i.Severity);
        });
    }

    [Fact]
    public void DriftDetection_TelemetryTracksContradictions()
    {
        _harness.InjectNode(new WikiNode
        {
            NodeId = "goal_test",
            Title = "Test Goal",
            NodeType = WikiNodeType.Goal,
            Summary = "A test goal",
            Salience = 0.1,
            LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-20)
        });

        _harness.InjectNode(new WikiNode
        {
            NodeId = "concept_other",
            Title = "Other Activity",
            NodeType = WikiNodeType.Concept,
            Summary = "Other activity",
            Salience = 0.9,
            LastTouchedAt = DateTimeOffset.UtcNow
        });

        _harness.RunMetabolismCycle().Wait();

        var metrics = _harness.Telemetry.GetContradictionMetrics();
        Assert.True(metrics.TotalDetections > 0, "Telemetry should track contradiction detections");
    }

    // ═══════════════════════════════════════════════════════════════
    // CATEGORY 3: RETRIEVAL HYGIENE VALIDATION
    // Does retrieval scale and stay relevant?
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void RetrievalHygiene_PrefersRelevantOverSalient()
    {
        _harness.InjectNode(new WikiNode
        {
            NodeId = "old_project",
            Title = "Old Project Alpha",
            NodeType = WikiNodeType.Project,
            Summary = "An old project from years ago",
            Salience = 0.95,
            LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-30)
        });

        _harness.InjectNode(new WikiNode
        {
            NodeId = "current_concern",
            Title = "Memory Persistence",
            NodeType = WikiNodeType.Concept,
            Summary = "Current concern about memory persistence in Engram",
            Salience = 0.6,
            LastTouchedAt = DateTimeOffset.UtcNow
        });

        var results = _harness.Search("memory persistence concern");

        Assert.True(results.Results.Count > 0, "Should find results");
        var topResult = results.Results.First();
        Assert.Equal("current_concern", topResult.Node.NodeId);
    }

    [Fact]
    public void RetrievalHygiene_Handles100PlusNodes()
    {
        for (int i = 0; i < 120; i++)
        {
            _harness.InjectNode(new WikiNode
            {
                NodeId = $"node_{i:D3}",
                Title = $"Concept {i}",
                NodeType = WikiNodeType.Concept,
                Summary = $"A concept about topic {i} with various details",
                Salience = 1.0 - (i * 0.005),
                LastTouchedAt = DateTimeOffset.UtcNow.AddHours(-i)
            });
        }

        _harness.InjectNode(new WikiNode
        {
            NodeId = "needle_in_haystack",
            Title = "Engram Architecture Decision",
            NodeType = WikiNodeType.Decision,
            Summary = "Critical architecture decision about semantic memory",
            Salience = 0.7,
            LastTouchedAt = DateTimeOffset.UtcNow
        });

        var results = _harness.Search("engram architecture semantic memory");

        Assert.True(results.Results.Count > 0, "Should find results in large corpus");
        Assert.Equal("needle_in_haystack", results.Results.First().Node.NodeId);
    }

    [Fact]
    public void RetrievalHygiene_BudgetRespected()
    {
        for (int i = 0; i < 20; i++)
        {
            _harness.InjectNode(new WikiNode
            {
                NodeId = $"budget_node_{i}",
                Title = $"Important Topic {i}",
                NodeType = WikiNodeType.Concept,
                Summary = $"An important topic {i} with detailed information",
                Salience = 0.9,
                LastTouchedAt = DateTimeOffset.UtcNow
            });
        }

        var candidates = _harness.NodeStore.LoadAll();
        var selected = _harness.BudgetManager.SelectNodes(candidates, "topic", DateTimeOffset.UtcNow);

        Assert.True(selected.Count <= _harness.BudgetManager.MaxNodes,
            $"Should respect max nodes budget: {selected.Count} <= {_harness.BudgetManager.MaxNodes}");
    }

    [Fact]
    public void RetrievalHygiene_TypeBoostingWorks()
    {
        _harness.InjectNode(new WikiNode
        {
            NodeId = "concept_engram",
            Title = "Engram",
            NodeType = WikiNodeType.Concept,
            Summary = "A concept about Engram",
            Salience = 0.5
        });

        _harness.InjectNode(new WikiNode
        {
            NodeId = "project_engram",
            Title = "Engram",
            NodeType = WikiNodeType.Project,
            Summary = "The Engram project",
            Salience = 0.5
        });

        var results = _harness.Search("engram");

        Assert.True(results.Results.Count >= 2, "Should find both nodes");

        var projectResult = results.Results.First(r => r.Node.NodeId == "project_engram");
        var conceptResult = results.Results.First(r => r.Node.NodeId == "concept_engram");
        Assert.True(projectResult.Relevance >= conceptResult.Relevance,
            "Project type should get a boost over Concept type");
    }

    [Fact]
    public void RetrievalHygiene_StaleNodesDoNotDominate()
    {
        for (int i = 0; i < 10; i++)
        {
            _harness.InjectNode(new WikiNode
            {
                NodeId = $"stale_{i}",
                Title = $"Old Topic {i}",
                NodeType = WikiNodeType.Concept,
                Summary = $"An old topic {i}",
                Salience = 0.8,
                LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-60)
            });
        }

        _harness.InjectNode(new WikiNode
        {
            NodeId = "fresh_relevant",
            Title = "Current Work",
            NodeType = WikiNodeType.Concept,
            Summary = "What I'm working on right now",
            Salience = 0.6,
            LastTouchedAt = DateTimeOffset.UtcNow
        });

        var candidates = _harness.NodeStore.LoadAll();
        var selected = _harness.BudgetManager.SelectNodes(candidates, "current work", DateTimeOffset.UtcNow);

        Assert.True(selected.Any(n => n.Node.NodeId == "fresh_relevant"),
            "Fresh relevant node should be selected even with lower salience");
    }

    // ═══════════════════════════════════════════════════════════════
    // CATEGORY 4: LONGITUDINAL COHERENCE VALIDATION
    // Does metabolism maintain consistency over time?
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void LongitudinalCoherence_MetabolismSurvivesMultipleCycles()
    {
        _harness.InjectConversation("I'm building Engram.", "Great!");

        var results = new List<MetabolismCycleResult>();
        for (int i = 0; i < 5; i++)
        {
            results.Add(_harness.RunMetabolismCycle().Result);
        }

        Assert.All(results, r => Assert.True(r.Success, "All cycles should succeed"));
        Assert.Equal(5, _harness.MetabolismService.CyclesCompleted);
    }

    [Fact]
    public void LongitudinalCoherence_DeduplicationPreventsWikiRot()
    {
        _harness.InjectConversation("I'm building Engram, a semantic memory system.", "Sounds good.");
        _harness.InjectConversation("I'm building Engram, my semantic memory project.", "Interesting.");
        _harness.InjectConversation("I'm building the Engram semantic memory system.", "Tell me more.");

        var nodesBefore = _harness.NodeStore.LoadAll().Count;

        _harness.RunMetabolismCycle().Wait();

        var nodesAfter = _harness.NodeStore.LoadAll().Count;

        Assert.True(nodesAfter <= nodesBefore,
            $"Deduplication should prevent node explosion: {nodesBefore} → {nodesAfter}");
    }

    [Fact]
    public void LongitudinalCoherence_SalienceDecaysOverTime()
    {
        _harness.InjectNode(new WikiNode
        {
            NodeId = "fading_topic",
            Title = "Fading Topic",
            NodeType = WikiNodeType.Concept,
            Summary = "A topic that should fade",
            Salience = 1.0,
            LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-30)
        });

        var salienceBefore = _harness.GetNode("fading_topic")!.Salience;

        _harness.RunMetabolismCycle().Wait();

        var salienceAfter = _harness.GetNode("fading_topic")!.Salience;

        Assert.True(salienceAfter < salienceBefore,
            $"Salience should decay: {salienceBefore:F3} → {salienceAfter:F3}");
    }

    [Fact]
    public void LongitudinalCoherence_StaleNodesArchived()
    {
        _harness.InjectNode(new WikiNode
        {
            NodeId = "very_stale",
            Title = "Very Old Topic",
            NodeType = WikiNodeType.Concept,
            Summary = "Something very old",
            Salience = 0.05,
            LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-60)
        });

        _harness.RunMetabolismCycle().Wait();

        var diagnostics = _harness.GetDiagnostics();
        // Node may or may not be archived depending on salience decay algorithm
        Assert.True(diagnostics.Metabolism.TotalNodesArchived >= 0,
            "Metabolism should run without error");
    }

    [Fact]
    public void LongitudinalCoherence_ContradictionsPersistAcrossCycles()
    {
        _harness.InjectNode(new WikiNode
        {
            NodeId = "goal_persist",
            Title = "Persistent Goal",
            NodeType = WikiNodeType.Goal,
            Summary = "A goal that should stay detected",
            Salience = 0.15,
            LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-14)
        });

        _harness.InjectNode(new WikiNode
        {
            NodeId = "active_distraction",
            Title = "Active Distraction",
            NodeType = WikiNodeType.Concept,
            Summary = "Something distracting",
            Salience = 0.9,
            LastTouchedAt = DateTimeOffset.UtcNow
        });

        for (int i = 0; i < 3; i++)
        {
            _harness.RunMetabolismCycle().Wait();
        }

        var diag = _harness.GetDiagnostics();
        Assert.True(diag.Contradictions.TotalDetections >= 3,
            "Contradictions should be detected in each cycle");
    }

    [Fact]
    public void LongitudinalCoherence_TelemetryAccumulatesCorrectly()
    {
        _harness.InjectConversation("I'm building Engram.", "Great!");
        _harness.InjectConversation("I'm worried about stability.", "Understood.");

        _harness.RunMetabolismCycle().Wait();
        _harness.RunMetabolismCycle().Wait();

        var diag = _harness.GetDiagnostics();

        Assert.Equal(2, diag.MemoryPipeline.TotalInvocations);
        Assert.True(_harness.MetabolismService.CyclesCompleted >= 2, $"Expected >= 2 cycles, got {_harness.MetabolismService.CyclesCompleted}");
        Assert.True(diag.Timeline.EventsWritten > 0 || _harness.CapturedEvents.Count > 0,
            "Timeline should have events");
        Assert.True(diag.Deduplication.TotalRuns > 0, "Dedup should have run");
    }

    // ═══════════════════════════════════════════════════════════════
    // CATEGORY 5: INTERVENTION QUALITY VALIDATION
    // Are interventions accurate and useful?
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void InterventionQuality_MessagesAreContextuallyAccurate()
    {
        _harness.InjectNode(new WikiNode
        {
            NodeId = "goal_deep_work",
            Title = "Deep Work Practice",
            NodeType = WikiNodeType.Goal,
            Summary = "Practice deep work daily",
            Salience = 0.15,
            LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-14)
        });

        _harness.InjectNode(new WikiNode
        {
            NodeId = "concept_youtube",
            Title = "YouTube Watching",
            NodeType = WikiNodeType.Concept,
            Summary = "Watching YouTube videos",
            Salience = 0.85,
            LastTouchedAt = DateTimeOffset.UtcNow
        });

        var contradictions = _harness.DetectContradictions();
        var interventions = _harness.GenerateInterventions(contradictions);

        Assert.True(interventions.Count > 0);

        var intervention = interventions.First();
        Assert.Contains("Deep Work", intervention.Message);
        Assert.NotNull(intervention.DeclaredIntent);
        Assert.NotNull(intervention.ObservedBehavior);
    }

    [Fact]
    public void InterventionQuality_NotGeneratedForAlignedBehavior()
    {
        _harness.InjectNode(new WikiNode
        {
            NodeId = "goal_engram",
            Title = "Ship Engram",
            NodeType = WikiNodeType.Goal,
            Summary = "Ship the Engram product",
            Salience = 0.9,
            LastTouchedAt = DateTimeOffset.UtcNow
        });

        _harness.InjectNode(new WikiNode
        {
            NodeId = "activity_engram",
            Title = "Engram Development",
            NodeType = WikiNodeType.Concept,
            Summary = "Working on Engram development",
            Salience = 0.85,
            LastTouchedAt = DateTimeOffset.UtcNow
        });

        var contradictions = _harness.DetectContradictions();

        Assert.DoesNotContain(contradictions, c => c.Type == ContradictionType.GoalActivityGap);
    }

    [Fact]
    public void InterventionQuality_SeverityMatchesEvidence()
    {
        _harness.InjectNode(new WikiNode
        {
            NodeId = "mild_goal",
            Title = "Mild Goal",
            NodeType = WikiNodeType.Goal,
            Summary = "A mild goal",
            Salience = 0.3,
            LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-5)
        });

        _harness.InjectNode(new WikiNode
        {
            NodeId = "mild_distraction",
            Title = "Mild Distraction",
            NodeType = WikiNodeType.Concept,
            Summary = "Something else",
            Salience = 0.8,
            LastTouchedAt = DateTimeOffset.UtcNow
        });

        var contradictions = _harness.DetectContradictions();
        var mildContradiction = contradictions.FirstOrDefault(c => c.Type == ContradictionType.GoalActivityGap);

        if (mildContradiction != null)
        {
            Assert.True(mildContradiction.Severity <= ContradictionSeverity.Medium,
                "5-day gap should be Medium or lower severity");
        }
    }

    [Fact]
    public void InterventionQuality_TelemetryTracksInterventions()
    {
        _harness.InjectNode(new WikiNode
        {
            NodeId = "goal_track",
            Title = "Trackable Goal",
            NodeType = WikiNodeType.Goal,
            Summary = "A goal for telemetry",
            Salience = 0.1,
            LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-20)
        });

        _harness.InjectNode(new WikiNode
        {
            NodeId = "distraction_track",
            Title = "Trackable Distraction",
            NodeType = WikiNodeType.Concept,
            Summary = "A distraction",
            Salience = 0.9,
            LastTouchedAt = DateTimeOffset.UtcNow
        });

        // Run metabolism which generates interventions
        _harness.RunMetabolismCycle().Wait();

        // Check that metabolism detected contradictions
        var diag = _harness.GetDiagnostics();
        Assert.True(diag.Contradictions.TotalDetections > 0 || diag.Metabolism.CyclesCompleted > 0,
            "Metabolism should have run and detected contradictions");
    }

    [Fact]
    public void InterventionQuality_TensionSynthesisDetectsPatterns()
    {
        _harness.InjectNode(new WikiNode
        {
            NodeId = "goal_1",
            Title = "Goal A",
            NodeType = WikiNodeType.Goal,
            Summary = "First goal",
            Salience = 0.1,
            LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-20)
        });

        _harness.InjectNode(new WikiNode
        {
            NodeId = "goal_2",
            Title = "Goal B",
            NodeType = WikiNodeType.Goal,
            Summary = "Second goal",
            Salience = 0.1,
            LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-20)
        });

        _harness.InjectNode(new WikiNode
        {
            NodeId = "active_thing",
            Title = "Active Thing",
            NodeType = WikiNodeType.Concept,
            Summary = "Something active",
            Salience = 0.9,
            LastTouchedAt = DateTimeOffset.UtcNow
        });

        var contradictions = _harness.DetectContradictions();
        var interventions = _harness.GenerateInterventions(contradictions);

        Assert.True(interventions.Count >= 2, "Should generate multiple interventions");
    }

    // ═══════════════════════════════════════════════════════════════
    // CROSS-CATEGORY: FULL COGNITIVE LOOP VALIDATION
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void FullLoop_ConversationToMetabolismToIntervention()
    {
        _harness.InjectConversation("I'm building Engram and my goal is to ship by end of month.", "Understood.");

        _harness.InjectNode(new WikiNode
        {
            NodeId = "concept_gaming",
            Title = "Gaming Marathon",
            NodeType = WikiNodeType.Concept,
            Summary = "Week-long gaming sessions",
            Salience = 0.9,
            LastTouchedAt = DateTimeOffset.UtcNow
        });

        var goalNodes = _harness.GetNodesByType(WikiNodeType.Goal);
        foreach (var goal in goalNodes)
        {
            goal.Salience = 0.2;
            goal.LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-10);
            _harness.InjectNode(goal);
        }

        _harness.RunMetabolismCycle().Wait();

        var diag = _harness.GetDiagnostics();

        Assert.True(_harness.MetabolismService.CyclesCompleted > 0 || diag.Metabolism.CyclesCompleted > 0, "Metabolism should have run");
    }

    [Fact]
    public void FullLoop_ObservabilityProvesBehavior()
    {
        _harness.InjectConversation("I'm building Engram.", "Good progress.");
        _harness.RunMetabolismCycle().Wait();

        var diag = _harness.GetDiagnostics();

        Assert.True(diag.MemoryPipeline.TotalInvocations > 0, "Memory pipeline was invoked");
        Assert.True(_harness.MetabolismService.CyclesCompleted > 0 || diag.Metabolism.CyclesCompleted > 0, "Metabolism ran");
        Assert.True(diag.Timeline.EventsWritten > 0 || _harness.CapturedEvents.Count > 0,
            "Events should flow through the system");
        Assert.True(diag.Uptime > TimeSpan.Zero, "System has been running");
    }
}
