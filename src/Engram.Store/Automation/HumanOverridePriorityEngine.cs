using System;

namespace Engram.Store.Automation;

public enum CooperativeDecision
{
    Continue,
    Yield,       // Short pause, yield foreground
    Suspend,     // Medium pause, save checkpoint
    Abort,       // Explicit cancel
    Reconcile,   // Conflicting action
    Terminate    // Destructive divergence
}

public class HumanOverridePriorityEngine
{
    private readonly SovereigntyMonitor _sovereigntyMonitor;
    private readonly ExecutionSafetyManager _safetyManager;
    private readonly CoexistenceMetricsTracker? _metricsTracker;

    private DateTimeOffset? _interruptionStart;
    private bool _hasAborted;

    public HumanOverridePriorityEngine(
        SovereigntyMonitor sovereigntyMonitor,
        ExecutionSafetyManager safetyManager,
        CoexistenceMetricsTracker? metricsTracker = null)
    {
        _sovereigntyMonitor = sovereigntyMonitor ?? throw new ArgumentNullException(nameof(sovereigntyMonitor));
        _safetyManager = safetyManager ?? throw new ArgumentNullException(nameof(safetyManager));
        _metricsTracker = metricsTracker;
    }

    public CooperativeDecision EvaluateControlTransfer(
        string workflowId,
        string activeProcessName,
        string activeWindowTitle,
        bool isExplicitCancel = false,
        bool hasDestructiveDivergence = false)
    {
        if (isExplicitCancel)
        {
            _metricsTracker?.RecordInterruption(isCancel: true);
            return CooperativeDecision.Abort;
        }

        if (hasDestructiveDivergence)
        {
            _metricsTracker?.RecordInterruption(isCancel: true);
            return CooperativeDecision.Terminate;
        }

        // 1. Detect user activity
        bool userActive = _sovereigntyMonitor.DetectUserActivity();

        // 2. Check mouse drift failsafe
        bool mouseOverridden = false;
        try
        {
            _safetyManager.VerifyMouseFailsafe();
        }
        catch (InvalidOperationException)
        {
            mouseOverridden = true;
            userActive = true;
        }

        // 3. Process focus change
        bool focusChanged = false;
        if (!string.IsNullOrEmpty(activeProcessName))
        {
            try
            {
                _safetyManager.VerifyProcessSafety(activeProcessName, activeWindowTitle);
            }
            catch (InvalidOperationException)
            {
                // Privilege escalation or blacklisted process means immediate override
                focusChanged = true;
                userActive = true;
            }
        }

        if (!userActive && !focusChanged)
        {
            _interruptionStart = null;
            return CooperativeDecision.Continue;
        }

        // We have active user interruption
        var now = DateTimeOffset.UtcNow;
        if (_interruptionStart == null)
        {
            _interruptionStart = now;
        }

        var duration = now - _interruptionStart.Value;

        // Cooperative decision timing based on recommended D4 model
        CooperativeDecision decision;
        if (focusChanged)
        {
            // Conflicting action/process focus hijacking
            decision = CooperativeDecision.Reconcile;
            _metricsTracker?.RecordInterruption(isCancel: false);
        }
        else if (duration <= TimeSpan.FromSeconds(5))
        {
            // Short interruption: silent yield
            decision = CooperativeDecision.Yield;
            _metricsTracker?.RecordSilence(duration.TotalSeconds);
        }
        else if (duration <= TimeSpan.FromSeconds(30))
        {
            // Medium interruption: suspend
            decision = CooperativeDecision.Suspend;
            _metricsTracker?.RecordInterruption(isCancel: false);
        }
        else
        {
            // Long interruption/Explicit contradiction
            decision = CooperativeDecision.Suspend; // low-priority resume
        }

        return decision;
    }

    public void Reset()
    {
        _interruptionStart = null;
        _hasAborted = false;
    }
}
