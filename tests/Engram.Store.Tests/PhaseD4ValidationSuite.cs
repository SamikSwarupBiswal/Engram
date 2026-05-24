using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Engram.Store.Events;
using Engram.Store.Automation;
using Xunit;

namespace Engram.Store.Tests;

public class PhaseD4ValidationSuite : IDisposable
{
    private readonly TempWorkspace _workspace = new();
    private readonly InMemoryEventBus _eventBus;

    public PhaseD4ValidationSuite()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        _eventBus = new InMemoryEventBus();
    }

    public void Dispose()
    {
        _eventBus.Dispose();
        _workspace.Dispose();
    }

    // ─── Coexistence Telemetry Tests ───

    [Fact]
    public void CoexistenceMetricsTracker_CalculatesCorrectScoresAndWritesReports()
    {
        var tracker = new CoexistenceMetricsTracker(_workspace.Paths.Root);

        tracker.RecordAction(isBackground: true, userActive: false, succeeded: true);
        tracker.RecordAction(isBackground: true, userActive: true, succeeded: false);
        tracker.RecordInterruption(isCancel: false);
        tracker.RecordIntervention(accepted: true);
        tracker.RecordApprovalPrompt();
        tracker.RecordSilence(1800); // 30 mins

        var metrics = tracker.CalculateMetrics();

        Assert.True(metrics.InterruptionIrritation > 0.0);
        Assert.Equal(0.5, metrics.AutonomyDiscomfort); // 1 active out of 2 actions
        Assert.Equal(1.0, metrics.InterventionUsefulness); // 1 accepted, 0 dismissed
        Assert.True(metrics.SilenceQuality > 0.0);

        var reportPath = tracker.GenerateCoexistenceReport();
        var relReportPath = tracker.GenerateExecutionReliabilityReport();
        var pacingReportPath = tracker.GenerateTrustPacingReport();
        var ecologyReportPath = tracker.GenerateEcologicalHealthReport();
        var fatigueReportPath = tracker.GenerateInterventionFatigueReport();

        Assert.True(File.Exists(reportPath));
        Assert.True(File.Exists(relReportPath));
        Assert.True(File.Exists(pacingReportPath));
        Assert.True(File.Exists(ecologyReportPath));
        Assert.True(File.Exists(fatigueReportPath));
    }

    // ─── HOPE Tests ───

    [Fact]
    public void HOPE_EvaluatesYieldFirstAbortSecond_FlowsCorrectly()
    {
        var sovereignty = new SovereigntyMonitor(2000, () => 500); // User is active
        var safety = new ExecutionSafetyManager();
        var tracker = new CoexistenceMetricsTracker(_workspace.Paths.Root);
        var hope = new HumanOverridePriorityEngine(sovereignty, safety, tracker);

        // Immediate user active -> brief interruption -> Yield
        var decision = hope.EvaluateControlTransfer("test-wf", "chrome", "Google Chrome");
        Assert.Equal(CooperativeDecision.Yield, decision);

        // Explicit cancel -> Abort
        decision = hope.EvaluateControlTransfer("test-wf", "chrome", "Google Chrome", isExplicitCancel: true);
        Assert.Equal(CooperativeDecision.Abort, decision);

        // Destructive divergence -> Terminate
        decision = hope.EvaluateControlTransfer("test-wf", "chrome", "Google Chrome", hasDestructiveDivergence: true);
        Assert.Equal(CooperativeDecision.Terminate, decision);
    }

    // ─── Intent Reconciliation Engine Tests ───

    [Fact]
    public void IRE_ToleratesAttentionDivergenceWithHysteresis()
    {
        var ire = new IntentReconciliationEngine(reconciliationThreshold: 0.4);

        // 1. Initial State
        Assert.Equal(1.0, ire.IntentConfidence);
        Assert.False(ire.ShouldReconcile());

        // 2. Chat interaction: different command -> drops confidence
        ire.HandleChatInteraction("Goal: code report", "MemoryQuery");
        Assert.True(ire.IntentConfidence < 1.0);

        // 3. User browses unrelated app -> drift evaluated
        bool reconcile = ire.EvaluateIntentDrift("Goal: code report", "spotify", "Spotify");
        
        // Hysteresis keeps confidence above threshold for the first brief check
        Assert.False(reconcile);
        Assert.True(ire.IntentConfidence >= 0.4);
    }

    // ─── Continuity Checkpoints Tests ───

    [Fact]
    public void WorkflowContinuitySnapshots_SerializesAndValidatesResumability()
    {
        var store = new WorkflowPersistenceStore(_workspace.Paths.Root);
        var snapshots = new WorkflowContinuitySnapshots(store);

        var checkpoint = new WorkflowCheckpoint
        {
            WorkflowId = "wf-123",
            Goal = "Research GPUs",
            PlanJson = "{\"PlanId\":\"plan-1\",\"Steps\":[]}"
        };

        bool ok = snapshots.ValidateSnapshotResumability(checkpoint, out string error);
        Assert.True(ok, error);

        var invalidCheckpoint = new WorkflowCheckpoint
        {
            WorkflowId = "wf-123",
            PlanJson = "invalid-json"
        };
        bool fail = snapshots.ValidateSnapshotResumability(invalidCheckpoint, out error);
        Assert.False(fail);
        Assert.Contains("invalid", error, StringComparison.OrdinalIgnoreCase);
    }

    // ─── Verification Fusion Engine Tests ───

    [Fact]
    public void VerificationFusionEngine_ReconcilesConflictingSignalsAndConfidence()
    {
        var fusion = new VerificationFusionEngine();
        
        var signals = new List<VerificationSignal>
        {
            new() { Tier = VerificationTier.Structured, Outcome = true, SignalConfidence = 1.0 },
            new() { Tier = VerificationTier.Ocr, Outcome = false, SignalConfidence = 0.8 }
        };

        var result = fusion.Fuse(signals);

        // Structured (1.0 weight) wins over OCR (0.6 weight * 0.8 confidence)
        Assert.True(result.IsVerified);
        Assert.True(result.VerificationConfidence > 0.5);
        Assert.Contains("Conflict", result.Message);
    }

    // ─── Cross Application Context Binder Tests ───

    [Fact]
    public void CrossApplicationContextBinder_BridgesContextAcrossApps()
    {
        var binder = new CrossApplicationContextBinder();
        var docContext = new Engram.Store.Automation.ExecutionContext();
        var commContext = new Engram.Store.Automation.ExecutionContext();

        // 1. Scraped browser data to document context
        binder.BridgeBrowserToDocument("GPU Report\nThis report compares the latest GPUs.", docContext);
        Assert.Equal("GPU Report", docContext.GetVariable<string>("document_title"));
        Assert.Equal("This report compares the latest GPUs.", docContext.GetVariable<string>("document_content"));

        // 2. Filesystem output to communication context
        binder.BridgeFilesystemToCommunication(@"C:\reports\gpu_research.docx", commContext);
        Assert.Equal(@"C:\reports\gpu_research.docx", commContext.GetVariable<string>("attachment_path"));
        Assert.Contains("gpu_research", commContext.GetVariable<string>("email_subject"));
    }

    // ─── Environment Drift Recovery Tests ───

    [Fact]
    public async Task EnvironmentDriftRecovery_PerformsCorrectRepairs()
    {
        var eventBus = new InMemoryEventBus();
        var resilience = new EnvironmentalResilienceEngine(eventBus);
        var recovery = new EnvironmentDriftRecovery(resilience);

        var report = new EnvironmentSyncReport();
        report.IsSynchronized = false;
        report.Divergences.Add(new EnvironmentDivergence
        {
            Source = "Workflow",
            Expected = "wf-1",
            Actual = "wf-2"
        });

        var context = new Engram.Store.Automation.ExecutionContext();
        bool success = await recovery.AttemptRecoveryAsync(report, context);

        Assert.True(success);
        Assert.Equal("wf-2", context.GetVariable<string>("active_workflow_id"));
    }
}
