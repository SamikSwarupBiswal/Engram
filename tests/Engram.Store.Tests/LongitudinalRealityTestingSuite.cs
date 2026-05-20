using Engram.Store.Metabolism;
using Engram.Store.Perception;
using Engram.Store.Wiki;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Phase 8: Longitudinal Human Reality Testing — test suite.
///
/// Tests:
/// - Narrative Drift Auditor (alignment scoring, trend detection)
/// - Intervention Fatigue Tracker (dismissal rates, fatigue scoring)
/// - Memory Pollution Detector (stale nodes, orphans, overrepresentation)
/// - Semantic Compressor (prune candidates, merge candidates, archival)
/// - Ambiguity Tolerance Engine (ambiguity detection, "I don't know" infrastructure)
/// - Integration (harness wiring, telemetry, full pipeline)
/// </summary>
public class LongitudinalRealityTestingSuite : IDisposable
{
    private readonly CognitiveReplayHarness _harness;

    public LongitudinalRealityTestingSuite()
    {
        _harness = new CognitiveReplayHarness();
    }

    public void Dispose()
    {
        _harness.Dispose();
    }

    // ═══════════════════════════════════════════════════════════
    // 8A: NARRATIVE DRIFT AUDITOR
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void DriftAuditor_RunAudit_EmptyGraph()
    {
        var auditor = _harness.DriftAuditor;
        var result = auditor.RunAudit();

        Assert.NotNull(result);
        Assert.Equal(0, result.NodeCount);
        Assert.True(result.OverallAlignment >= 0);
    }

    [Fact]
    public void DriftAuditor_RunAudit_WithGoals()
    {
        var auditor = _harness.DriftAuditor;

        // Seed some goals
        _harness.InjectNode(new WikiNode
        {
            NodeId = "goal1",
            Title = "Ship Engram",
            NodeType = WikiNodeType.Goal,
            Salience = 0.8,
            LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });

        var result = auditor.RunAudit();
        Assert.Equal(1, result.NodeCount);
        Assert.NotNull(result.GoalAlignment);
    }

    [Fact]
    public void DriftAuditor_AuditHistory()
    {
        var auditor = _harness.DriftAuditor;
        auditor.RunAudit();
        auditor.RunAudit();

        var history = auditor.GetAuditHistory();
        Assert.Equal(2, history.Count);
    }

    [Fact]
    public void DriftAuditor_Trend_InsufficientData()
    {
        var auditor = _harness.DriftAuditor;
        auditor.RunAudit();

        var trend = auditor.GetTrend();
        Assert.Equal(TrendDirection.InsufficientData, trend.Direction);
    }

    [Fact]
    public void DriftAuditor_Trend_Stable()
    {
        var auditor = _harness.DriftAuditor;
        auditor.RunAudit();
        auditor.RunAudit();

        var trend = auditor.GetTrend();
        Assert.Equal(TrendDirection.Stable, trend.Direction);
    }

    // ═══════════════════════════════════════════════════════════
    // 8B: INTERVENTION FATIGUE TRACKER
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void FatigueTracker_EmptyReport()
    {
        var tracker = _harness.FatigueTracker;
        var report = tracker.GenerateReport();

        Assert.Equal(0, report.TotalInterventions);
        Assert.False(report.IsFatigued);
    }

    [Fact]
    public void FatigueTracker_TrackDismissals()
    {
        var tracker = _harness.FatigueTracker;

        tracker.RecordInterventionPresented("i1", "drift");
        tracker.RecordInterventionPresented("i2", "drift");
        tracker.RecordInterventionDismissed("i1");
        tracker.RecordInterventionDismissed("i2");

        var report = tracker.GenerateReport();
        Assert.Equal(2, report.TotalInterventions);
        Assert.Equal(2, report.Dismissed);
        Assert.Equal(1.0, report.DismissalRate);
    }

    [Fact]
    public void FatigueTracker_TrackActions()
    {
        var tracker = _harness.FatigueTracker;

        tracker.RecordInterventionPresented("i1", "goal");
        tracker.RecordInterventionActed("i1");

        var report = tracker.GenerateReport();
        Assert.Equal(1, report.Acted);
        Assert.Equal(1.0, report.ActionRate);
    }

    [Fact]
    public void FatigueTracker_FatigueDetection()
    {
        var tracker = _harness.FatigueTracker;

        // High dismissal + ignore rate = fatigued
        for (int i = 0; i < 10; i++)
        {
            tracker.RecordInterventionPresented($"i{i}", "drift");
            if (i < 6) tracker.RecordInterventionDismissed($"i{i}");
            else tracker.RecordInterventionIgnored($"i{i}");
        }

        var report = tracker.GenerateReport();
        Assert.True(report.IsFatigued);
        Assert.True(report.FatigueScore >= 0.6);
    }

