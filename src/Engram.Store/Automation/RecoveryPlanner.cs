using System;

namespace Engram.Store.Automation;

public enum RecoveryDecision
{
    Retry,
    Rollback,
    Compensate,
    Suspend,
    Yield,
    ReVerify,
    EscalateToUser
}

public class RecoveryPlanner
{
    public RecoveryDecision PlanRecovery(
        StepStatus stepStatus, 
        double currentConfidence, 
        MutationBoundarySemantics stepSemantics,
        bool userInterruptionDetected = false)
    {
        if (stepSemantics == null)
        {
            return RecoveryDecision.EscalateToUser;
        }

        if (userInterruptionDetected)
        {
            return RecoveryDecision.Yield;
        }

        if (currentConfidence < 0.2)
        {
            // Epistemic honesty: Too low confidence, must escalate or rollback
            if (stepSemantics.IsExternallyPropagated)
            {
                return RecoveryDecision.Compensate;
            }
            return stepSemantics.IsReversible ? RecoveryDecision.Rollback : RecoveryDecision.EscalateToUser;
        }

        if (stepStatus == StepStatus.Failed)
        {
            if (stepSemantics.IsExternallyPropagated)
            {
                // Cannot rollback external events, must compensate
                return stepSemantics.IsRecoverable ? RecoveryDecision.Compensate : RecoveryDecision.EscalateToUser;
            }

            if (stepSemantics.IsIrreversible)
            {
                // Irreversible local mutations must escalate to user
                return RecoveryDecision.EscalateToUser;
            }

            if (currentConfidence >= 0.7 && stepSemantics.IsRecoverable)
            {
                return RecoveryDecision.Retry;
            }

            return stepSemantics.IsReversible ? RecoveryDecision.Rollback : RecoveryDecision.EscalateToUser;
        }

        return RecoveryDecision.ReVerify;
    }
}
