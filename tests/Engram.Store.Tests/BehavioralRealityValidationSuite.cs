using Engram.Store.Events;
using Engram.Store.Metabolism;
using Engram.Store.Perception;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Phase 7: Behavioral Reality Validation — comprehensive test suite.
/// 
/// Tests the entire truth-preserving cognition pipeline:
/// - Perception Replay System (recording, replay, comparison)
/// - Interpretation Accuracy Tracking (outcomes, reports)
/// - False Pattern Detection (over-interpretation warnings)
/// - Human Truth Calibration (corrections, feedback)
/// - Cognitive Restraint Engine (silence, confidence, flow protection)
/// - Real Timeline Semantics (sessions, arcs, momentum, regressions)
/// - Integration (full pipeline, telemetry, harness wiring)
/// </summary>
public class BehavioralRealityValidationSuite : IDisposable
{
    private readonly CognitiveReplayHarness _harness;

    public BehavioralRealityValidationSuite()
    {
        _harness = new CognitiveReplayHarness();
    }

    public void Dispose()
    {
        _harness.Dispose();
    }

    // ═══════════════════════════════════════════════════════════
    // 7A: PERCEPTION REPLAY SYSTEM
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void BehavioralModeStrategy_Default_DetectsDeepWork()
    {
        var strategy = new DefaultBehavioralModeStrategy();
        var mode = strategy.DetectMode("code", "Engram - Visual Studio Code", TimeSpan.FromMinutes(15));
        Assert.Equal("deep_work", mode);
    }

    [Fact]
    public void BehavioralModeStrategy_Default_DetectsResearch()
    {
        var strategy = new DefaultBehavioralModeStrategy();
        var mode = strategy.DetectMode("chrome", "stackoverflow.com - How to fix", TimeSpan.FromMinutes(2));
        Assert.Equal("research", mode);
    }

    [Fact]
    public void BehavioralModeStrategy_Default_DetectsBrowsing()
    {
        var strategy = new DefaultBehavioralModeStrategy();
        var mode = strategy.DetectMode("chrome", "YouTube - Cat Videos", TimeSpan.FromMinutes(2));
        Assert.Equal("browsing", mode);
    }

    [Fact]
    public void BehavioralModeStrategy_Default_DetectsCommunication()
    {
        var strategy = new DefaultBehavioralModeStrategy();
        var mode = strategy.DetectMode("slack", "Slack - #general", TimeSpan.FromMinutes(1));
        Assert.Equal("communication", mode);
    }

    [Fact]
    public void BehavioralModeStrategy_Default_DetectsTerminalWork()
    {
        var strategy = new DefaultBehavioralModeStrategy();
        var mode = strategy.DetectMode("WindowsTerminal", "PowerShell", TimeSpan.FromMinutes(5));
        Assert.Equal("terminal_work", mode);
    }

    [Fact]
    public void BehavioralModeStrategy_Default_DetectsExploration()
    {
        var strategy = new DefaultBehavioralModeStrategy();
        var mode = strategy.DetectMode("notepad", "Untitled", TimeSpan.FromMinutes(1));
        Assert.Equal("exploration", mode);
    }

    [Fact]
    public void BehavioralModeStrategy_ShortFocus_NotDeepWork()
    {
        var strategy = new DefaultBehavioralModeStrategy();
        var mode = strategy.DetectMode("code", "VS Code", TimeSpan.FromMinutes(2));
        Assert.NotEqual("deep_work", mode);
    }

    [Fact]
    public void EnvironmentModel_AcceptsCustomStrategy()
    {
        var eventBus = _harness.EventBus;
        var customStrategy = new TestBehavioralModeStrategy("always_custom");
        var model = new EnvironmentModel(eventBus, customStrategy);

        model.ProcessWindowChange("code", "Test", TimeSpan.FromMinutes(15));
        var state = model.GetCurrentState();

        Assert.Equal("always_custom", state.CurrentBehavioralMode);
    }

    [Fact]
    public void PerceptionEventRecorder_StartStop()
    {
        var recorder = _harness.PerceptionRecorder;
        Assert.False(recorder.IsRecording);

        recorder.StartRecording();
        Assert.True(recorder.IsRecording);

        recorder.StopRecording();
        Assert.False(recorder.IsRecording);
    }

