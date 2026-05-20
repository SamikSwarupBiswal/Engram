using Microsoft.Extensions.Logging;

namespace Engram.Store.Perception;

/// <summary>
/// Tracks interpretation accuracy over time.
/// 
/// Records what Engram concluded vs what was actually true.
/// This is the feedback loop that prevents the semantic graph
/// from slowly diverging from reality.
/// 
/// Without accuracy tracking, Engram is a confident liar —
/// it states interpretations with authority but never checks them.
/// 
/// Data flow:
///   PerceptionEventRecorder (snapshots) → InterpretationAccuracyTracker
///   Human corrections → InterpretationAccuracyTracker
///   Accuracy reports → CognitiveRestraintEngine (silence when uncertain)
/// </summary>
public class InterpretationAccuracyTracker
{
    private readonly ILogger<InterpretationAccuracyTracker>? _logger;
    private readonly List<AccuracyRecord> _records = new();
    private readonly object _lock = new();

    public InterpretationAccuracyTracker(ILogger<InterpretationAccuracyTracker>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Record an interpretation outcome — what was the ground truth?
    /// </summary>
    public void RecordOutcome(string snapshotId, string interpretedMode, string actualMode,
        InterpretationOutcome outcome, string? correctionNote = null)
    {
        var record = new AccuracyRecord
        {
            SnapshotId = snapshotId,
            InterpretedMode = interpretedMode,
            ActualMode = actualMode,
            Outcome = outcome,
            CorrectionNote = correctionNote,
            RecordedAt = DateTimeOffset.UtcNow
        };

        lock (_lock)
        {
            _records.Add(record);
        }

        _logger?.LogDebug("Accuracy record: {SnapshotId} — {Outcome} (interpreted: {Interpreted}, actual: {Actual})",
            snapshotId, outcome, interpretedMode, actualMode);
    }

    /// <summary>
    /// Record that an interpretation was confirmed correct.
    /// </summary>
    public void RecordCorrect(string snapshotId, string mode)
    {
        RecordOutcome(snapshotId, mode, mode, InterpretationOutcome.Correct);
    }

    /// <summary>
    /// Record that an interpretation was wrong.
    /// </summary>
    public void RecordIncorrect(string snapshotId, string interpretedMode, string actualMode,
        string? note = null)
    {
        RecordOutcome(snapshotId, interpretedMode, actualMode, InterpretationOutcome.Incorrect, note);
    }

    /// <summary>
    /// Record that an interpretation was partially correct.
    /// Example: "research" when user was "studying" (related but not exact).
    /// </summary>
    public void RecordPartial(string snapshotId, string interpretedMode, string actualMode,
        string? note = null)
    {
        RecordOutcome(snapshotId, interpretedMode, actualMode, InterpretationOutcome.Partial, note);
    }

    /// <summary>
    /// Generate an accuracy report for a time period.
    /// </summary>
    public AccuracyReport GenerateReport(TimeSpan? period = null)
    {
        lock (_lock)
        {
            var records = period.HasValue
                ? _records.Where(r => r.RecordedAt >= DateTimeOffset.UtcNow - period.Value).ToList()
                : _records.ToList();

            if (records.Count == 0)
            {
                return new AccuracyReport
                {
                    Period = period,
                    TotalRecords = 0,
                    GeneratedAt = DateTimeOffset.UtcNow
                };
            }

            var correct = records.Count(r => r.Outcome == InterpretationOutcome.Correct);
            var incorrect = records.Count(r => r.Outcome == InterpretationOutcome.Incorrect);
            var partial = records.Count(r => r.Outcome == InterpretationOutcome.Partial);
            var unknown = records.Count(r => r.Outcome == InterpretationOutcome.Unknown);

            // Find systematic error patterns
            var errorPatterns = records
                .Where(r => r.Outcome == InterpretationOutcome.Incorrect)
                .GroupBy(r => $"{r.InterpretedMode}→{r.ActualMode}")
                .ToDictionary(g => g.Key, g => g.Count())
                .OrderByDescending(kv => kv.Value)
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            // Per-mode accuracy
            var perModeAccuracy = records
                .Where(r => r.Outcome != InterpretationOutcome.Unknown)
                .GroupBy(r => r.InterpretedMode)
                .ToDictionary(
                    g => g.Key,
                    g => new ModeAccuracy
                    {
                        Mode = g.Key,
                        Total = g.Count(),
                        Correct = g.Count(r => r.Outcome == InterpretationOutcome.Correct),
                        Incorrect = g.Count(r => r.Outcome == InterpretationOutcome.Incorrect),
                        Partial = g.Count(r => r.Outcome == InterpretationOutcome.Partial),
                        AccuracyRate = g.Count() > 0
                            ? (double)g.Count(r => r.Outcome == InterpretationOutcome.Correct) / g.Count()
                            : 0
                    });

            return new AccuracyReport
            {
                Period = period,
                TotalRecords = records.Count,
                CorrectCount = correct,
                IncorrectCount = incorrect,
                PartialCount = partial,
                UnknownCount = unknown,
                OverallAccuracy = records.Count > 0
                    ? (double)correct / records.Count
                    : 0,
                ErrorPatterns = errorPatterns,
                PerModeAccuracy = perModeAccuracy,
                GeneratedAt = DateTimeOffset.UtcNow
            };
        }
    }

    /// <summary>
    /// Get all records (for external analysis).
    /// </summary>
    public List<AccuracyRecord> GetAllRecords()
    {
        lock (_lock)
        {
            return _records.ToList();
        }
    }

    /// <summary>
    /// Get accuracy rate for a specific mode.
    /// </summary>
    public double GetModeAccuracy(string mode)
    {
        lock (_lock)
        {
            var modeRecords = _records.Where(r => r.InterpretedMode == mode).ToList();
            if (modeRecords.Count == 0) return -1; // No data
            return (double)modeRecords.Count(r => r.Outcome == InterpretationOutcome.Correct) / modeRecords.Count;
        }
    }
}

/// <summary>
/// The outcome of an interpretation.
/// </summary>
public enum InterpretationOutcome
{
    /// <summary>Interpretation was correct.</summary>
    Correct,

