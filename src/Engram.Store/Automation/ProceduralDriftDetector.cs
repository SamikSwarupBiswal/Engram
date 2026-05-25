using System;

namespace Engram.Store.Automation;

public class ProceduralDriftDetector
{
    private readonly ProceduralExperienceStore _store;

    public ProceduralDriftDetector(ProceduralExperienceStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// Analyzes the current execution step metrics and returns the detected uncertainty level (U1/U2) or null if no drift.
    /// </summary>
    public UncertaintyLevel? DetectDrift(
        string app, 
        string version, 
        ActionType type, 
        string selector, 
        TimeSpan duration, 
        bool success,
        string? activeWindowTitle,
        out string? reason)
    {
        reason = null;

        var entry = _store.GetEntry(app, version, type, selector);
        if (entry == null)
        {
            return null; // No history, so no drift can be detected
        }

        // 1. Action duration exceeds 3 standard deviations of historical experience (minimum 3 samples to avoid false positives)
        if (success && entry.SuccessCount >= 3 && entry.StandardDeviationMs > 0)
        {
            var diff = Math.Abs(duration.TotalMilliseconds - entry.AverageDurationMs);
            var threshold = 3 * entry.StandardDeviationMs;
            // Let's set a minimum threshold of 200ms to avoid flagging tiny normal variations
            if (diff > Math.Max(200, threshold))
            {
                reason = $"Action duration {duration.TotalMilliseconds:F1}ms exceeds historical average {entry.AverageDurationMs:F1}ms by more than 3 standard deviations (stdDev = {entry.StandardDeviationMs:F1}ms).";
                return UncertaintyLevel.U1_Observational; // U1 triggers retry/re-verify
            }
        }

        // 2. A historically safe selector suddenly fails multiple times (consecutive failures >= 2)
        if (!success)
        {
            var totalCount = entry.SuccessCount + entry.FailureCount;
            var historicalSuccessRate = totalCount > 0 ? (double)entry.SuccessCount / totalCount : 0.0;
            if (entry.SuccessCount >= 5 && historicalSuccessRate > 0.9 && entry.ConsecutiveFailures >= 2)
            {
                reason = $"Historically safe selector '{selector}' has failed {entry.ConsecutiveFailures} times consecutively.";
                return UncertaintyLevel.U2_StateAmbiguity; // State is ambiguous because historically safe selector is failing
            }
        }

        // 3. Unexpected modal pattern is detected
        if (!string.IsNullOrEmpty(activeWindowTitle) && entry.SuccessCount >= 3)
        {
            if (entry.SeenModals.Count > 0)
            {
                bool seenBefore = false;
                foreach (var title in entry.SeenModals)
                {
                    if (activeWindowTitle.Contains(title, StringComparison.OrdinalIgnoreCase) ||
                        title.Contains(activeWindowTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        seenBefore = true;
                        break;
                    }
                }

                if (!seenBefore)
                {
                    reason = $"Unexpected modal/window '{activeWindowTitle}' detected during action, which deviates from historical pattern.";
                    return UncertaintyLevel.U2_StateAmbiguity;
                }
            }
        }

        return null;
    }
}
