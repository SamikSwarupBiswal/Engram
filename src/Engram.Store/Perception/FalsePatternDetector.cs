using Microsoft.Extensions.Logging;

namespace Engram.Store.Perception;

/// <summary>
/// Detects and prevents false pattern interpretation.
/// 
/// Engram can become an overfitted therapist — seeing patterns
/// that aren't there, interpreting neutral behavior as meaningful.
/// 
/// This component prevents:
/// - Research ≠ procrastination
/// - Exploration ≠ drift
/// - Context switching ≠ instability
/// - Fatigue ≠ abandonment
/// - Browsing ≠ avoidance
/// 
/// It does this by tracking interpretation patterns and flagging
/// when the system is over-interpreting (too many negative conclusions
/// from neutral signals).
/// </summary>
public class FalsePatternDetector
{
    private readonly ILogger<FalsePatternDetector>? _logger;
    private readonly InterpretationAccuracyTracker _accuracyTracker;
    private readonly List<OverinterpretationRecord> _records = new();
    private readonly object _lock = new();

    /// <summary>
    /// Minimum number of interpretations of a pattern before it can be flagged.
    /// Prevents false positives from small sample sizes.
    /// </summary>
    public int MinSampleSize { get; set; } = 5;

    /// <summary>
    /// Threshold for over-interpretation — if this percentage of interpretations
    /// for a mode are negative (incorrect/partial), flag it.
    /// </summary>
    public double OverinterpretationThreshold { get; set; } = 0.4;

    public FalsePatternDetector(
        InterpretationAccuracyTracker accuracyTracker,
        ILogger<FalsePatternDetector>? logger = null)
    {
        _accuracyTracker = accuracyTracker;
        _logger = logger;
    }

    /// <summary>
    /// Check if a mode is being over-interpreted (too many false positives).
    /// Returns null if not enough data, or a warning if over-interpretation detected.
    /// </summary>
    public OverinterpretationWarning? CheckMode(string mode)
    {
        var accuracy = _accuracyTracker.GenerateReport();
        if (!accuracy.PerModeAccuracy.TryGetValue(mode, out var modeAcc))
            return null;

        if (modeAcc.Total < MinSampleSize)
            return null;

        var errorRate = 1.0 - modeAcc.AccuracyRate;
        if (errorRate >= OverinterpretationThreshold)
        {
            var warning = new OverinterpretationWarning
            {
                Mode = mode,
                ErrorRate = errorRate,
                SampleSize = modeAcc.Total,
                IncorrectCount = modeAcc.Incorrect,
                PartialCount = modeAcc.Partial,
                Severity = errorRate >= 0.6 ? OverinterpretationSeverity.High
                    : errorRate >= 0.4 ? OverinterpretationSeverity.Medium
                    : OverinterpretationSeverity.Low,
                Recommendation = GetRecommendation(mode, errorRate),
                DetectedAt = DateTimeOffset.UtcNow
            };

            lock (_lock)
            {
                _records.Add(new OverinterpretationRecord
                {
                    Mode = mode,
                    ErrorRate = errorRate,
                    SampleSize = modeAcc.Total,
                    DetectedAt = DateTimeOffset.UtcNow
                });
            }

            _logger?.LogWarning(
                "Over-interpretation detected for mode '{Mode}': {ErrorRate:P1} error rate ({Incorrect} incorrect, {Partial} partial out of {Total})",
                mode, errorRate, modeAcc.Incorrect, modeAcc.Partial, modeAcc.Total);

            return warning;
        }

        return null;
    }

    /// <summary>
    /// Check all modes for over-interpretation.
    /// </summary>
    public List<OverinterpretationWarning> CheckAllModes()
    {
        var warnings = new List<OverinterpretationWarning>();
        var accuracy = _accuracyTracker.GenerateReport();

        foreach (var mode in accuracy.PerModeAccuracy.Keys)
        {
            var warning = CheckMode(mode);
            if (warning != null)
                warnings.Add(warning);
        }

        return warnings;
    }