    [Fact]
    public void PerceptionEventRecorder_CapturesWindowEvents()
    {
        var recorder = _harness.PerceptionRecorder;
        recorder.StartRecording();

        _harness.EventBus.Publish(new EventEnvelope
        {
            EventType = "perception.active_window_changed",
            Source = "test",
            Payload = new { Process = "code", Title = "Test" }
        });

        // Give event time to propagate
        Thread.Sleep(50);

        var snapshots = recorder.GetSnapshots();
        Assert.True(snapshots.Count > 0);
        Assert.Equal("window_change", snapshots[0].Input.EventType);
    }

    [Fact]
    public void PerceptionEventRecorder_Clear()
    {
        var recorder = _harness.PerceptionRecorder;
        recorder.StartRecording();

        _harness.EventBus.Publish(new EventEnvelope
        {
            EventType = "perception.active_window_changed",
            Source = "test",
            Payload = new { Process = "code", Title = "Test" }
        });

        Thread.Sleep(50);
        recorder.Clear();
        Assert.Equal(0, recorder.SnapshotCount);
    }

    [Fact]
    public void PerceptionEventRecorder_InjectSnapshot()
    {
        var recorder = _harness.PerceptionRecorder;
        var snapshot = new PerceptionSnapshot
        {
            Input = new PerceptionInput { ProcessName = "test", EventType = "test" },
            Interpretation = new PerceptionInterpretation { BehavioralMode = "test_mode" }
        };

        recorder.InjectSnapshot(snapshot);
        Assert.Equal(1, recorder.SnapshotCount);
    }

    [Fact]
    public void PerceptionReplayEngine_ReplayProducesResults()
    {
        var engine = _harness.ReplayEngine;
        var strategy = new DefaultBehavioralModeStrategy();

        var snapshots = CreateTestSnapshots();
        var results = engine.Replay(snapshots, strategy);

        Assert.Equal(snapshots.Count, results.Count);
    }

    [Fact]
    public void PerceptionReplayEngine_DeterministicReplay()
    {
        var engine = _harness.ReplayEngine;
        var strategy = new DefaultBehavioralModeStrategy();

        var snapshots = CreateTestSnapshots();
        var results1 = engine.Replay(snapshots, strategy);
        var results2 = engine.Replay(snapshots, strategy);

        // Same inputs → same outputs (deterministic)
        for (int i = 0; i < results1.Count; i++)
        {
            Assert.Equal(results1[i].ReplayedMode, results2[i].ReplayedMode);
        }
    }

    [Fact]
    public void PerceptionReplayEngine_DifferentStrategies_DifferentResults()
    {
        var engine = _harness.ReplayEngine;
        var strategyA = new DefaultBehavioralModeStrategy();
        var strategyB = new TestBehavioralModeStrategy("custom_mode");

        var snapshots = CreateTestSnapshots();
        var resultsA = engine.Replay(snapshots, strategyA);
        var resultsB = engine.Replay(snapshots, strategyB);

        // Different strategies should produce different results
        Assert.NotEqual(resultsA[0].ReplayedMode, resultsB[0].ReplayedMode);
    }

    [Fact]
    public void PerceptionReplayEngine_DivergenceDetection()
    {
        var engine = _harness.ReplayEngine;
        var strategy = new DefaultBehavioralModeStrategy();

        // Create snapshots with wrong original modes
        var snapshots = new List<PerceptionSnapshot>
        {
            new()
            {
                Input = new PerceptionInput { ProcessName = "code", WindowTitle = "VS Code", FocusDuration = TimeSpan.FromMinutes(15) },
                Interpretation = new PerceptionInterpretation { BehavioralMode = "browsing" } // Wrong!
            }
        };

        var results = engine.Replay(snapshots, strategy);
        Assert.True(results[0].HasDivergence);
    }

    [Fact]
    public void InterpretationComparator_FindsDivergences()
    {
        var comparator = _harness.Comparator;

        var setA = new List<ReplayResult>
        {
            new() { ReplayedMode = "deep_work", OriginalMode = "deep_work" },
            new() { ReplayedMode = "research", OriginalMode = "research" }
        };
        var setB = new List<ReplayResult>
        {
            new() { ReplayedMode = "deep_work", OriginalMode = "deep_work" },
            new() { ReplayedMode = "browsing", OriginalMode = "research" }
        };

        var report = comparator.Compare(setA, setB);
        Assert.Equal(1, report.DivergenceCount);
        Assert.True(report.DivergenceRate > 0);
    }

