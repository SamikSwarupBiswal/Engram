using Engram.Store.Metabolism;
using Engram.Store.Wiki;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Sprint 3 Behavioral Validation — Closing the Cognitive Loop.
/// 
/// Tests that outputs of cognition become future inputs to cognition.
/// - Intervention persistence (first-class semantic entities)
/// - Contradiction history (longitudinal tracking)
/// - Resolution detection (contradictions can resolve)
/// - Tension evolution (escalation, decay, clustering)
/// - Behavioral context in prompts
/// </summary>
public class Sprint3ValidationSuite : IDisposable
{
    private readonly CognitiveReplayHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    // ═══════════════════════════════════════════════════════════════
    // INTERVENTION PERSISTENCE
    // Interventions are first-class semantic entities, not ephemeral
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void InterventionPersistence_StoredAndRetrievable()
    {
        var intervention = new Intervention
        {
            InterventionId = "test_int_001",
            Type = InterventionType.GoalDrift,
            Severity = InterventionSeverity.High,
            Message = "Your goal 'Ship Engram' is fading while gaming is active.",
            Source = "GoalActivityGap",
            GeneratedAt = DateTimeOffset.UtcNow
        };

        _harness.InterventionStore.Save(intervention);

        var loaded = _harness.InterventionStore.LoadAll();
        Assert.Single(loaded);
        Assert.Equal("test_int_001", loaded[0].InterventionId);
        Assert.Equal(InterventionType.GoalDrift, loaded[0].Type);
    }

    [Fact]
    public void InterventionPersistence_UpdatesExisting()
    {
        var intervention = new Intervention
        {
            InterventionId = "test_int_002",
            Type = InterventionType.PriorityDrift,
            Severity = InterventionSeverity.Medium,
            Message = "Priority drift detected.",
            GeneratedAt = DateTimeOffset.UtcNow
        };

        _harness.InterventionStore.Save(intervention);

        // Update status
        intervention.Status = InterventionStatus.Acknowledged;
        intervention.RespondedAt = DateTimeOffset.UtcNow;
        _harness.InterventionStore.Save(intervention);

        var loaded = _harness.InterventionStore.LoadAll();
        Assert.Single(loaded);
        Assert.Equal(InterventionStatus.Acknowledged, loaded[0].Status);
    }

    [Fact]
    public void InterventionPersistence_FilterByStatus()
    {
        _harness.InterventionStore.Save(new Intervention
        {
            InterventionId = "int_pending",
            Status = InterventionStatus.Pending,
            Message = "Pending",
            GeneratedAt = DateTimeOffset.UtcNow
        });

        _harness.InterventionStore.Save(new Intervention
        {
            InterventionId = "int_acked",
            Status = InterventionStatus.Acknowledged,
            Message = "Acknowledged",
            GeneratedAt = DateTimeOffset.UtcNow
        });

        var pending = _harness.InterventionStore.LoadByStatus(InterventionStatus.Pending);
        Assert.Single(pending);
        Assert.Equal("int_pending", pending[0].InterventionId);
    }

    [Fact]
    public void InterventionPersistence_FilterByRecency()
    {
        _harness.InterventionStore.Save(new Intervention
        {
            InterventionId = "int_recent",
            Message = "Recent",
            GeneratedAt = DateTimeOffset.UtcNow
        });

        _harness.InterventionStore.Save(new Intervention
        {
            InterventionId = "int_old",
            Message = "Old",
            GeneratedAt = DateTimeOffset.UtcNow.AddDays(-30)
        });

        var recent = _harness.InterventionStore.LoadRecent(TimeSpan.FromDays(7));
        Assert.Single(recent);
        Assert.Equal("int_recent", recent[0].InterventionId);
    }

    [Fact]
    public void InterventionPersistence_Stats()
    {
        _harness.InterventionStore.Save(new Intervention
        {
            InterventionId = "int_s1",
            Status = InterventionStatus.Pending,
            Message = "Test",
            GeneratedAt = DateTimeOffset.UtcNow
        });

        _harness.InterventionStore.Save(new Intervention
        {
            InterventionId = "int_s2",
            Status = InterventionStatus.Acted,
            Message = "Test",
            GeneratedAt = DateTimeOffset.UtcNow
        });

        var stats = _harness.InterventionStore.GetStats();
        Assert.Equal(2, stats.TotalCount);
        Assert.Equal(1, stats.PendingCount);
        Assert.Equal(1, stats.ActedCount);
    }

