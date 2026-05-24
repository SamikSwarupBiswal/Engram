using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Engram.Store.Automation;

namespace Engram.Store.Tests.Automation;

public class CoexistenceCollisionTests
{
    [Fact]
    public async Task HumanIntentCollisionEngine_ShouldAssessUserActivity()
    {
        var monitor = new SovereigntyMonitor(2000, () => 50); // User active (50ms idle)
        var safety = new ExecutionSafetyManager();
        var tracker = new CoexistenceMetricsTracker();
        var overrideEngine = new HumanOverridePriorityEngine(monitor, safety, tracker);
        var collisionEngine = new HumanIntentCollisionEngine(overrideEngine, monitor);

        var decision = await collisionEngine.AssessCollisionAsync("wf1", "explorer", "File Explorer");
        Assert.Equal(CooperativeDecision.Yield, decision);
    }

    [Fact]
    public async Task CooperativeCursorProtocol_ShouldYieldSovereignty()
    {
        var activeMonitor = new SovereigntyMonitor(2000, () => 100); // User active
        var protocol1 = new CooperativeCursorProtocol(activeMonitor);
        
        Assert.False(await protocol1.RequestCursorSovereigntyAsync());
        Assert.False(protocol1.CursorSovereigntyHeld);

        var idleMonitor = new SovereigntyMonitor(2000, () => 5000); // User idle
        var protocol2 = new CooperativeCursorProtocol(idleMonitor);
        
        Assert.True(await protocol2.RequestCursorSovereigntyAsync());
        Assert.True(protocol2.CursorSovereigntyHeld);
        
        protocol2.YieldToHuman();
        Assert.False(protocol2.CursorSovereigntyHeld);
    }

    [Fact]
    public async Task SilentYieldEngine_ShouldPauseWhileUserActive()
    {
        var activeCount = 0;
        // Mock idle provider: first 3 checks active, then idle
        Func<int> idleProvider = () => 
        {
            activeCount++;
            return activeCount <= 3 ? 100 : 5000;
        };

        var monitor = new SovereigntyMonitor(2000, idleProvider);
        var yieldEngine = new SilentYieldEngine(monitor);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var start = DateTimeOffset.UtcNow;
        
        await yieldEngine.YieldSilentlyAsync(TimeSpan.FromMilliseconds(50), cts.Token);
        var elapsed = DateTimeOffset.UtcNow - start;

        // Verify it yielded at least until the idle state kicked in (needs multiple loops)
        Assert.True(activeCount > 3);
    }
}
