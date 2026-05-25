using System;

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
}