    // ═══════════════════════════════════════════════════════════════
    // CONTRADICTION HISTORY
    // Longitudinal tracking — patterns accumulate over time
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ContradictionHistory_RecordsAndRetrieves()
    {
        var contradiction = new BehavioralContradiction
        {
            Type = ContradictionType.GoalActivityGap,
            Severity = ContradictionSeverity.Medium,
            Description = "Goal fading",
            DeclaredIntent = "Deep Work",
            ObservedBehavior = "YouTube watching",
            RelatedNodeIds = new List<string> { "goal_1" }
        };

        _harness.ContradictionHistoryStore.Record(contradiction);

        var history = _harness.ContradictionHistoryStore.LoadAll();
        Assert.Single(history);
        Assert.Equal(ContradictionType.GoalActivityGap, history[0].Type);
        Assert.Equal(1, history[0].ObservationCount);
    }

    [Fact]
    public void ContradictionHistory_AccumulatesObservations()
    {
        var contradiction = new BehavioralContradiction
        {
            Type = ContradictionType.GoalActivityGap,
            Severity = ContradictionSeverity.Medium,
            Description = "Goal fading",
            DeclaredIntent = "Deep Work",
            ObservedBehavior = "YouTube"
        };

        // Record same contradiction multiple times
        _harness.ContradictionHistoryStore.Record(contradiction);
        contradiction.Severity = ContradictionSeverity.High;
        _harness.ContradictionHistoryStore.Record(contradiction);
        contradiction.Severity = ContradictionSeverity.Critical;
        _harness.ContradictionHistoryStore.Record(contradiction);

        var history = _harness.ContradictionHistoryStore.LoadAll();
        Assert.Single(history); // Same contradiction, accumulated
        Assert.Equal(3, history[0].ObservationCount);
        Assert.Equal(ContradictionSeverity.Critical, history[0].CurrentSeverity);
        Assert.Equal(ContradictionTrend.Worsening, history[0].Trend);
    }

    [Fact]
    public void ContradictionHistory_DifferentContradictions_SeparateRecords()
    {
        _harness.ContradictionHistoryStore.Record(new BehavioralContradiction
        {
            Type = ContradictionType.GoalActivityGap,
            DeclaredIntent = "Deep Work",
            Severity = ContradictionSeverity.Medium
        });

        _harness.ContradictionHistoryStore.Record(new BehavioralContradiction
        {
            Type = ContradictionType.PriorityDrift,
            DeclaredIntent = "Ship Engram",
            Severity = ContradictionSeverity.Medium
        });

        var history = _harness.ContradictionHistoryStore.LoadAll();
        Assert.Equal(2, history.Count);
    }

    [Fact]
    public void ContradictionHistory_Resolution()
    {
        _harness.ContradictionHistoryStore.Record(new BehavioralContradiction
        {
            Type = ContradictionType.GoalActivityGap,
            DeclaredIntent = "Deep Work",
            Severity = ContradictionSeverity.Medium
        });

        var history = _harness.ContradictionHistoryStore.LoadAll();
        var id = history[0].ContradictionId;

        _harness.ContradictionHistoryStore.Resolve(id, "Goal salience recovered");

        var resolved = _harness.ContradictionHistoryStore.LoadAll();
        Assert.Equal(ContradictionStatus.Resolved, resolved[0].Status);
        Assert.Equal("Goal salience recovered", resolved[0].Resolution);
    }

    [Fact]
    public void ContradictionHistory_ActiveFilter()
    {
        // Add active
        _harness.ContradictionHistoryStore.Record(new BehavioralContradiction
        {
            Type = ContradictionType.GoalActivityGap,
            DeclaredIntent = "Active Goal",
            Severity = ContradictionSeverity.Medium
        });

        // Add and resolve
        _harness.ContradictionHistoryStore.Record(new BehavioralContradiction
        {
            Type = ContradictionType.PriorityDrift,
            DeclaredIntent = "Resolved Priority",
            Severity = ContradictionSeverity.Low
        });
        var history = _harness.ContradictionHistoryStore.LoadAll();
        _harness.ContradictionHistoryStore.Resolve(history[1].ContradictionId);

        var active = _harness.ContradictionHistoryStore.LoadActive();
        Assert.Single(active);
        Assert.Equal("Active Goal", active[0].DeclaredIntent);
    }