    [Fact]
    public void FatigueTracker_ShouldReduceFrequency()
    {
        var tracker = _harness.FatigueTracker;

        for (int i = 0; i < 10; i++)
        {
            tracker.RecordInterventionPresented($"i{i}", "drift");
            tracker.RecordInterventionDismissed($"i{i}");
        }

        Assert.True(tracker.ShouldReduceFrequency());
    }

    [Fact]
    public void FatigueTracker_CategoryBreakdown()
    {
        var tracker = _harness.FatigueTracker;

        tracker.RecordInterventionPresented("i1", "drift");
        tracker.RecordInterventionPresented("i2", "goal");
        tracker.RecordInterventionDismissed("i1");

        var report = tracker.GenerateReport();
        Assert.Equal(2, report.CategoryBreakdown.Count);
        Assert.True(report.CategoryBreakdown.ContainsKey("drift"));
        Assert.True(report.CategoryBreakdown.ContainsKey("goal"));
    }

    // ═══════════════════════════════════════════════════════════
    // 8C: MEMORY POLLUTION DETECTOR
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void PollutionDetector_EmptyGraph()
    {
        var detector = _harness.PollutionDetector;
        var report = detector.Analyze();

        Assert.Equal(0, report.TotalNodes);
        Assert.Equal(0, report.PollutionScore);
    }

    [Fact]
    public void PollutionDetector_DetectsStaleNodes()
    {
        var detector = _harness.PollutionDetector;

        _harness.InjectNode(new WikiNode
        {
            NodeId = "stale1",
            Title = "Old Project",
            NodeType = WikiNodeType.Concept,
            Salience = 0.1,
            LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-60)
        });