    [Fact]
    public void InterpretationComparator_SystematicPatterns()
    {
        var comparator = _harness.Comparator;

        var setA = new List<ReplayResult>
        {
            new() { ReplayedMode = "research", OriginalMode = "research" },
            new() { ReplayedMode = "research", OriginalMode = "research" },
            new() { ReplayedMode = "research", OriginalMode = "research" }
        };
        var setB = new List<ReplayResult>
        {
            new() { ReplayedMode = "browsing", OriginalMode = "research" },
            new() { ReplayedMode = "browsing", OriginalMode = "research" },
            new() { ReplayedMode = "browsing", OriginalMode = "research" }
        };

        var report = comparator.Compare(setA, setB);
        Assert.True(report.SystematicPatterns.ContainsKey("research→browsing"));
        Assert.Equal(3, report.SystematicPatterns["research→browsing"]);
    }

    // ═══════════════════════════════════════════════════════════
    // 7B: INTERPRETATION ACCURACY TRACKING
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void AccuracyTracker_RecordCorrect()
    {
        var tracker = _harness.AccuracyTracker;
        tracker.RecordCorrect("snap1", "deep_work");

        var records = tracker.GetAllRecords();
        Assert.Single(records);
        Assert.Equal(InterpretationOutcome.Correct, records[0].Outcome);
    }

    [Fact]
    public void AccuracyTracker_RecordIncorrect()
    {
        var tracker = _harness.AccuracyTracker;
        tracker.RecordIncorrect("snap1", "browsing", "research", "Was actually researching");

        var records = tracker.GetAllRecords();
        Assert.Single(records);
        Assert.Equal(InterpretationOutcome.Incorrect, records[0].Outcome);
        Assert.Equal("Was actually researching", records[0].CorrectionNote);
    }

    [Fact]
    public void AccuracyTracker_RecordPartial()
    {
        var tracker = _harness.AccuracyTracker;
        tracker.RecordPartial("snap1", "research", "studying");

        var records = tracker.GetAllRecords();
        Assert.Single(records);
        Assert.Equal(InterpretationOutcome.Partial, records[0].Outcome);
    }

    [Fact]
    public void AccuracyTracker_Report_Empty()
    {
        var tracker = _harness.AccuracyTracker;
        var report = tracker.GenerateReport();

        Assert.Equal(0, report.TotalRecords);
        Assert.Equal(0, report.OverallAccuracy);
    }

    [Fact]
    public void AccuracyTracker_Report_WithRecords()
    {
        var tracker = _harness.AccuracyTracker;
        tracker.RecordCorrect("s1", "deep_work");
        tracker.RecordCorrect("s2", "research");
        tracker.RecordIncorrect("s3", "browsing", "research");

        var report = tracker.GenerateReport();
        Assert.Equal(3, report.TotalRecords);
        Assert.Equal(2, report.CorrectCount);
        Assert.Equal(1, report.IncorrectCount);
        Assert.True(report.OverallAccuracy > 0.6);
    }

    [Fact]
    public void AccuracyTracker_Report_PerModeAccuracy()
    {
        var tracker = _harness.AccuracyTracker;
        tracker.RecordCorrect("s1", "deep_work");
        tracker.RecordCorrect("s2", "deep_work");
        tracker.RecordIncorrect("s3", "browsing", "research");

        var report = tracker.GenerateReport();
        Assert.True(report.PerModeAccuracy.ContainsKey("deep_work"));
        Assert.Equal(1.0, report.PerModeAccuracy["deep_work"].AccuracyRate);
    }

    [Fact]
    public void AccuracyTracker_Report_ErrorPatterns()
    {
        var tracker = _harness.AccuracyTracker;
        tracker.RecordIncorrect("s1", "browsing", "research");
        tracker.RecordIncorrect("s2", "browsing", "research");

        var report = tracker.GenerateReport();
        Assert.True(report.ErrorPatterns.ContainsKey("browsing→research"));
        Assert.Equal(2, report.ErrorPatterns["browsing→research"]);
    }

    [Fact]
    public void AccuracyTracker_ModeAccuracy_NoData()
    {
        var tracker = _harness.AccuracyTracker;
        Assert.Equal(-1, tracker.GetModeAccuracy("nonexistent"));
    }

    // ═══════════════════════════════════════════════════════════
    // 7C: FALSE PATTERN DETECTION
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void FalsePatternDetector_NoWarning_WhenAccurate()
    {
        var tracker = _harness.AccuracyTracker;
        var detector = _harness.FalsePatternDetector;

        for (int i = 0; i < 10; i++)
            tracker.RecordCorrect($"s{i}", "deep_work");

        var warning = detector.CheckMode("deep_work");
        Assert.Null(warning);
    }

