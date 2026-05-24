using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Engram.Store.Automation;
using Engram.Store.Inference;
using Engram.Store.Metabolism;
using Engram.Store.Wiki;
using Engram.Store.Identity;

namespace Engram.Store.Tests.Hardening;

public class DriverFallbackTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("ENGRAM_SAFE_MODE", null);
        DegradationTracker.Instance.ResetDegradation("WebView2FallbackActive");
        _workspace.Dispose();
    }

    [Fact]
    public async Task PlaywrightFailure_TriggersWebView2Fallback_AndDropsConfidence()
    {
        // Assert initial state is full confidence
        Assert.Equal(1.0, DegradationTracker.Instance.GetEnvironmentalConfidence());

        using var runtime = new BrowserAgentRuntime(null, _workspace.Paths);
        runtime.IsSimulationMode = false; // Turn off mock simulation mode
        runtime.SimulatePlaywrightFailure = true; // Force Playwright to throw failure on command

        // Attempt a browser action
        await runtime.NavigateAsync("https://example.com");

        // Verify fallback active
        Assert.True(DegradationTracker.Instance.IsDegraded("WebView2FallbackActive"));
        
        // Confidence should have dropped to 0.62 due to WebView2 fallback active
        Assert.Equal(0.62, DegradationTracker.Instance.GetEnvironmentalConfidence());

        // Verify pathology memory file exists and was written correctly
        var pathologyPath = Path.Combine(_workspace.Paths.Root, "diagnostics", "pathology_memory.json");
        Assert.True(File.Exists(pathologyPath));

        var lastFailureTime = runtime.GetLastFailureTime();
        Assert.True(lastFailureTime > DateTime.MinValue);

        // Verify we can retrieve the driver and it's WebView2DriverFallback
        var currentDriver = await runtime.GetDriverAsync();
        Assert.IsType<WebView2DriverFallback>(currentDriver);
    }

    [Fact]
    public void AutonomyCeiling_BlocksAutoApproval_UnderLowConfidence()
    {
        // Force low confidence
        DegradationTracker.Instance.SetDegradation("WebView2FallbackActive", true, "Playwright failure");
        Assert.True(DegradationTracker.Instance.GetEnvironmentalConfidence() < 0.8);

        var gate = new PermissionGate();
        var action = new AutomationAction
        {
            ActionId = "a1",
            Type = ActionType.Wait, // Normally auto-approved
            Description = "Wait for page load"
        };

        var permission = gate.CheckPermission(action);
        // Under low confidence, wait action must be Pending (requiring human approval) instead of AutoApproved!
        Assert.Equal(ActionPermission.Pending, permission);
    }

    [Fact]
    public void EpistemicCaution_ScalesDownContradictionSeverity_UnderLowConfidence()
    {
        // Create nodes and stores
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new WikiNodeStore(_workspace.Paths);
        var identity = new IdentityStore(_workspace.Paths);

        // Save a goal that is fading (low salience)
        var goal = new WikiNode
        {
            NodeId = "fading-goal",
            Title = "Fading Goal",
            NodeType = WikiNodeType.Goal,
            Salience = 0.1,
            LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-10)
        };
        store.Save(goal);

        // Save an active concept that is unrelated
        var activeConcept = new WikiNode
        {
            NodeId = "active-concept",
            Title = "Unrelated Activity",
            NodeType = WikiNodeType.Concept,
            Salience = 0.9,
            LastTouchedAt = DateTimeOffset.UtcNow
        };
        store.Save(activeConcept);

        var detector = new ContradictionDetector(store, identity);

        // With high confidence, goal fading contradiction should be High severity
        DegradationTracker.Instance.ResetDegradation("WebView2FallbackActive");
        Assert.Equal(1.0, DegradationTracker.Instance.GetEnvironmentalConfidence());

        var highConfContradictions = detector.DetectAll();
        Assert.NotEmpty(highConfContradictions);
        Assert.Contains(highConfContradictions, c => c.Severity == ContradictionSeverity.High);

        // Force low confidence (< 0.8)
        DegradationTracker.Instance.SetDegradation("WebView2FallbackActive", true, "Playwright failure");
        Assert.True(DegradationTracker.Instance.GetEnvironmentalConfidence() < 0.8);

        var lowConfContradictions = detector.DetectAll();
        Assert.NotEmpty(lowConfContradictions);
        // The contradiction severity should have been scaled down from High to Medium!
        Assert.Contains(lowConfContradictions, c => c.Severity == ContradictionSeverity.Medium);
        Assert.DoesNotContain(lowConfContradictions, c => c.Severity == ContradictionSeverity.High);
    }

    [Fact]
    public void PathologyDecay_AndHysteresisCooldown_ManageDistrust()
    {
        // Set degradation
        DegradationTracker.Instance.SetDegradation("WebView2FallbackActive", true, "Playwright error");
        Assert.True(DegradationTracker.Instance.IsDegraded("WebView2FallbackActive"));

        // Hysteresis decay evaluation
        var initialDistrust = DegradationTracker.Instance.GetDistrustLevel("WebView2FallbackActive", DateTime.UtcNow);
        Assert.True(initialDistrust > 0.99);

        // Verify exponential decay over simulated elapsed time (e.g. 500 seconds ago)
        var decayedDistrust = DegradationTracker.Instance.GetDistrustLevel("WebView2FallbackActive", DateTime.UtcNow.AddSeconds(-500));
        // D(500) = 1.0 * e^(-0.005 * 500) = e^(-2.5) ~ 0.082
        Assert.True(decayedDistrust < 0.1);
        Assert.True(decayedDistrust > 0.0);

        // Turn off degradation (which starts hysteresis timer)
        DegradationTracker.Instance.SetDegradation("WebView2FallbackActive", false);
        
        // Hysteresis prevents immediate restoration; it should still be degraded
        Assert.True(DegradationTracker.Instance.IsDegraded("WebView2FallbackActive"));
    }
}
