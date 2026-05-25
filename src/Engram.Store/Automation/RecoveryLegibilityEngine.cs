using System;
using System.Collections.Generic;
using System.Linq;

namespace Engram.Store.Automation;

/// <summary>
/// RecoveryLegibilityEngine — translates technical exceptions (Playwright timeouts, Win32 errors)
/// into operationally neutral, objective statements.
/// Avoids emotional, humanizing language to preserve professional trust boundaries.
/// </summary>
public class RecoveryLegibilityEngine
{
    public string TranslateFailure(string errorMessage, string exceptionDetails)
    {
        var msg = (errorMessage + " " + exceptionDetails).ToLowerInvariant();

        if (msg.Contains("desynchronization") || msg.Contains("desynchronized"))
        {
            return "The task paused because the expected application state could no longer be confirmed.";
        }

        if (msg.Contains("fatigue") || msg.Contains("epistemic debt") || msg.Contains("decay factor"))
        {
            return "Too many verification mismatches accumulated during execution.";
        }

        if (msg.Contains("compensation") || msg.Contains("irrecoverable") || msg.Contains("propagation"))
        {
            return "The environment changed in a way that made the workflow unsafe to continue.";
        }

        if (msg.Contains("drift") || msg.Contains("recalibration") || msg.Contains("degraded confidence"))
        {
            return "Systemic platform drift prevented execution continuity.";
        }

        if (msg.Contains("playwright") || msg.Contains("browser") || msg.Contains("page") || msg.Contains("selector"))
        {
            if (msg.Contains("timeout"))
            {
                return "The operation exceeded the scheduled time window.";
            }
            return "The browser environment changed unexpectedly.";
        }

        if (msg.Contains("verification") || msg.Contains("assert") || msg.Contains("confidence") || msg.Contains("verify"))
        {
            return "Verification confidence dropped below safe threshold.";
        }

        if (msg.Contains("process") || msg.Contains("window") || msg.Contains("focus") || msg.Contains("active"))
        {
            return "The target application no longer matched the expected state.";
        }

        if (msg.Contains("permission") || msg.Contains("access") || msg.Contains("denied") || msg.Contains("unauthorized"))
        {
            return "Permissions constraints prevented target mutation.";
        }

        if (msg.Contains("timeout") || msg.Contains("time limit"))
        {
            return "The operation exceeded the scheduled time window.";
        }

        return "An unexpected operational divergence occurred.";
    }

    public string TranslateRecovery(bool success)
    {
        return success 
            ? "Operational alignment was successfully restored." 
            : "Automatic recovery was unable to resolve the divergence.";
    }

    public ExplainabilityReport GenerateReport(string errorMessage, string exceptionDetails, ActionRuntime? runtime = null)
    {
        var calm = TranslateFailure(errorMessage, exceptionDetails);
        var msg = (errorMessage + " " + exceptionDetails).ToLowerInvariant();
        
        // Tier 2: Operational Detail
        var detail = "The system encountered a verification or environment boundary.";
        if (msg.Contains("desynchronization") || msg.Contains("desynchronized"))
        {
            detail = "Divergence detected between the expected application state and the actual OS window focus or browser state.";
        }
        else if (msg.Contains("fatigue") || msg.Contains("epistemic debt") || msg.Contains("decay factor"))
        {
            detail = "Execution suspended due to consecutive verification failures crossing the epistemic safety limit.";
        }
        else if (msg.Contains("compensation") || msg.Contains("irrecoverable") || msg.Contains("propagation"))
        {
            detail = "An externally propagated action (such as a file upload or notification) failed and could not be safely rolled back.";
        }
        else if (msg.Contains("drift") || msg.Contains("recalibration") || msg.Contains("degraded confidence"))
        {
            detail = "The target application scope drifted from historical metrics, triggering a recalibration check.";
        }

        // Tier 3: Causal Trace
        var trace = errorMessage;
        if (!string.IsNullOrEmpty(exceptionDetails))
        {
            trace += "\nTrace: " + exceptionDetails.Split('\n')[0];
        }

        // Tier 4: Full Epistemic Graph / Internal Diagnostics
        var graph = "Entropy State: [Unknown]\nUncertainty Level: [Unknown]";
        if (runtime != null)
        {
            var envelope = runtime.IdentityEnvelope;
            var unresolvedPropagation = 0;
            if (runtime.PropagationLedger != null)
            {
                unresolvedPropagation = runtime.PropagationLedger.GetRecords().FindAll(r => r.Status == "Uncertain" || r.Status == "Failed").Count;
            }
            int humanCollisionCount = runtime.ActiveContext?.GetVariable<int>("HumanCollisionCount") ?? 0;
            double driftIndex = 0.0;
            if (runtime.DriftCorrelationEngine != null)
            {
                var appScope = runtime.ActiveContext?.GetVariable<string>("AppName") ?? "Default";
                driftIndex = 1.0 - runtime.DriftCorrelationEngine.GetScopeConfidence(appScope);
            }

            var planSteps = runtime.ActivePlan?.Steps;
            var elapsedPlanTime = planSteps != null && planSteps.Count > 0
                ? DateTimeOffset.UtcNow - planSteps.Values.Min(s => s.StartedAt ?? DateTimeOffset.UtcNow)
                : TimeSpan.Zero;
            var completedCount = planSteps != null ? planSteps.Values.Count(s => s.Status == StepStatus.Completed) : 0;

            var fatigueVector = runtime.TemporalDegradationModel?.ComputeFatigueVector(
                runtime.ActivePlan?.PlanId ?? "default",
                elapsedPlanTime,
                completedCount,
                envelope?.UncertaintyLog,
                unresolvedPropagation,
                humanCollisionCount,
                driftIndex
            );

            if (fatigueVector != null)
            {
                graph = $"TemporalEntropy: {fatigueVector.TemporalEntropy:F2}, " +
                        $"VerificationErosion: {fatigueVector.VerificationErosion:F2}, " +
                        $"EnvironmentalInstability: {fatigueVector.EnvironmentalInstability:F2}, " +
                        $"PropagationAmbiguity: {fatigueVector.PropagationAmbiguity:F2}, " +
                        $"HumanCollisionDensity: {fatigueVector.HumanCollisionDensity:F2}, " +
                        $"ProceduralDivergence: {fatigueVector.ProceduralDivergence:F2}, " +
                        $"AggregateFatigueIndex: {fatigueVector.GetAggregateFatigueIndex():F2}";
            }
        }

        return new ExplainabilityReport
        {
            CalmSummary = calm,
            OperationalDetail = detail,
            CausalTrace = trace,
            FullEpistemicGraph = graph
        };
    }
}

public class ExplainabilityReport
{
    public string CalmSummary { get; set; } = string.Empty;
    public string OperationalDetail { get; set; } = string.Empty;
    public string CausalTrace { get; set; } = string.Empty;
    public string FullEpistemicGraph { get; set; } = string.Empty;
}