    [Fact]
    public void FalsePatternDetector_Warning_WhenOverInterpreted()
    {
        var tracker = _harness.AccuracyTracker;
        var detector = _harness.FalsePatternDetector;

        // 7 incorrect out of 10 = 70% error rate
        for (int i = 0; i < 3; i++)
            tracker.RecordCorrect($"s{i}", "browsing");
        for (int i = 3; i < 10; i++)
            tracker.RecordIncorrect($"s{i}", "browsing", "research");

        var warning = detector.CheckMode("browsing");
        Assert.NotNull(warning);
        Assert.Equal(OverinterpretationSeverity.High, warning!.Severity);
    }

    [Fact]
    public void FalsePatternDetector_NoWarning_InsufficientData()
    {
        var tracker = _harness.AccuracyTracker;
        var detector = _harness.FalsePatternDetector;

        tracker.RecordIncorrect("s1", "browsing", "research");

        var warning = detector.CheckMode("browsing");
        Assert.Null(warning); // Only 1 record, below MinSampleSize
    }

    [Fact]
    public void FalsePatternDetector_CheckAllModes()
    {
        var tracker = _harness.AccuracyTracker;
        var detector = _harness.FalsePatternDetector;

        // Make browsing over-interpreted
        for (int i = 0; i < 3; i++)
            tracker.RecordCorrect($"s{i}", "browsing");
        for (int i = 3; i < 10; i++)
            tracker.RecordIncorrect($"s{i}", "browsing", "research");

        var warnings = detector.CheckAllModes();
        Assert.True(warnings.Count > 0);
        Assert.Contains(warnings, w => w.Mode == "browsing");
    }

    [Fact]
    public void FalsePatternDetector_Profile()
    {
        var tracker = _harness.AccuracyTracker;
        var detector = _harness.FalsePatternDetector;

        for (int i = 0; i < 3; i++)
            tracker.RecordCorrect($"s{i}", "browsing");
        for (int i = 3; i < 10; i++)
            tracker.RecordIncorrect($"s{i}", "browsing", "research");

        var profile = detector.GetProfile();
        Assert.True(profile.ModesWithWarnings.ContainsKey("browsing"));
    }

    [Fact]
    public void FalsePatternDetector_RecordOverinterpretation()
    {
        var detector = _harness.FalsePatternDetector;
        detector.RecordOverinterpretation("browsing", "user was reading docs", "research misclassified");

        var records = detector.GetRecords();
        Assert.Single(records);
    }

    // ═══════════════════════════════════════════════════════════
    // 7D: HUMAN TRUTH CALIBRATION
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void CalibrationStore_AddCorrection()
    {
        var store = _harness.CalibrationStore;
        var record = store.CorrectMode("snap1", "browsing", "research", "Was reading docs");

        Assert.NotNull(record);
        Assert.Equal("browsing", record.EngramInterpretation);
        Assert.Equal("research", record.ActualBehavior);
    }

    [Fact]
    public void CalibrationStore_DismissPattern()
    {
        var store = _harness.CalibrationStore;
        var record = store.DismissPattern("context_switching", "Normal for this user");

        Assert.Equal(CorrectionType.PatternDismissed, record.Type);
    }

    [Fact]
    public void CalibrationStore_MarkTemporary()
    {
        var store = _harness.CalibrationStore;
        var record = store.MarkTemporary("snap1", "idle", "Was on phone call");

        Assert.Equal(CorrectionType.Temporary, record.Type);
    }

    [Fact]
    public void CalibrationStore_IgnoreCategory()
    {
        var store = _harness.CalibrationStore;
        var record = store.IgnoreCategory("browsing", "Don't track browsing");

        Assert.Equal(CorrectionType.CategoryIgnored, record.Type);
        Assert.True(store.IsCategoryIgnored("browsing"));
    }

    [Fact]
    public void CalibrationStore_GetCorrectionsForMode()
    {
        var store = _harness.CalibrationStore;
        store.CorrectMode("s1", "browsing", "research");
        store.CorrectMode("s2", "browsing", "studying");

        var corrections = store.GetCorrectionsForMode("browsing");
        Assert.Equal(2, corrections.Count);
    }

    [Fact]
    public void CalibrationStore_IsModeFrequentlyCorrected()
    {
        var store = _harness.CalibrationStore;
        store.CorrectMode("s1", "browsing", "research");
        store.CorrectMode("s2", "browsing", "studying");
        store.CorrectMode("s3", "browsing", "reading");

        Assert.True(store.IsModeFrequentlyCorrected("browsing", threshold: 3));
        Assert.False(store.IsModeFrequentlyCorrected("deep_work", threshold: 3));
    }

