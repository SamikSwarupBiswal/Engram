using Microsoft.Extensions.Logging;

namespace Engram.Store.Perception;

/// <summary>
/// Replays recorded perception snapshots through any behavioral mode strategy.
/// 
/// This is the scientific method for Engram's perception layer.
/// 
/// Given the same inputs, does a different strategy produce different interpretations?
/// Given the same strategy, do the same inputs produce the same interpretations?
/// 
/// Replay enables:
/// - Deterministic testing of interpretation logic
/// - A/B comparison of strategies
/// - Regression detection when strategy logic changes
/// - Validation that interpretations match expected ground truth
/// </summary>
public class PerceptionReplayEngine
{
    private readonly ILogger<PerceptionReplayEngine>? _logger;

    public PerceptionReplayEngine(ILogger<PerceptionReplayEngine>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Replay a sequence of snapshots through a strategy.
    /// Returns new interpretations without modifying originals.
    /// </summary>
    public List<ReplayResult> Replay(
        IReadOnlyList<PerceptionSnapshot> snapshots,
        IBehavioralModeStrategy strategy)
    {
        var results = new List<ReplayResult>(snapshots.Count);

        foreach (var snapshot in snapshots)
        {
            var replayedMode = strategy.DetectMode(
                snapshot.Input.ProcessName,
                snapshot.Input.WindowTitle,
                snapshot.Input.FocusDuration);

            results.Add(new ReplayResult
            {
                OriginalSnapshotId = snapshot.SnapshotId,
                OriginalMode = snapshot.Interpretation.BehavioralMode,
                ReplayedMode = replayedMode,
                Input = snapshot.Input,
                Timestamp = snapshot.Timestamp,
                SequenceNumber = snapshot.SequenceNumber
            });
        }

        _logger?.LogInformation(
            "Replayed {Count} snapshots through {Strategy}. {Divergences} divergences found.",
            snapshots.Count,
            strategy.GetType().Name,
            results.Count(r => r.HasDivergence));

        return results;
    }

    /// <summary>
    /// Replay through multiple strategies and compare.
    /// Enables A/B testing of interpretation logic.
    /// </summary>
    public Dictionary<string, List<ReplayResult>> ReplayComparison(
        IReadOnlyList<PerceptionSnapshot> snapshots,
        Dictionary<string, IBehavioralModeStrategy> strategies)
    {
        var results = new Dictionary<string, List<ReplayResult>>();

        foreach (var (name, strategy) in strategies)
        {
            results[name] = Replay(snapshots, strategy);
        }

        return results;
    }

    /// <summary>
    /// Replay and compare against expected ground truth.
    /// Returns only divergences (where interpretation differs from expected).
    /// </summary>
    public List<GroundTruthDivergence> ReplayAgainstGroundTruth(
        IReadOnlyList<PerceptionSnapshot> snapshots,
        IBehavioralModeStrategy strategy,
        Dictionary<string, string> groundTruth)
    {
        var divergences = new List<GroundTruthDivergence>();

        foreach (var snapshot in snapshots)
        {
            if (!groundTruth.TryGetValue(snapshot.SnapshotId, out var expectedMode))
                continue;

            var replayedMode = strategy.DetectMode(
                snapshot.Input.ProcessName,
                snapshot.Input.WindowTitle,
                snapshot.Input.FocusDuration);

            if (replayedMode != expectedMode)
            {
                divergences.Add(new GroundTruthDivergence
                {
                    SnapshotId = snapshot.SnapshotId,
                    ExpectedMode = expectedMode,
                    ActualMode = replayedMode,
                    Input = snapshot.Input,
                    Timestamp = snapshot.Timestamp
                });
            }
        }

        _logger?.LogInformation(
            "Ground truth check: {Divergences}/{Total} divergences",
            divergences.Count, snapshots.Count);

        return divergences;
    }
}

/// <summary>
/// Result of replaying a single snapshot through a strategy.
/// </summary>
public record ReplayResult
{
    public string OriginalSnapshotId { get; init; } = string.Empty;
    public string OriginalMode { get; init; } = string.Empty;
    public string ReplayedMode { get; init; } = string.Empty;
    public PerceptionInput Input { get; init; } = new();
    public DateTimeOffset Timestamp { get; init; }
    public long SequenceNumber { get; init; }

    /// <summary>Whether the replayed interpretation differs from the original.</summary>
    public bool HasDivergence => OriginalMode != ReplayedMode;
}

/// <summary>
/// A divergence between interpretation and ground truth.
/// </summary>
public record GroundTruthDivergence
{
    public string SnapshotId { get; init; } = string.Empty;
    public string ExpectedMode { get; init; } = string.Empty;
    public string ActualMode { get; init; } = string.Empty;
    public PerceptionInput Input { get; init; } = new();
    public DateTimeOffset Timestamp { get; init; }
}
