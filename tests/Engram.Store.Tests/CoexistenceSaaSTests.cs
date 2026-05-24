using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Engram.Store.Events;
using Engram.Store.Governance;
using Engram.Store.Metabolism;
using Engram.Store.Automation;
using Engram.Store.Identity;
using Engram.Store.Inference;
using Xunit;

namespace Engram.Store.Tests;

public class CoexistenceSaaSTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();
    private readonly InMemoryEventBus _eventBus;
    private readonly IdentityStore _identityStore;

    public CoexistenceSaaSTests()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        _eventBus = new InMemoryEventBus();
        _identityStore = new IdentityStore(_workspace.Paths);
    }

    public void Dispose()
    {
        _eventBus.Dispose();
        _identityStore.Dispose();
        _workspace.Dispose();
    }

    // ─── 1. Thread-Safety & High Concurrency Tests ───

    [Fact]
    public async Task AutonomyDecayEngine_HighConcurrentLoad_MaintainsMathematicalConsistency()
    {
        var config = new GovernanceConfig();
        var decayEngine = new AutonomyDecayEngine(config);

        int concurrentThreads = 30;
        int operationsPerThread = 200;
        var tasks = new List<Task>();

        // Start concurrent tasks updating friction and successes
        for (int i = 0; i < concurrentThreads; i++)
        {
            bool recordFriction = (i % 3 == 0); // 10 threads friction, 20 threads success
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < operationsPerThread; j++)
                {
                    if (recordFriction)
                    {
                        decayEngine.RecordFriction(0.1);
                    }
                    else
                    {
                        decayEngine.RecordSuccess();
                    }
                }
            }));
        }

        await Task.WhenAll(tasks);

        double score = decayEngine.FrictionScore;
        Assert.True(score >= 0.0, $"Friction score {score} must be non-negative");
        Assert.True(score <= 5.0, $"Friction score {score} must be capped at 5.0");

        // Verify that DetermineDecayedAutonomy remains correct and does not throw under concurrency
        var checkTasks = Enumerable.Range(0, 50).Select(_ => Task.Run(() =>
        {
            var level = decayEngine.DetermineDecayedAutonomy(AutonomyLevel.Aggressive);
            Assert.True(level == AutonomyLevel.Low || level == AutonomyLevel.Medium || level == AutonomyLevel.Aggressive);
        }));

        await Task.WhenAll(checkTasks);
    }

    [Fact]
    public async Task FailureNarrativeRecorder_ConcurrentDiskWrites_DoesNotCorruptJson()
    {
        var recorder = new FailureNarrativeRecorder(_workspace.Root);

        int concurrentWrites = 40;
        var tasks = new List<Task>();

        for (int i = 0; i < concurrentWrites; i++)
        {
            int index = i;
            tasks.Add(Task.Run(async () =>
            {
                var narrative = new FailureNarrative
                {
                    WorkflowId = $"wf-saas-{index}",
                    Goal = $"SAAS Production Stress Test {index}",
                    FailedStepId = $"step-{index}",
                    StepDescription = $"Automated click of element {index}",
                    TechnicalDetails = "TimeoutException: Microsoft.Playwright.PlaywrightException",
                    LegibleExplanation = $"The browser execution window timed out on step {index}.",
                    AutonomyLevel = "Medium",
                    RecoveryAttempted = true,
                    RecoverySucceeded = false,
                    RecoveryExplanation = "The system failed to recover stability."
                };

                await recorder.RecordFailureNarrativeAsync(narrative);
            }));
        }

        await Task.WhenAll(tasks);

        var list = await recorder.GetNarrativesAsync();
        Assert.NotNull(list);
        Assert.Equal(concurrentWrites, list.Count);

        var ids = list.Select(n => n.WorkflowId).Distinct().ToList();
        Assert.Equal(concurrentWrites, ids.Count);

        for (int i = 0; i < concurrentWrites; i++)
        {
            Assert.Contains($"wf-saas-{i}", ids);
        }
    }

    // ─── 2. Boundary Condition & Safety Ceilings ───

    [Theory]
    // Destructive ceilings must be capped to Low (forces approval -> Pending)
    [InlineData("destructive", "Delete database tables", ActionPermission.Pending)]
    [InlineData("destructive", "Wipe config directories", ActionPermission.Pending)]
    // Financial ceilings must be capped to Low (forces approval -> Pending)
    [InlineData("financial", "Pay invoice bill amount", ActionPermission.Pending)]
    [InlineData("financial", "Checkout cart content", ActionPermission.Pending)]
    // Filesystem writes ceiling is Medium (warmup threshold of 10)
    // We will verify that under Medium ceiling, warmup counts are strictly checked
    [InlineData("filesystem_write", "Save file report.xlsx", ActionPermission.Pending)]
    // Email ceiling is Medium (warmup threshold of 10)
    [InlineData("email", "Send mail notification", ActionPermission.Pending)]
    // Coding/Browser Research ceiling is Aggressive (warmup threshold of 5)
    [InlineData("browser_research", "Search StackOverflow error message", ActionPermission.Pending)]
    public void PermissionGate_CeilingDeterministicGuards_NeverFailOpen(
        string domainCategory, string description, ActionPermission expectedInitialPermission)
    {
        var config = new GovernanceConfig
        {
            BaselineAutonomy = AutonomyLevel.Aggressive,
            ActiveAutonomy = AutonomyLevel.Aggressive
        };

        var gate = new PermissionGate(_workspace.Paths, config);
        var action = new AutomationAction
        {
            Type = ActionType.Click,
            Description = description
        };

        // Assert initial state is pending because warmup counts are 0
        Assert.Equal(expectedInitialPermission, gate.CheckPermission(action));

        // Record 6 successes (warmup count = 6)
        for (int i = 0; i < 6; i++)
        {
            gate.RecordSuccess(action);
        }

        var result = gate.CheckPermission(action);

        if (domainCategory == "destructive" || domainCategory == "financial")
        {
            // Low ceiling rules: always require approval (Pending) regardless of warmup streaks
            Assert.Equal(ActionPermission.Pending, result);
        }
        else if (domainCategory == "filesystem_write" || domainCategory == "email")
        {
            // Medium ceiling required warmup is 10. Since warmup is 6, it must still be Pending.
            Assert.Equal(ActionPermission.Pending, result);

            // Record 5 more successes (total 11)
            for (int i = 0; i < 5; i++)
            {
                gate.RecordSuccess(action);
            }

            // Warmup (11) >= 10, so it should graduate to AutoApproved under Aggressive active autonomy
            Assert.Equal(ActionPermission.AutoApproved, gate.CheckPermission(action));
        }
        else
        {
            // Aggressive ceiling required warmup is 5. Since warmup is 6, it should graduate to AutoApproved.
            Assert.Equal(ActionPermission.AutoApproved, result);
        }
    }

    [Fact]
    public void PermissionGate_SafeActions_RespectConfidenceHysteresis()
    {
        // Ensure baseline state is clean
        DegradationTracker.Instance.ResetDegradation("WebView2FallbackActive");
        try
        {
            var config = new GovernanceConfig
            {
                BaselineAutonomy = AutonomyLevel.Aggressive,
                ActiveAutonomy = AutonomyLevel.Aggressive
            };

            var gate = new PermissionGate(_workspace.Paths, config);

            var safeAction = new AutomationAction
            {
                Type = ActionType.Screenshot,
                Description = "Take background window screenshot"
            };

            // 1. With clean system (confidence = 1.0), it should be auto-approved since confidence >= 0.65 (Aggressive ceiling threshold)
            Assert.Equal(ActionPermission.AutoApproved, gate.CheckPermission(safeAction));

            // 2. Set degradation WebView2FallbackActive -> confidence degrades to 0.62
            // 0.62 < 0.65 threshold, so it must return Pending (fails confidence safety check)
            DegradationTracker.Instance.SetDegradation("WebView2FallbackActive", true);
            Assert.Equal(ActionPermission.Pending, gate.CheckPermission(safeAction));
        }
        finally
        {
            // Clean up to prevent other tests from failing
            DegradationTracker.Instance.ResetDegradation("WebView2FallbackActive");
        }
    }

    // ─── 3. Stress Soak Telemetry Validation ───

    [Fact]
    public void TelemetryTrackers_StressSoakSimulation_KeepsNormalizedScores()
    {
        var silence = new SilenceQualityTracker();
        var pressure = new AuthorityPressureMonitor();
        var residue = new CognitiveResidueTracker();
        var comfort = new RecoveryComfortAnalyzer();

        // Simulate 2000 events in a dense sequence
        int iterations = 500;
        for (int i = 0; i < iterations; i++)
        {
            // Mix prompts, active window changes, frustrations, silent/loud failures
            pressure.RecordPrompt();
            silence.RecordInterruption();
            residue.RecordInterruption();

            if (i % 2 == 0)
            {
                pressure.RecordUserActivity();
                silence.RecordInterventionJustification(justified: true);
                residue.RecordUserResponse(dismissedOrCancelled: false);
                comfort.RecordFailure(recoveredSilently: true);
            }
            else
            {
                silence.RecordInterventionJustification(justified: false);
                residue.RecordUserResponse(dismissedOrCancelled: true);
                residue.RecordContextSwitchAfterInterruption();
                comfort.RecordFailure(recoveredSilently: false);
                comfort.RecordUserFrustrationAfterFailure();
            }

            // Verify scores remain within valid [0.0, 1.0] range and are NOT NaN or Infinity
            double silenceQuality = silence.CalculateSilenceQualityScore();
            double pressureScore = pressure.CalculatePressureScore();
            double residueScore = residue.CurrentScore;
            double comfortScore = comfort.CalculateRecoveryComfortScore();

            Assert.True(!double.IsNaN(silenceQuality) && !double.IsInfinity(silenceQuality));
            Assert.True(silenceQuality >= 0.0 && silenceQuality <= 1.0);

            Assert.True(!double.IsNaN(pressureScore) && !double.IsInfinity(pressureScore));
            Assert.True(pressureScore >= 0.0 && pressureScore <= 1.0);

            Assert.True(!double.IsNaN(residueScore) && !double.IsInfinity(residueScore));
            Assert.True(residueScore >= 0.0 && residueScore <= 1.0);

            Assert.True(!double.IsNaN(comfortScore) && !double.IsInfinity(comfortScore));
            Assert.True(comfortScore >= 0.0 && comfortScore <= 1.0);
        }
    }

    [Fact]
    public void TelemetryTrackers_ExtremeInputSpikes_DoesNotOverflow()
    {
        var pressure = new AuthorityPressureMonitor();

        // Record a massive burst of user activity and prompts (e.g. 1000 events)
        for (int i = 0; i < 1000; i++)
        {
            pressure.RecordPrompt();
            pressure.RecordUserActivity();
        }

        double score = pressure.CalculatePressureScore();
        Assert.True(score >= 0.0 && score <= 1.0);
    }
}