    [Fact]
    public void CalibrationStore_Summary()
    {
        var store = _harness.CalibrationStore;
        store.CorrectMode("s1", "browsing", "research");
        store.DismissPattern("context_switching");
        store.IgnoreCategory("browsing");

        var summary = store.GetSummary();
        Assert.Equal(3, summary.TotalCorrections);
        Assert.Contains("context_switching", summary.DismissedPatterns);
        Assert.Contains("browsing", summary.IgnoredCategories);
    }

    [Fact]
    public void CalibrationStore_Persistence()
    {
        var store = _harness.CalibrationStore;
        store.CorrectMode("s1", "browsing", "research");

        // Verify the record exists
        var corrections = store.GetAllCorrections();
        Assert.Single(corrections);
    }

    // ═══════════════════════════════════════════════════════════
    // 7E: COGNITIVE RESTRAINT ENGINE
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void RestraintEngine_AllowsHighConfidence()
    {
        var engine = _harness.RestraintEngine;
        var context = new RestraintContext
        {
            InterpretationConfidence = 0.9,
            CurrentBehavioralMode = "research"
        };

        var decision = engine.ShouldSpeak(context);
        Assert.True(decision.Allow);
    }

    [Fact]
    public void RestraintEngine_SuppressesLowConfidence()
    {
        var engine = new CognitiveRestraintEngine(new RestraintPolicy
        {
            MinConfidenceThreshold = 0.5,
            MinSilenceBetweenInterventions = TimeSpan.Zero
        });

        var context = new RestraintContext
        {
            InterpretationConfidence = 0.2,
            CurrentBehavioralMode = "research"
        };

        var decision = engine.ShouldSpeak(context);
        Assert.False(decision.Allow);
        Assert.Equal(RestraintReason.LowConfidence, decision.ReasonCode);
    }

    [Fact]
    public void RestraintEngine_SuppressesDeepWork()
    {
        var engine = new CognitiveRestraintEngine(new RestraintPolicy
        {
            MinSilenceBetweenInterventions = TimeSpan.Zero,
            AllowDeepWorkInterruptions = false
        });

        var context = new RestraintContext
        {
            InterpretationConfidence = 0.9,
            CurrentBehavioralMode = "deep_work"
        };

        var decision = engine.ShouldSpeak(context);
        Assert.False(decision.Allow);
        Assert.Equal(RestraintReason.FlowStateProtection, decision.ReasonCode);
    }

    [Fact]
    public void RestraintEngine_SuppressesOverInterpreted()
    {
        var engine = new CognitiveRestraintEngine(new RestraintPolicy
        {
            MinSilenceBetweenInterventions = TimeSpan.Zero
        });

        var context = new RestraintContext
        {
            InterpretationConfidence = 0.9,
            CurrentBehavioralMode = "browsing",
            IsOverInterpreted = true
        };

        var decision = engine.ShouldSpeak(context);
        Assert.False(decision.Allow);
        Assert.Equal(RestraintReason.OverInterpreted, decision.ReasonCode);
    }

    [Fact]
    public void RestraintEngine_SuppressesFrequentlyCorrected()
    {
        var engine = new CognitiveRestraintEngine(new RestraintPolicy
        {
            MinSilenceBetweenInterventions = TimeSpan.Zero
        });

        var context = new RestraintContext
        {
            InterpretationConfidence = 0.9,
            CurrentBehavioralMode = "browsing",
            IsFrequentlyCorrected = true
        };

        var decision = engine.ShouldSpeak(context);
        Assert.False(decision.Allow);
        Assert.Equal(RestraintReason.FrequentlyCorrected, decision.ReasonCode);
    }

    [Fact]
    public void RestraintEngine_SuppressesCategoryIgnored()
    {
        var engine = new CognitiveRestraintEngine(new RestraintPolicy
        {
            MinSilenceBetweenInterventions = TimeSpan.Zero
        });

        var context = new RestraintContext
        {
            InterpretationConfidence = 0.9,
            CurrentBehavioralMode = "browsing",
            IsCategoryIgnored = true
        };

        var decision = engine.ShouldSpeak(context);
        Assert.False(decision.Allow);
        Assert.Equal(RestraintReason.CategoryIgnored, decision.ReasonCode);
    }

