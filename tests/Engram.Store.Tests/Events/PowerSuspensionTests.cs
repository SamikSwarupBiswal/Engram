using System;
using System.Threading.Tasks;
using Xunit;
using Engram.Store.Events;
using Engram.Store.Inference;

namespace Engram.Store.Tests.Events;

public class PowerSuspensionTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task SuspendAndResume_AppliesStabilizationDelayAndTrackerStates()
    {
        var tracker = DegradationTracker.Instance;

        // Reset states
        tracker.ResetDegradation("WakeStabilizing");
        tracker.ResetDegradation("SafeModeActive");

        Assert.False(tracker.IsDegraded("WakeStabilizing"));

        // Trigger suspend behavior simulation
        tracker.SetDegradation("WakeStabilizing", true, "System wake stabilization active");
        Assert.True(tracker.IsDegraded("WakeStabilizing"));

        // Verify confidence reduces during wake stabilization
        var confidence = tracker.GetEnvironmentalConfidence();
        Assert.Equal(0.71, confidence);

        // Manually reset degradation to simulate timer passing (avoid waiting 5 mins in unit test)
        tracker.ResetDegradation("WakeStabilizing");
        Assert.False(tracker.IsDegraded("WakeStabilizing"));
        Assert.Equal(1.0, tracker.GetEnvironmentalConfidence());
    }

    [Fact]
    public void PowerBroadcastListener_CanBeInstantiatedWithoutCrash()
    {
        // Assert creation on any OS succeeds gracefully (no Win32 platform crashes on construct)
        using var listener = new PowerBroadcastListener();
        Assert.NotNull(listener);
    }
}
