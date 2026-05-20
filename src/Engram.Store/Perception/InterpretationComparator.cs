using Microsoft.Extensions.Logging;

namespace Engram.Store.Perception;

/// <summary>
/// Compares interpretation sets to find divergences, patterns, and accuracy.
/// 
/// Used for:
/// - Comparing replay results against original interpretations
/// - Comparing two strategies against the same input stream
/// - Finding systematic patterns in interpretation errors
/// - Generating accuracy reports
/// </summary>
public class InterpretationComparator
{
    private readonly ILogger<InterpretationComparator>? _logger;

    public InterpretationComparator(ILogger<InterpretationComparator>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Compare two sets of replay results (e.g., strategy A vs strategy B).
    /// </summary>
    public ComparisonReport Compare(
        List<ReplayResult> setA,
        List<ReplayResult> setB,
        string labelA = "Strategy A",
        string labelB = "Strategy B")
    {
        var divergences = new List<ModeDivergence>();
        var totalCompared = Math.Min(setA.Count, setB.Count);

        for (int i = 0; i < totalCompared; i++)
        {
            var a = setA[i];
            var b = setB[i];

            if (a.ReplayedMode != b.ReplayedMode)
            {
                divergences.Add(new ModeDivergence
                {
                    SnapshotId = a.OriginalSnapshotId,
                    ModeA = a.ReplayedMode,
                    ModeB = b.ReplayedMode,
                    Input = a.Input,
                    Timestamp = a.Timestamp
                });
            }
        }

        // Find systematic patterns
        var patternCounts = divergences
            .GroupBy(d => $"{d.ModeA}→{d.ModeB}")
            .ToDictionary(g => g.Key, g => g.Count());

        var report = new ComparisonReport
        {
            LabelA = labelA,
            LabelB = labelB,
            TotalCompared = totalCompared,
            DivergenceCount = divergences.Count,
            DivergenceRate = totalCompared > 0 ? (double)divergences.Count / totalCompared : 0,
            Divergences = divergences,
            SystematicPatterns = patternCounts
        };

        _logger?.LogInformation(
            "Comparison {A} vs {B}: {Divergences}/{Total} divergences ({Rate:P1})",
            labelA, labelB, divergences.Count, totalCompared, report.DivergenceRate);

        return report;
    }

    /// <summary>
    /// Compare replay results against the original recordings.
    /// Tests determinism — does the same strategy produce the same results?
    /// </summary>
    public ComparisonReport CompareAgainstOriginal(List<ReplayResult> replayResults)
    {
        var divergences = new List<ModeDivergence>();

        foreach (var result in replayResults.Where(r => r.HasDivergence))
        {
            divergences.Add(new ModeDivergence
            {
                SnapshotId = result.OriginalSnapshotId,
                ModeA = result.OriginalMode,
                ModeB = result.ReplayedMode,
                Input = result.Input,
                Timestamp = result.Timestamp
            });
        }

        var patternCounts = divergences
            .GroupBy(d => $"{d.ModeA}→{d.ModeB}")
            .ToDictionary(g => g.Key, g => g.Count());

        return new ComparisonReport
        {
            LabelA = "Original",
            LabelB = "Replayed",
            TotalCompared = replayResults.Count,
            DivergenceCount = divergences.Count,
            DivergenceRate = replayResults.Count > 0 ? (double)divergences.Count / replayResults.Count : 0,
            Divergences = divergences,
            SystematicPatterns = patternCounts
        };
    }

    /// <summary>
    /// Find the most common misinterpretation patterns.
    /// Returns patterns like "research → browsing" (systematic over-classification).
    /// </summary>
    public List<MisinterpretationPattern> FindSystematicErrors(
        List<ReplayResult> replayResults,
        Dictionary<string, string> groundTruth)
    {
        var errors = new List<MisinterpretationPattern>();

        foreach (var result in replayResults)
        {
            if (!groundTruth.TryGetValue(result.OriginalSnapshotId, out var expected))
                continue;

            if (result.ReplayedMode != expected)
            {
                errors.Add(new MisinterpretationPattern
                {
                    ExpectedMode = expected,
                    ActualMode = result.ReplayedMode,
                    ProcessName = result.Input.ProcessName,
                    WindowTitle = result.Input.WindowTitle,
                    FocusDuration = result.Input.FocusDuration
                });
            }
        }

        // Group by pattern and find systematic ones (appearing 2+ times)
        return errors
            .GroupBy(e => $"{e.ExpectedMode}→{e.ActualMode}")
            .Where(g => g.Count() >= 2)
            .Select(g => new MisinterpretationPattern
            {
                ExpectedMode = g.First().ExpectedMode,
                ActualMode = g.First().ActualMode,
                OccurrenceCount = g.Count(),
                ExampleProcess = g.First().ProcessName,
                ExampleTitle = g.First().WindowTitle
            })
            .OrderByDescending(p => p.OccurrenceCount)
            .ToList();
    }
}

/// <summary>
/// Report comparing two interpretation sets.
/// </summary>
public record ComparisonReport
{
    public string LabelA { get; init; } = string.Empty;
    public string LabelB { get; init; } = string.Empty;
    public int TotalCompared { get; init; }
    public int DivergenceCount { get; init; }
    public double DivergenceRate { get; init; }
    public List<ModeDivergence> Divergences { get; init; } = new();
    public Dictionary<string, int> SystematicPatterns { get; init; } = new();
}

/// <summary>
/// A single divergence between two modes.
/// </summary>
public record ModeDivergence
{
    public string SnapshotId { get; init; } = string.Empty;
    public string ModeA { get; init; } = string.Empty;
    public string ModeB { get; init; } = string.Empty;
    public PerceptionInput Input { get; init; } = new();
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// A systematic misinterpretation pattern.
/// </summary>
public record MisinterpretationPattern
{
    public string ExpectedMode { get; init; } = string.Empty;
    public string ActualMode { get; init; } = string.Empty;
    public string ProcessName { get; init; } = string.Empty;
    public string WindowTitle { get; init; } = string.Empty;
    public TimeSpan FocusDuration { get; init; }
    public int OccurrenceCount { get; init; }
    public string ExampleProcess { get; init; } = string.Empty;
    public string ExampleTitle { get; init; } = string.Empty;
}