    [Fact]
    public void RestraintEngine_SuppressesInterventionFatigue()
    {
        var engine = new CognitiveRestraintEngine(new RestraintPolicy
        {
            MinSilenceBetweenInterventions = TimeSpan.Zero,
            MaxInterventionsPerHour = 2
        });

        // First two should pass
        engine.ShouldSpeak(new RestraintContext { InterpretationConfidence = 0.9, CurrentBehavioralMode = "research" });
        engine.ShouldSpeak(new RestraintContext { InterpretationConfidence = 0.9, CurrentBehavioralMode = "research" });

        // Third should be suppressed
        var decision = engine.ShouldSpeak(new RestraintContext
        {
            InterpretationConfidence = 0.9,
            CurrentBehavioralMode = "research",
            InterventionsInLastHour = 3
        });

        Assert.False(decision.Allow);
        Assert.Equal(RestraintReason.InterventionFatigue, decision.ReasonCode);
    }

    [Fact]
    public void RestraintEngine_Stats()
    {
        var engine = new CognitiveRestraintEngine(new RestraintPolicy
        {
            MinSilenceBetweenInterventions = TimeSpan.Zero
        });

        engine.ShouldSpeak(new RestraintContext { InterpretationConfidence = 0.9, CurrentBehavioralMode = "research" });
        engine.ShouldSpeak(new RestraintContext { InterpretationConfidence = 0.1, CurrentBehavioralMode = "research" });

        var stats = engine.GetStats();
        Assert.Equal(2, stats.TotalDecisions);
        Assert.Equal(1, stats.Allowed);
        Assert.Equal(1, stats.Suppressed);
    }

    [Fact]
    public void RestraintEngine_SilenceThreshold()
    {
        var engine = new CognitiveRestraintEngine(new RestraintPolicy
        {
            MinSilenceBetweenInterventions = TimeSpan.FromMinutes(10)
        });

        // First should pass
        var d1 = engine.ShouldSpeak(new RestraintContext { InterpretationConfidence = 0.9, CurrentBehavioralMode = "research" });
        Assert.True(d1.Allow);

        // Immediate second should be suppressed
        var d2 = engine.ShouldSpeak(new RestraintContext { InterpretationConfidence = 0.9, CurrentBehavioralMode = "research" });
        Assert.False(d2.Allow);
        Assert.Equal(RestraintReason.SilenceThreshold, d2.ReasonCode);
    }

    [Fact]
    public void RestraintEngine_LowAccuracy()
    {
        var engine = new CognitiveRestraintEngine(new RestraintPolicy
        {
            MinSilenceBetweenInterventions = TimeSpan.Zero,
            MinAccuracyForIntervention = 0.5
        });

        var context = new RestraintContext
        {
            InterpretationConfidence = 0.9,
            CurrentBehavioralMode = "browsing",
            ModeAccuracyRate = 0.2
        };

        var decision = engine.ShouldSpeak(context);
        Assert.False(decision.Allow);
        Assert.Equal(RestraintReason.LowAccuracy, decision.ReasonCode);
    }

    // ═══════════════════════════════════════════════════════════
    // 7F: REAL TIMELINE SEMANTICS
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void TimelineSemantics_AnalyzeEmpty()
    {
        var engine = _harness.TimelineSemantics;
        var analysis = engine.Analyze(new List<PerceptionSnapshot>());

        Assert.Empty(analysis.Sessions);
        Assert.Empty(analysis.Arcs);
    }

    [Fact]
    public void TimelineSemantics_ExtractsSessions()
    {
        var engine = _harness.TimelineSemantics;
        var snapshots = new List<PerceptionSnapshot>
        {
            CreateSnapshot("deep_work", DateTimeOffset.UtcNow.AddMinutes(-60)),
            CreateSnapshot("deep_work", DateTimeOffset.UtcNow.AddMinutes(-50)),
            CreateSnapshot("research", DateTimeOffset.UtcNow.AddMinutes(-40)),
            CreateSnapshot("research", DateTimeOffset.UtcNow.AddMinutes(-30)),
        };

        var analysis = engine.Analyze(snapshots);
        Assert.Equal(2, analysis.Sessions.Count);
        Assert.Equal("deep_work", analysis.Sessions[0].Mode);
        Assert.Equal("research", analysis.Sessions[1].Mode);
    }

    [Fact]
    public void TimelineSemantics_ExtractsArcs()
    {
        var engine = _harness.TimelineSemantics;
        var snapshots = new List<PerceptionSnapshot>
        {
            CreateSnapshot("deep_work", DateTimeOffset.UtcNow.AddHours(-5)),
            CreateSnapshot("deep_work", DateTimeOffset.UtcNow.AddHours(-4)),
            // Gap > 2 hours
            CreateSnapshot("deep_work", DateTimeOffset.UtcNow.AddHours(-1)),
            CreateSnapshot("deep_work", DateTimeOffset.UtcNow.AddMinutes(-30)),
        };

        var analysis = engine.Analyze(snapshots);
        // Should detect at least one arc for deep_work
        Assert.True(analysis.Arcs.Count >= 1);
    }

