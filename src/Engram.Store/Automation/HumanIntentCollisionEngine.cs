using System;
using System.Threading;
using System.Threading.Tasks;

namespace Engram.Store.Automation;

public class HumanIntentCollisionEngine
{
    private readonly HumanOverridePriorityEngine _overrideEngine;
    private readonly SovereigntyMonitor _sovereigntyMonitor;

    public HumanIntentCollisionEngine(HumanOverridePriorityEngine overrideEngine, SovereigntyMonitor sovereigntyMonitor)
    {
        _overrideEngine = overrideEngine ?? throw new ArgumentNullException(nameof(overrideEngine));
        _sovereigntyMonitor = sovereigntyMonitor ?? throw new ArgumentNullException(nameof(sovereigntyMonitor));
    }

    public async Task<CooperativeDecision> AssessCollisionAsync(
        string workflowId, 
        string activeProcess, 
        string activeTitle, 
        bool isExplicitCancel = false, 
        bool hasConflict = false,
        CancellationToken ct = default)
    {
        // Leverage existing override engine logic
        var decision = _overrideEngine.EvaluateControlTransfer(
            workflowId, 
            activeProcess, 
            activeTitle, 
            isExplicitCancel, 
            hasConflict
        );

        if (decision == CooperativeDecision.Continue)
        {
            // Perform additional checks for manual actions or multitasking collisions
            bool userActive = _sovereigntyMonitor.DetectUserActivity();
            if (userActive)
            {
                return CooperativeDecision.Yield;
            }
        }

        return decision;
    }
}