        var report = detector.Analyze();
        Assert.Contains("Old Project", report.StaleNodes);
    }

    [Fact]
    public void PollutionDetector_DetectsOrphanedNodes()
    {
        var detector = _harness.PollutionDetector;

        _harness.InjectNode(new WikiNode
        {
            NodeId = "orphan1",
            Title = "Isolated Fact",
            NodeType = WikiNodeType.Concept,
            Salience = 0.5,
            LastTouchedAt = DateTimeOffset.UtcNow
        });

        var report = detector.Analyze();
        Assert.Contains("Isolated Fact", report.OrphanedNodes);
    }

    [Fact]
    public void PollutionDetector_GetPruneCandidates()
    {
        var detector = _harness.PollutionDetector;

        // Stale + low salience + no relations = prune candidate
        _harness.InjectNode(new WikiNode
        {
            NodeId = "prune1",
            Title = "Dead Node",
            NodeType = WikiNodeType.Concept,
            Salience = 0.01,
            LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-90)
        });

        var candidates = detector.GetPruneCandidates();
        Assert.Single(candidates);
    }

    // ═══════════════════════════════════════════════════════════
    // 8D: SEMANTIC COMPRESSOR
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Compressor_Analyze_EmptyGraph()
    {
        var compressor = _harness.Compressor;
        var report = compressor.AnalyzeForCompression();

        Assert.Equal(0, report.TotalNodes);
        Assert.Empty(report.PruneCandidates);
        Assert.Empty(report.MergeCandidates);
    }

    [Fact]
    public void Compressor_FindsPruneCandidates()
    {
        var compressor = _harness.Compressor;

        _harness.InjectNode(new WikiNode
        {
            NodeId = "prune1",
            Title = "Old Concept",
            NodeType = WikiNodeType.Concept,
            Salience = 0.01,
            Facts = new List<WikiFact> { new() { Text = "minor fact" } },
            LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-90)
        });

        var report = compressor.AnalyzeForCompression();
        Assert.Contains("Old Concept", report.PruneCandidates);
    }

    [Fact]
    public void Compressor_FindsMergeCandidates()
    {
        var compressor = _harness.Compressor;

        _harness.InjectNode(new WikiNode
        {
            NodeId = "merge1",
            Title = "Machine Learning Models",
            NodeType = WikiNodeType.Concept,
            Salience = 0.5,
            LastTouchedAt = DateTimeOffset.UtcNow
        });
        _harness.InjectNode(new WikiNode
        {
            NodeId = "merge2",
            Title = "Machine Learning Models",
            NodeType = WikiNodeType.Concept,
            Salience = 0.4,
            LastTouchedAt = DateTimeOffset.UtcNow
        });

        var report = compressor.AnalyzeForCompression();
        Assert.True(report.MergeCandidates.Count > 0);
    }

    [Fact]
    public void Compressor_FindsArchiveCandidates()
    {
        var compressor = _harness.Compressor;

        _harness.InjectNode(new WikiNode
        {
            NodeId = "archive1",
            Title = "Old Project",
            NodeType = WikiNodeType.Concept,
            Salience = 0.1,
            LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-30)
        });

        var report = compressor.AnalyzeForCompression();
        Assert.Contains("Old Project", report.ArchiveCandidates);
    }

    // ═══════════════════════════════════════════════════════════
    // 8E: AMBIGUITY TOLERANCE ENGINE
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void AmbiguityEngine_NoInterpretations_CompleteAmbiguity()
    {
        var engine = _harness.AmbiguityEngine;
        var assessment = engine.Evaluate("2h on YouTube", new List<CompetingInterpretation>());

        Assert.True(assessment.IsAmbiguous);
        Assert.Equal(AmbiguityLevel.Complete, assessment.AmbiguityLevel);
        Assert.Equal(AmbiguityAction.SayUnknown, assessment.RecommendedAction);
    }

    [Fact]
    public void AmbiguityEngine_ClearInterpretation_NotAmbiguous()
    {
        var engine = _harness.AmbiguityEngine;
        var interpretations = new List<CompetingInterpretation>
        {
            new() { Narrative = "deep_work", Plausibility = 0.95, Evidence = "Coding in VSCode for 2 hours" }
        };

        var assessment = engine.Evaluate("Coding session", interpretations);
        Assert.False(assessment.IsAmbiguous);
        Assert.Equal(AmbiguityLevel.None, assessment.AmbiguityLevel);
    }

    [Fact]
    public void AmbiguityEngine_CloseCompetition_HighAmbiguity()
    {
        var engine = _harness.AmbiguityEngine;
        var interpretations = new List<CompetingInterpretation>
        {
            new() { Narrative = "research", Plausibility = 0.45, Evidence = "Reading documentation" },
            new() { Narrative = "procrastination", Plausibility = 0.40, Evidence = "Browsing random sites" },
            new() { Narrative = "decompression", Plausibility = 0.35, Evidence = "After intense work session" }
        };

        var assessment = engine.Evaluate("Browsing for 30 minutes", interpretations);
        Assert.True(assessment.IsAmbiguous);
        Assert.True(assessment.AmbiguityLevel >= AmbiguityLevel.Moderate);
    }

    [Fact]
    public void AmbiguityEngine_NegativeDefaultBias_Detected()
    {
        var engine = _harness.AmbiguityEngine;
        var interpretations = new List<CompetingInterpretation>
        {
            new() { Narrative = "procrastination", Plausibility = 0.5, Evidence = "On YouTube" },
            new() { Narrative = "research", Plausibility = 0.4, Evidence = "Tutorial videos" },
            new() { Narrative = "decompression", Plausibility = 0.3, Evidence = "After 3h coding" }
        };

        var assessment = engine.Evaluate("YouTube for 30 min", interpretations);
        Assert.Contains(assessment.Signals, s => s.Type == AmbiguitySignalType.NegativeDefaultBias);
    }

    [Fact]
    public void AmbiguityEngine_FormatAmbiguousResponse()
    {
        var engine = _harness.AmbiguityEngine;
        var interpretations = new List<CompetingInterpretation>
        {
            new() { Narrative = "research", Plausibility = 0.45 },
            new() { Narrative = "procrastination", Plausibility = 0.40 },
            new() { Narrative = "decompression", Plausibility = 0.35 }
        };

        var assessment = engine.Evaluate("YouTube browsing", interpretations);
        var response = engine.FormatAmbiguousResponse(assessment);

        Assert.Contains("My best guess is", response);
    }

    [Fact]
    public void AmbiguityEngine_FormatClearResponse()
    {
        var engine = _harness.AmbiguityEngine;
        var interpretations = new List<CompetingInterpretation>
        {
            new() { Narrative = "deep work", Plausibility = 0.95 }
        };

        var assessment = engine.Evaluate("Coding", interpretations);
        var response = engine.FormatAmbiguousResponse(assessment);

        Assert.Contains("appears to be", response);
    }

    [Fact]
    public void AmbiguityEngine_Stats()
    {
        var engine = _harness.AmbiguityEngine;

        // One clear, one ambiguous
        engine.Evaluate("Coding", new List<CompetingInterpretation>
        {
            new() { Narrative = "deep_work", Plausibility = 0.95 }
        });
        engine.Evaluate("YouTube", new List<CompetingInterpretation>
        {
            new() { Narrative = "research", Plausibility = 0.4 },
            new() { Narrative = "procrastination", Plausibility = 0.35 }
        });

        var stats = engine.GetStats();
        Assert.Equal(2, stats.TotalEvaluations);
        Assert.Equal(1, stats.AmbiguousCount);
    }

    [Fact]
    public void AmbiguityEngine_OverConfident_Detection()
    {
        var engine = _harness.AmbiguityEngine;

        // 15 clear interpretations, 0 ambiguous = over-confident
        for (int i = 0; i < 15; i++)
        {
            engine.Evaluate($"observation {i}", new List<CompetingInterpretation>
            {
                new() { Narrative = "clear", Plausibility = 0.95 }
            });
        }

        Assert.True(engine.IsOverConfident());
    }

    [Fact]
    public void AmbiguityEngine_NotOverConfident_WhenAmbiguous()
    {
        var engine = _harness.AmbiguityEngine;

        // Mix of clear and ambiguous
        for (int i = 0; i < 10; i++)
        {
            engine.Evaluate($"observation {i}", new List<CompetingInterpretation>
            {
                new() { Narrative = "option_a", Plausibility = 0.35 },
                new() { Narrative = "option_b", Plausibility = 0.30 }
            });
        }

        Assert.False(engine.IsOverConfident());
    }

    // ═══════════════════════════════════════════════════════════
    // INTEGRATION TESTS
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Integration_HarnessHasPhase8Components()
    {
        Assert.NotNull(_harness.DriftAuditor);
        Assert.NotNull(_harness.FatigueTracker);
        Assert.NotNull(_harness.PollutionDetector);
        Assert.NotNull(_harness.Compressor);
        Assert.NotNull(_harness.AmbiguityEngine);
    }

    [Fact]
    public void Integration_Telemetry_TracksPhase8()
    {
        var telemetry = _harness.Telemetry;

        telemetry.RecordDriftAudit(0.75);
        telemetry.RecordInterventionDismissed();
        telemetry.RecordInterventionActedOn();
        telemetry.RecordAmbiguityEvaluation(isAmbiguous: true);
        telemetry.RecordUnknownClassification();

        var metrics = telemetry.GetPhase8Metrics();
        Assert.Equal(1, metrics.DriftAuditsRun);
        Assert.Equal(0.75, metrics.LastDriftAlignment);
        Assert.Equal(1, metrics.InterventionsDismissed);
        Assert.Equal(1, metrics.InterventionsActedOn);
        Assert.Equal(1, metrics.AmbiguousObservations);
        Assert.Equal(1, metrics.UnknownClassifications);
    }

    [Fact]
    public void Integration_DiagnosticsSnapshot_ContainsPhase8()
    {
        var snapshot = _harness.Telemetry.GetDiagnosticsSnapshot();
        Assert.NotNull(snapshot.Phase8);
    }

    [Fact]
    public void Integration_FullPipeline_AuditFatigueAmbiguity()
    {
        // 1. Seed the graph
        _harness.InjectNode(new WikiNode
        {
            NodeId = "g1", Title = "Ship Product", NodeType = WikiNodeType.Goal,
            Salience = 0.8, LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });

        // 2. Run drift audit
        var audit = _harness.DriftAuditor.RunAudit();
        Assert.NotNull(audit);
        Assert.Equal(1, audit.NodeCount);

        // 3. Track fatigue
        _harness.FatigueTracker.RecordInterventionPresented("i1", "drift");
        _harness.FatigueTracker.RecordInterventionDismissed("i1");
        var fatigue = _harness.FatigueTracker.GenerateReport();
        Assert.Equal(1, fatigue.Dismissed);

        // 4. Check pollution
        var pollution = _harness.PollutionDetector.Analyze();
        Assert.True(pollution.TotalNodes >= 1);

        // 5. Evaluate ambiguity
        var assessment = _harness.AmbiguityEngine.Evaluate(
            "2h on YouTube",
            new List<CompetingInterpretation>
            {
                new() { Narrative = "research", Plausibility = 0.4 },
                new() { Narrative = "procrastination", Plausibility = 0.35 },
                new() { Narrative = "decompression", Plausibility = 0.25 }
            });
        Assert.True(assessment.IsAmbiguous);
    }
}
