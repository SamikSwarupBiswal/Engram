using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Engram.Store.Automation;
using Engram.Store.Governance;
using Xunit;

namespace Engram.Store.Tests;

public class PhaseD6CoexistenceTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose()
    {
        _workspace.Dispose();
    }

    // ─── 1. Contextual Autonomy Modulation Tests ───

    [Fact]
    public void ContextualAutonomyModulator_SoftensAutonomy_WhenMultitaskingIsHigh()
    {
        var config = new GovernanceConfig { BaselineAutonomy = AutonomyLevel.Aggressive };
        var modulator = new ContextualAutonomyModulator(config);

        // Under normal velocity, active equals baseline
        Assert.Equal(AutonomyLevel.Aggressive, modulator.DetermineModulatedAutonomy(multitaskingVelocity: 2));

        // High multitasking velocity (> 6 switches) softs active autonomy to Medium
        Assert.Equal(AutonomyLevel.Medium, modulator.DetermineModulatedAutonomy(multitaskingVelocity: 7));

        // Typing burst softs baseline to Medium as well
        Assert.Equal(AutonomyLevel.Medium, modulator.DetermineModulatedAutonomy(multitaskingVelocity: 2, isTypingBurst: true));

        // If baseline is Medium, it softs to Low under load
        config.BaselineAutonomy = AutonomyLevel.Medium;
        Assert.Equal(AutonomyLevel.Low, modulator.DetermineModulatedAutonomy(multitaskingVelocity: 8));
    }

    // ─── 2. Autonomy Decay Tests ───

    [Fact]
    public void AutonomyDecayEngine_RegressesAutonomy_UnderUserFriction()
    {
        var config = new GovernanceConfig();
        var decay = new AutonomyDecayEngine(config);

        Assert.Equal(0.0, decay.FrictionScore);

        // Record a series of friction events
        decay.RecordFriction(1.0);
        decay.RecordFriction(1.0);

        Assert.Equal(2.0, decay.FrictionScore);

        // Aggressive autonomy regresses to Medium when friction >= 1.5
        var active = decay.DetermineDecayedAutonomy(AutonomyLevel.Aggressive);
        Assert.Equal(AutonomyLevel.Medium, active);

        // Medium regresses to Low
        active = decay.DetermineDecayedAutonomy(AutonomyLevel.Medium);
        Assert.Equal(AutonomyLevel.Low, active);

        // Add more friction -> score >= 3.0
        decay.RecordFriction(1.5);
        Assert.Equal(3.5, decay.FrictionScore);

        // All levels regress to Low
        Assert.Equal(AutonomyLevel.Low, decay.DetermineDecayedAutonomy(AutonomyLevel.Aggressive));

        // Record success events decreases friction score
        decay.RecordSuccess();
        decay.RecordSuccess();

        Assert.Equal(2.5, decay.FrictionScore);
    }

    // ─── 3. Domain Ceilings & PermissionGate Tests ───

    [Fact]
    public void PermissionGate_EnforcesDomainCeilings_RegardlessOfAutonomySetting()
    {
        var config = new GovernanceConfig
        {
            BaselineAutonomy = AutonomyLevel.Aggressive,
            ActiveAutonomy = AutonomyLevel.Aggressive // Aggressive active autonomy
        };

        var gate = new PermissionGate(_workspace.Paths, config);

        // Create a destructive action
        var deleteAction = new AutomationAction
        {
            Type = ActionType.Click,
            Description = "Delete all database tables"
        };

        // Destructive actions have Low ceiling -> must return Pending (forced approval)
        Assert.Equal(ActionPermission.Pending, gate.CheckPermission(deleteAction));

        // Create a filesystem write action
        var saveAction = new AutomationAction
        {
            Type = ActionType.Click,
            Description = "Save results to results.csv"
        };

        // Filesystem writes have Medium ceiling -> required warmup is 10
        // Since warmup count is 0 (< 10), it should return Pending
        Assert.Equal(ActionPermission.Pending, gate.CheckPermission(saveAction));
    }

    // ─── 4. Failure Narratives & Legibility Tests ───

    [Fact]
    public void RecoveryLegibilityEngine_TranslatesTechnicalExceptions_IntoOperationallyNeutralLanguage()
    {
        var engine = new RecoveryLegibilityEngine();

        // 1. Playwright/Browser Timeout
        var explanation = engine.TranslateFailure(
            errorMessage: "Playwright timeout error", 
            exceptionDetails: "Microsoft.Playwright.PlaywrightException: Timeout 30000ms exceeded"
        );
        Assert.Equal("The operation exceeded the scheduled time window.", explanation);

        // 2. Playwright general error
        explanation = engine.TranslateFailure(
            errorMessage: "Browser page crashed", 
            exceptionDetails: "Microsoft.Playwright.PlaywrightException: Page closed"
        );
        Assert.Equal("The browser environment changed unexpectedly.", explanation);

        // 3. Verification failure
        explanation = engine.TranslateFailure(
            errorMessage: "Verification failed: target element not found", 
            exceptionDetails: "InvalidOperationException: Assert failed"
        );
        Assert.Equal("Verification confidence dropped below safe threshold.", explanation);

        // 4. Focus/Application mismatch
        explanation = engine.TranslateFailure(
            errorMessage: "Active window is not Chrome", 
            exceptionDetails: "WindowFocusMismatchException: expected Chrome, got Spotify"
        );
        Assert.Equal("The target application no longer matched the expected state.", explanation);

        // Ensure there is no emotionally-loaded or humanized language
        Assert.DoesNotContain("confused", explanation.ToLowerInvariant());
        Assert.DoesNotContain("sorry", explanation.ToLowerInvariant());
        Assert.DoesNotContain("think", explanation.ToLowerInvariant());
    }

    [Fact]
    public async Task FailureNarrativeRecorder_SavesNeutralNarratives()
    {
        var recorder = new FailureNarrativeRecorder(_workspace.Paths.Root);
        var narrative = new FailureNarrative
        {
            WorkflowId = "wf-d6",
            Goal = "Test narrative writing",
            FailedStepId = "step-1",
            StepDescription = "Type credentials",
            TechnicalDetails = "TimeoutException",
            LegibleExplanation = "The operation exceeded the scheduled time window.",
            AutonomyLevel = "Aggressive",
            RecoveryAttempted = true,
            RecoverySucceeded = false,
            RecoveryExplanation = "Automatic recovery was unable to resolve the divergence."
        };

        await recorder.RecordFailureNarrativeAsync(narrative);

        var list = await recorder.GetNarrativesAsync();
        Assert.Single(list);
        Assert.Equal("wf-d6", list[0].WorkflowId);
        Assert.Equal("The operation exceeded the scheduled time window.", list[0].LegibleExplanation);
    }

    // ─── 5. Telemetry Calculations Tests ───

    [Fact]
    public void SilenceQualityTracker_And_PressureMonitor_CalculateCorrectScores()
    {
        var silence = new SilenceQualityTracker();
        var pressure = new AuthorityPressureMonitor();
        var residue = new CognitiveResidueTracker();
        var comfort = new RecoveryComfortAnalyzer();

        // Silence quality calculations
        silence.RecordInterruption();
        silence.RecordInterventionJustification(justified: true);
        silence.RecordInterruption();
        silence.RecordInterventionJustification(justified: false);

        Assert.Equal(0.5, silence.GetJustificationRatio());
        Assert.True(silence.CalculateSilenceQualityScore() > 0.0);

        // Pressure calculations
        pressure.RecordPrompt();
        pressure.RecordPrompt();
        pressure.RecordUserActivity();

        Assert.True(pressure.CalculatePressureScore() > 0.0);

        // Cognitive Residue calculations
        residue.RecordInterruption();
        residue.RecordUserResponse(dismissedOrCancelled: true);

        Assert.True(residue.CurrentScore > 0.0);

        // Recovery comfort calculations
        comfort.RecordFailure(recoveredSilently: true);
        comfort.RecordFailure(recoveredSilently: false);
        comfort.RecordUserFrustrationAfterFailure();

        Assert.Equal(0.5, comfort.CalculateRecoveryComfortScore());
    }
}
