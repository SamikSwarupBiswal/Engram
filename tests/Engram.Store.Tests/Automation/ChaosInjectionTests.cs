using System;
using System.Collections.Generic;
using Xunit;
using Engram.Store.Automation;

namespace Engram.Store.Tests.Automation;

public class ChaosInjectionTests
{
    [Fact]
    public void ChaosInjectionHarness_ShouldDispatchEvents()
    {
        var harness = new ChaosInjectionHarness();
        var eventsList = new List<ChaosEvent>();
        harness.OnChaosInjected += (ev) => eventsList.Add(ev);

        // Environmental
        harness.InjectChaos(ChaosEvent.BrowserCrash);
        // Behavioral
        harness.InjectChaos(ChaosEvent.UserImpatience);

        Assert.Equal(2, eventsList.Count);
        Assert.Contains(ChaosEvent.BrowserCrash, eventsList);
        Assert.Contains(ChaosEvent.UserImpatience, eventsList);
    }

    [Fact]
    public void RecoveryPlanner_ShouldResolveDecisionsCorrectly()
    {
        var planner = new RecoveryPlanner();
        var reversibleSemantics = new MutationBoundarySemantics { IsReversible = true, IsRecoverable = true };
        var irreversibleSemantics = new MutationBoundarySemantics { IsReversible = false, IsRecoverable = false, IsIrreversible = true };
        var externalSemantics = new MutationBoundarySemantics { IsReversible = false, IsRecoverable = true, IsExternallyPropagated = true };

        // 1. High confidence, reversible step -> Retry
        var dec1 = planner.PlanRecovery(StepStatus.Failed, 0.85, reversibleSemantics);
        Assert.Equal(RecoveryDecision.Retry, dec1);

        // 2. Reversible step, low confidence -> Rollback
        var dec2 = planner.PlanRecovery(StepStatus.Failed, 0.3, reversibleSemantics);
        Assert.Equal(RecoveryDecision.Rollback, dec2);

        // 3. Irreversible step, low confidence -> EscalateToUser
        var dec3 = planner.PlanRecovery(StepStatus.Failed, 0.35, irreversibleSemantics);
        Assert.Equal(RecoveryDecision.EscalateToUser, dec3);

        // 4. Externally propagated step -> Compensate
        var dec4 = planner.PlanRecovery(StepStatus.Failed, 0.5, externalSemantics);
        Assert.Equal(RecoveryDecision.Compensate, dec4);
    }
}
