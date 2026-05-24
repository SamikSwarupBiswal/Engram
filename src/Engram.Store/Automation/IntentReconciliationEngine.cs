using System;

namespace Engram.Store.Automation;

public class IntentReconciliationEngine
{
    private readonly double _reconciliationThreshold = 0.4;
    private double _intentConfidence = 1.0;
    private DateTimeOffset? _deviationStart;
    private int _deviationAccumulator = 0;

    public double IntentConfidence => _intentConfidence;

    public IntentReconciliationEngine(double reconciliationThreshold = 0.4)
    {
        _reconciliationThreshold = reconciliationThreshold;
    }

    /// <summary>
    /// Evaluates if user activity indicates intent drift.
    /// </summary>
    /// <param name="activeGoal">The active automation goal.</param>
    /// <param name="activeWindowProcess">The active process name.</param>
    /// <param name="activeWindowTitle">The active window title.</param>
    /// <returns>True if reconciliation is required.</returns>
    public bool EvaluateIntentDrift(string activeGoal, string activeWindowProcess, string activeWindowTitle)
    {
        if (string.IsNullOrEmpty(activeGoal)) return false;

        bool isDivergent = DetectDivergence(activeGoal, activeWindowProcess, activeWindowTitle);

        if (isDivergent)
        {
            var now = DateTimeOffset.UtcNow;
            if (_deviationStart == null)
            {
                _deviationStart = now;
            }

            var deviationDuration = now - _deviationStart.Value;

            // Intent Confidence Hysteresis & Persistence Window
            // Single brief Alt-Tab or inspection of notifications (e.g. < 15 seconds) does NOT trigger drift.
            if (deviationDuration > TimeSpan.FromSeconds(15))
            {
                _deviationAccumulator++;
                // Decay confidence based on continued divergence
                _intentConfidence = Math.Max(0.0, _intentConfidence - 0.15);
            }
            else
            {
                // Ambiguity tolerance: keep confidence intermediate, do not collapse instantly
                _intentConfidence = Math.Max(0.6, _intentConfidence - 0.02);
            }
        }
        else
        {
            // Recover confidence when user is back on track or idle
            _deviationStart = null;
            if (_deviationAccumulator > 0) _deviationAccumulator--;
            _intentConfidence = Math.Min(1.0, _intentConfidence + 0.1);
        }

        return ShouldReconcile();
    }

    /// <summary>
    /// Adjusts confidence when user interacts with chat mid-execution.
    /// </summary>
    public void HandleChatInteraction(string activeGoal, string newIntentType)
    {
        // If user sends a different command/intent while a goal is executing, confidence drops
        if (!string.IsNullOrEmpty(newIntentType) && 
            !newIntentType.Equals("Conversational", StringComparison.OrdinalIgnoreCase))
        {
            _intentConfidence = Math.Max(0.0, _intentConfidence - 0.35);
        }
        else
        {
            // Simple conversational chit-chat is allowed and doesn't heavily penalize
            _intentConfidence = Math.Max(0.5, _intentConfidence - 0.05);
        }
    }

    public bool ShouldReconcile()
    {
        // Gating threshold: only trigger when confidence drops below 0.4
        return _intentConfidence < _reconciliationThreshold;
    }

    private static bool DetectDivergence(string goal, string processName, string windowTitle)
    {
        if (string.IsNullOrEmpty(processName)) return false;

        // If the process is a browser or explorer, or matches keywords in the goal, it's NOT divergent.
        bool matchesGoal = goal.Contains(processName, StringComparison.OrdinalIgnoreCase) ||
                           (!string.IsNullOrEmpty(windowTitle) && windowTitle.Contains(processName, StringComparison.OrdinalIgnoreCase));

        bool isBrowser = processName.Contains("chrome", StringComparison.OrdinalIgnoreCase) ||
                         processName.Contains("edge", StringComparison.OrdinalIgnoreCase) ||
                         processName.Contains("firefox", StringComparison.OrdinalIgnoreCase) ||
                         processName.Contains("playwright", StringComparison.OrdinalIgnoreCase);

        // General productivity apps are tolerated
        if (isBrowser || matchesGoal)
        {
            return false;
        }

        // Unrelated applications like games, social media, or system tools indicate potential divergence
        bool isUnrelated = processName.Contains("discord", StringComparison.OrdinalIgnoreCase) ||
                            processName.Contains("steam", StringComparison.OrdinalIgnoreCase) ||
                            processName.Contains("spotify", StringComparison.OrdinalIgnoreCase) ||
                            processName.Contains("netflix", StringComparison.OrdinalIgnoreCase);

        return isUnrelated;
    }

    public void Reset()
    {
        _intentConfidence = 1.0;
        _deviationStart = null;
        _deviationAccumulator = 0;
    }
}