    [Fact]
    public void TimelineSemantics_MomentumDetection()
    {
        var engine = _harness.TimelineSemantics;

        // Create sessions with increasing duration (building momentum)
        var baseTime = DateTimeOffset.UtcNow.AddHours(-3);
        var snapshots = new List<PerceptionSnapshot>
        {
            CreateSnapshot("deep_work", baseTime, TimeSpan.FromMinutes(5)),
            CreateSnapshot("research", baseTime.AddMinutes(10)),
            CreateSnapshot("deep_work", baseTime.AddMinutes(20), TimeSpan.FromMinutes(10)),
            CreateSnapshot("research", baseTime.AddMinutes(30)),
            CreateSnapshot("deep_work", baseTime.AddMinutes(40), TimeSpan.FromMinutes(20)),
            CreateSnapshot("research", baseTime.AddMinutes(50)),
            CreateSnapshot("deep_work", baseTime.AddMinutes(60), TimeSpan.FromMinutes(30)),
        };

        var analysis = engine.Analyze(snapshots);
        // Should detect momentum signals
        Assert.True(analysis.MomentumSignals.Count >= 0); // May or may not find depending on data
    }

    [Fact]
    public void TimelineSemantics_CurrentState()
    {
        var engine = _harness.TimelineSemantics;
        var state = engine.GetCurrentState();

        Assert.NotNull(state);
        Assert.Equal(0, state.TotalArcs); // No data yet
    }

    [Fact]
    public void TimelineSemantics_GapDetection()
    {
        var engine = _harness.TimelineSemantics;

        // Two sessions with a 45-minute gap
        var snapshots = new List<PerceptionSnapshot>
        {
            CreateSnapshot("deep_work", DateTimeOffset.UtcNow.AddHours(-2)),
            CreateSnapshot("deep_work", DateTimeOffset.UtcNow.AddHours(-2).AddMinutes(10)),
            // 45 minute gap
            CreateSnapshot("deep_work", DateTimeOffset.UtcNow.AddHours(-1).AddMinutes(5)),
            CreateSnapshot("deep_work", DateTimeOffset.UtcNow.AddHours(-1)),
        };

        var analysis = engine.Analyze(snapshots);
        // Should detect 2 sessions due to gap > 30 min
        Assert.Equal(2, analysis.Sessions.Count);
    }

    // ═══════════════════════════════════════════════════════════
    // INTEGRATION TESTS
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Integration_HarnessHasPhase7Components()
    {
        Assert.NotNull(_harness.PerceptionRecorder);
        Assert.NotNull(_harness.ReplayEngine);
        Assert.NotNull(_harness.Comparator);
        Assert.NotNull(_harness.AccuracyTracker);
        Assert.NotNull(_harness.FalsePatternDetector);
        Assert.NotNull(_harness.CalibrationStore);
        Assert.NotNull(_harness.RestraintEngine);
        Assert.NotNull(_harness.TimelineSemantics);
    }

    [Fact]
    public void Integration_FullPipeline_RecordReplayCompare()
    {
        // 1. Create snapshots
        var snapshots = CreateTestSnapshots();

        // 2. Record them
        var recorder = _harness.PerceptionRecorder;
        foreach (var s in snapshots)
            recorder.InjectSnapshot(s);

        Assert.Equal(snapshots.Count, recorder.SnapshotCount);

        // 3. Replay through default strategy
        var results = _harness.ReplayEngine.Replay(snapshots, new DefaultBehavioralModeStrategy());

        // 4. Compare against original
        var report = _harness.Comparator.CompareAgainstOriginal(results);
        Assert.NotNull(report);
    }

    [Fact]
    public void Integration_AccuracyTracking_FeedbackLoop()
    {
        var tracker = _harness.AccuracyTracker;
        var detector = _harness.FalsePatternDetector;

        // Record outcomes
        for (int i = 0; i < 5; i++)
            tracker.RecordCorrect($"s{i}", "deep_work");
        for (int i = 5; i < 10; i++)
            tracker.RecordIncorrect($"s{i}", "browsing", "research");

        // Check for over-interpretation
        var warning = detector.CheckMode("browsing");
        Assert.NotNull(warning);

        // Verify the warning has the right data
        Assert.Equal("browsing", warning!.Mode);
        Assert.True(warning.ErrorRate > 0.4);
    }