    /// <summary>
    /// Record a specific over-interpretation event.
    /// Used when the system detects it's reading too much into neutral behavior.
    /// </summary>
    public void RecordOverinterpretation(string mode, string context, string reason)
    {
        lock (_lock)
        {
            _records.Add(new OverinterpretationRecord
            {
                Mode = mode,
                Context = context,
                Reason = reason,
                DetectedAt = DateTimeOffset.UtcNow
            });
        }

        _logger?.LogInformation("Over-interpretation recorded: {Mode} — {Reason}", mode, reason);
    }

    /// <summary>
    /// Get all over-interpretation records.
    /// </summary>
    public List<OverinterpretationRecord> GetRecords()
    {
        lock (_lock)
        {
            return _records.ToList();
        }
    }

    /// <summary>
    /// Get the current false pattern profile — which modes are being over-interpreted?
    /// </summary>
    public FalsePatternProfile GetProfile()
    {
        var accuracy = _accuracyTracker.GenerateReport();
        var modeWarnings = new Dictionary<string, double>();

        foreach (var (mode, modeAcc) in accuracy.PerModeAccuracy)
        {
            if (modeAcc.Total >= MinSampleSize)
            {
                var errorRate = 1.0 - modeAcc.AccuracyRate;
                if (errorRate > 0) // Only include modes with errors
                    modeWarnings[mode] = errorRate;
            }
        }

        lock (_lock)
        {
            return new FalsePatternProfile
            {
                ModesWithWarnings = modeWarnings,
                TotalOverinterpretationRecords = _records.Count,
                MostOverinterpretedMode = modeWarnings
                    .OrderByDescending(kv => kv.Value)
                    .FirstOrDefault().Key,
                GeneratedAt = DateTimeOffset.UtcNow
            };
        }
    }

    private static string GetRecommendation(string mode, double errorRate)
    {
        return mode switch
        {
            "research" when errorRate > 0.5 =>
                "Stop interpreting research as procrastination. Research is productive work. Only flag when research replaces building for extended periods.",
            "browsing" when errorRate > 0.5 =>
                "Stop interpreting browsing as avoidance. Users browse for many legitimate reasons. Only flag when browsing dominates a work session.",
            "exploration" when errorRate > 0.5 =>
                "Stop interpreting exploration as drift. Exploration builds understanding. Only flag when exploration never leads to action.",
            "context_switching" when errorRate > 0.5 =>
                "Stop interpreting context switching as instability. Many workflows require frequent switching. Only flag when switching is rapid and sustained.",
            "communication" when errorRate > 0.5 =>
                "Stop interpreting communication as distraction. Communication is work. Only flag when communication replaces deep work for extended periods.",
            _ when errorRate > 0.6 =>
                $"Mode '{mode}' has a {errorRate:P0} error rate. Consider reducing sensitivity for this interpretation.",
            _ =>
                $"Mode '{mode}' has moderate over-interpretation ({errorRate:P0}). Monitor for patterns."
        };
    }
}

/// <summary>
/// Severity of over-interpretation.
/// </summary>
public enum OverinterpretationSeverity
{
    Low,      // Minor over-interpretation, monitoring
    Medium,   // Notable pattern, should adjust sensitivity
    High      // Systematic false pattern, must correct
}

/// <summary>
/// A warning that a mode is being over-interpreted.
/// </summary>
public record OverinterpretationWarning
{
    public string Mode { get; init; } = string.Empty;
    public double ErrorRate { get; init; }
    public int SampleSize { get; init; }
    public int IncorrectCount { get; init; }
    public int PartialCount { get; init; }
    public OverinterpretationSeverity Severity { get; init; }
    public string Recommendation { get; init; } = string.Empty;
    public DateTimeOffset DetectedAt { get; init; }
}

/// <summary>
/// Record of an over-interpretation event.
/// </summary>
public record OverinterpretationRecord
{
    public string Mode { get; init; } = string.Empty;
    public double ErrorRate { get; init; }
    public int SampleSize { get; init; }
    public string? Context { get; init; }
    public string? Reason { get; init; }
    public DateTimeOffset DetectedAt { get; init; }
}

/// <summary>
/// Profile of false pattern interpretation across all modes.
/// </summary>
public record FalsePatternProfile
{
    public Dictionary<string, double> ModesWithWarnings { get; init; } = new();
    public int TotalOverinterpretationRecords { get; init; }
    public string? MostOverinterpretedMode { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
}