    /// <summary>Interpretation was incorrect.</summary>
    Incorrect,

    /// <summary>Interpretation was partially correct (related but not exact).</summary>
    Partial,

    /// <summary>Outcome is unknown — not yet validated.</summary>
    Unknown
}

/// <summary>
/// A single accuracy record.
/// </summary>
public record AccuracyRecord
{
    public string SnapshotId { get; init; } = string.Empty;
    public string InterpretedMode { get; init; } = string.Empty;
    public string ActualMode { get; init; } = string.Empty;
    public InterpretationOutcome Outcome { get; init; }
    public string? CorrectionNote { get; init; }
    public DateTimeOffset RecordedAt { get; init; }
}

/// <summary>
/// Summary report of interpretation accuracy.
/// </summary>
public record AccuracyReport
{
    public TimeSpan? Period { get; init; }
    public int TotalRecords { get; init; }
    public int CorrectCount { get; init; }
    public int IncorrectCount { get; init; }
    public int PartialCount { get; init; }
    public int UnknownCount { get; init; }
    public double OverallAccuracy { get; init; }

    /// <summary>Error patterns: "interpreted→actual" → count.</summary>
    public Dictionary<string, int> ErrorPatterns { get; init; } = new();

    /// <summary>Per-mode accuracy breakdown.</summary>
    public Dictionary<string, ModeAccuracy> PerModeAccuracy { get; init; } = new();

    public DateTimeOffset GeneratedAt { get; init; }
}

/// <summary>
/// Accuracy metrics for a specific behavioral mode.
/// </summary>
public record ModeAccuracy
{
    public string Mode { get; init; } = string.Empty;
    public int Total { get; init; }
    public int Correct { get; init; }
    public int Incorrect { get; init; }
    public int Partial { get; init; }
    public double AccuracyRate { get; init; }
}