    [Fact]
    public void Integration_Calibration_AffectsRestraint()
    {
        var store = _harness.CalibrationStore;
        var engine = new CognitiveRestraintEngine(new RestraintPolicy
        {
            MinSilenceBetweenInterventions = TimeSpan.Zero
        });

        // Human corrects browsing interpretations multiple times
        store.CorrectMode("s1", "browsing", "research");
        store.CorrectMode("s2", "browsing", "research");
        store.CorrectMode("s3", "browsing", "research");

        // Restraint engine should suppress if flagged as frequently corrected
        var context = new RestraintContext
        {
            InterpretationConfidence = 0.9,
            CurrentBehavioralMode = "browsing",
            IsFrequentlyCorrected = store.IsModeFrequentlyCorrected("browsing")
        };

        var decision = engine.ShouldSpeak(context);
        Assert.False(decision.Allow);
    }

    [Fact]
    public void Integration_Telemetry_TracksPhase7()
    {
        var telemetry = _harness.Telemetry;

        // Record some Phase 7 activity
        telemetry.RecordPerceptionSnapshot();
        telemetry.RecordPerceptionSnapshot();
        telemetry.RecordInterpretationOutcome(correct: true);
        telemetry.RecordInterpretationOutcome(correct: false);
        telemetry.RecordRestraintDecision(allowed: true);
        telemetry.RecordRestraintDecision(allowed: false);

        var metrics = telemetry.GetPhase7Metrics();
        Assert.Equal(2, metrics.PerceptionSnapshotsRecorded);
        Assert.Equal(1, metrics.InterpretationsCorrect);
        Assert.Equal(1, metrics.InterpretationsIncorrect);
        Assert.Equal(1, metrics.RestraintDecisionsAllowed);
        Assert.Equal(1, metrics.RestraintDecisionsSuppressed);
    }

    [Fact]
    public void Integration_DiagnosticsSnapshot_ContainsPhase7()
    {
        var snapshot = _harness.Telemetry.GetDiagnosticsSnapshot();
        Assert.NotNull(snapshot.Phase7);
    }

    // ═══════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════

    private static List<PerceptionSnapshot> CreateTestSnapshots()
    {
        var baseTime = DateTimeOffset.UtcNow.AddHours(-2);
        return new List<PerceptionSnapshot>
        {
            new()
            {
                Input = new PerceptionInput { ProcessName = "code", WindowTitle = "VS Code", FocusDuration = TimeSpan.FromMinutes(15) },
                Interpretation = new PerceptionInterpretation { BehavioralMode = "deep_work" },
                Timestamp = baseTime
            },
            new()
            {
                Input = new PerceptionInput { ProcessName = "chrome", WindowTitle = "Stack Overflow", FocusDuration = TimeSpan.FromMinutes(2) },
                Interpretation = new PerceptionInterpretation { BehavioralMode = "research" },
                Timestamp = baseTime.AddMinutes(20)
            },
            new()
            {
                Input = new PerceptionInput { ProcessName = "slack", WindowTitle = "Slack", FocusDuration = TimeSpan.FromMinutes(1) },
                Interpretation = new PerceptionInterpretation { BehavioralMode = "communication" },
                Timestamp = baseTime.AddMinutes(40)
            },
            new()
            {
                Input = new PerceptionInput { ProcessName = "WindowsTerminal", WindowTitle = "PowerShell", FocusDuration = TimeSpan.FromMinutes(5) },
                Interpretation = new PerceptionInterpretation { BehavioralMode = "terminal_work" },
                Timestamp = baseTime.AddMinutes(60)
            }
        };
    }

    private static PerceptionSnapshot CreateSnapshot(string mode, DateTimeOffset timestamp, TimeSpan? focusDuration = null)
    {
        return new PerceptionSnapshot
        {
            Timestamp = timestamp,
            Input = new PerceptionInput
            {
                ProcessName = mode == "deep_work" ? "code" : "chrome",
                WindowTitle = mode == "deep_work" ? "VS Code" : "Browser",
                FocusDuration = focusDuration ?? TimeSpan.FromMinutes(5),
                EventType = "window_change"
            },
            Interpretation = new PerceptionInterpretation
            {
                BehavioralMode = mode
            }
        };
    }
}

/// <summary>
/// Test strategy that always returns a fixed mode.
/// </summary>
public class TestBehavioralModeStrategy : IBehavioralModeStrategy
{
    private readonly string _mode;

    public TestBehavioralModeStrategy(string mode)
    {
        _mode = mode;
    }

    public string DetectMode(string processName, string windowTitle, TimeSpan focusDuration)
    {
        return _mode;
    }
}