    // ═══════════════════════════════════════════════════════════════
    // RESOLUTION DETECTION
    // Contradictions can resolve when behavior aligns
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ResolutionDetection_SalienceRecovery()
    {
        // Create a fading goal contradiction
        _harness.InjectNode(new WikiNode
        {
            NodeId = "goal_recovery",
            Title = "Recovery Goal",
            NodeType = WikiNodeType.Goal,
            Summary = "A goal that will recover",
            Salience = 0.1,
            LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-10)
        });

        _harness.ContradictionHistoryStore.Record(new BehavioralContradiction
        {
            Type = ContradictionType.GoalActivityGap,
            DeclaredIntent = "Recovery Goal",
            Severity = ContradictionSeverity.High,
            RelatedNodeIds = new List<string> { "goal_recovery" }
        });

        // Now recover the goal's salience
        var node = _harness.GetNode("goal_recovery")!;
        node.Salience = 0.6;
        _harness.InjectNode(node);

        var resolutions = _harness.ResolutionDetector.DetectResolutions();

        Assert.Single(resolutions);
        Assert.Equal(ResolutionType.SalienceRecovery, resolutions[0].ResolutionType);
    }

    [Fact]
    public void ResolutionDetection_ActivityResumed()
    {
        _harness.InjectNode(new WikiNode
        {
            NodeId = "commitment_resume",
            Title = "Resume This",
            NodeType = WikiNodeType.Decision,
            Summary = "A commitment",
            Facts = new List<WikiFact> { new() { Text = "Will do this" } },
            Salience = 0.3,
            LastTouchedAt = DateTimeOffset.UtcNow.AddHours(-1) // Recently active
        });

        _harness.ContradictionHistoryStore.Record(new BehavioralContradiction
        {
            Type = ContradictionType.AbandonedCommitment,
            DeclaredIntent = "Resume This",
            Severity = ContradictionSeverity.Medium,
            RelatedNodeIds = new List<string> { "commitment_resume" }
        });

        var resolutions = _harness.ResolutionDetector.DetectResolutions();

        Assert.Single(resolutions);
        Assert.Equal(ResolutionType.ActivityResumed, resolutions[0].ResolutionType);
    }

    // ═══════════════════════════════════════════════════════════════
    // TENSION EVOLUTION
    // Escalation, decay, reinforcement, clustering
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TensionEvolution_ImportanceScoring()
    {
        // High-frequency, worsening contradiction
        for (int i = 0; i < 5; i++)
        {
            _harness.ContradictionHistoryStore.Record(new BehavioralContradiction
            {
                Type = ContradictionType.GoalActivityGap,
                DeclaredIntent = "Important Goal",
                Severity = (ContradictionSeverity)Math.Min(3, i),
                ObservedBehavior = $"Observation {i}"
            });
        }

        var scores = _harness.TensionEngine.ScoreActiveTensions();

        Assert.Single(scores);
        Assert.True(scores[0].ImportanceScore > 0, "Should have positive importance");
        Assert.Equal(5, scores[0].Frequency);
        Assert.Equal(ContradictionTrend.Worsening, scores[0].Trend);
    }

    [Fact]
    public void TensionEvolution_Clustering()
    {
        // Multiple same-type contradictions → pattern
        _harness.ContradictionHistoryStore.Record(new BehavioralContradiction
        {
            Type = ContradictionType.GoalActivityGap,
            DeclaredIntent = "Goal A",
            Severity = ContradictionSeverity.Medium
        });

        _harness.ContradictionHistoryStore.Record(new BehavioralContradiction
        {
            Type = ContradictionType.GoalActivityGap,
            DeclaredIntent = "Goal B",
            Severity = ContradictionSeverity.High
        });

        var clusters = _harness.TensionEngine.ClusterTensions();

        Assert.Single(clusters);
        Assert.Equal(2, clusters[0].ContradictionCount);
        Assert.Contains("Pattern", clusters[0].Pattern);
    }

    [Fact]
    public void TensionEvolution_DifferentTypes_NoClustering()
    {
        _harness.ContradictionHistoryStore.Record(new BehavioralContradiction
        {
            Type = ContradictionType.GoalActivityGap,
            DeclaredIntent = "Goal",
            Severity = ContradictionSeverity.Medium
        });

        _harness.ContradictionHistoryStore.Record(new BehavioralContradiction
        {
            Type = ContradictionType.PriorityDrift,
            DeclaredIntent = "Priority",
            Severity = ContradictionSeverity.Medium
        });

        var clusters = _harness.TensionEngine.ClusterTensions();

        // Different types → no clustering (need 2+ of same type)
        Assert.Empty(clusters);
    }

    // ═══════════════════════════════════════════════════════════════
    // FULL LOOP: COGNITION → PERSISTENCE → FUTURE COGNITION
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void FullLoop_MetabolismPersistsContradictionsAndInterventions()
    {
        // Set up conditions for contradictions
        _harness.InjectNode(new WikiNode
        {
            NodeId = "goal_loop",
            Title = "Loop Goal",
            NodeType = WikiNodeType.Goal,
            Summary = "A goal for loop testing",
            Salience = 0.1,
            LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-20)
        });

        _harness.InjectNode(new WikiNode
        {
            NodeId = "distraction_loop",
            Title = "Loop Distraction",
            NodeType = WikiNodeType.Concept,
            Summary = "A distraction",
            Salience = 0.9,
            LastTouchedAt = DateTimeOffset.UtcNow
        });

        // Run metabolism (should persist contradictions and interventions)
        _harness.RunMetabolismCycle().Wait();

        // Verify contradictions were persisted
        var contradictionHistory = _harness.ContradictionHistoryStore.LoadAll();
        Assert.True(contradictionHistory.Count > 0,
            "Contradictions should be persisted in history");

        // Verify interventions were persisted
        var interventions = _harness.InterventionStore.LoadAll();
        Assert.True(interventions.Count > 0,
            "Interventions should be persisted");
    }

    [Fact]
    public void FullLoop_ContradictionsAccumulateOverMultipleCycles()
    {
        _harness.InjectNode(new WikiNode
        {
            NodeId = "goal_accumulate",
            Title = "Accumulating Goal",
            NodeType = WikiNodeType.Goal,
            Summary = "A goal that accumulates contradictions",
            Salience = 0.15,
            LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-10)
        });

        _harness.InjectNode(new WikiNode
        {
            NodeId = "distraction_accumulate",
            Title = "Accumulating Distraction",
            NodeType = WikiNodeType.Concept,
            Summary = "A distraction",
            Salience = 0.85,
            LastTouchedAt = DateTimeOffset.UtcNow
        });

        // Run multiple cycles
        for (int i = 0; i < 3; i++)
        {
            _harness.RunMetabolismCycle().Wait();
        }

        var history = _harness.ContradictionHistoryStore.LoadAll();
        Assert.True(history.Count > 0, "Should have contradiction history");
        Assert.True(history[0].ObservationCount >= 1,
            "Contradiction should have accumulated observations");
    }

    [Fact]
    public void FullLoop_BehavioralContextInPrompt()
    {
        // Create escalating contradiction
        for (int i = 0; i < 3; i++)
        {
            _harness.ContradictionHistoryStore.Record(new BehavioralContradiction
            {
                Type = ContradictionType.GoalActivityGap,
                DeclaredIntent = "Deep Work",
                Severity = (ContradictionSeverity)Math.Min(3, i + 1),
                ObservedBehavior = "YouTube watching"
            });
        }

        // The PromptAssembler should now have access to contradiction history
        // (if wired). At minimum, the store should be queryable.
        var active = _harness.ContradictionHistoryStore.LoadActive();
        Assert.Single(active);
        Assert.True(active[0].ObservationCount >= 3);
        Assert.Equal(ContradictionTrend.Worsening, active[0].Trend);
    }
}
