namespace Engram.Store.Perception;

/// <summary>
/// Strategy interface for behavioral mode detection.
/// 
/// Extracted from EnvironmentModel to enable:
/// - Replay: inject different strategies to compare interpretations
/// - Testing: mock strategies for deterministic behavior
/// - Evolution: swap detection logic without changing the model
/// 
/// This is the architectural key to Phase 7.
/// Without injectable detection, perception is untestable.
/// </summary>
public interface IBehavioralModeStrategy
{
    /// <summary>
    /// Determine the behavioral mode from window context.
    /// Returns a mode string (e.g., "deep_work", "research", "browsing").
    /// </summary>
    string DetectMode(string processName, string windowTitle, TimeSpan focusDuration);
}

/// <summary>
/// Default behavioral mode detection — the Phase 6 string-match logic.
/// Symbolic, not situational. This is the baseline that Phase 7 validates against.
/// </summary>
public class DefaultBehavioralModeStrategy : IBehavioralModeStrategy
{
    public string DetectMode(string processName, string windowTitle, TimeSpan focusDuration)
    {
        var processLower = processName.ToLowerInvariant();
        var titleLower = windowTitle.ToLowerInvariant();

        // Deep work: long focus on code/editor
        if (focusDuration.TotalMinutes > 10 &&
            (processLower.Contains("code") || processLower.Contains("visual studio") ||
             processLower.Contains("intellij") || processLower.Contains("vim")))
        {
            return "deep_work";
        }

        // Research: browser with research-related titles
        if (processLower.Contains("chrome") || processLower.Contains("firefox") || processLower.Contains("edge"))
        {
            if (titleLower.Contains("search") || titleLower.Contains("stackoverflow") ||
                titleLower.Contains("github") || titleLower.Contains("documentation") ||
                titleLower.Contains("mdn") || titleLower.Contains("wiki"))
            {
                return "research";
            }
            return "browsing";
        }

        // Communication
        if (processLower.Contains("slack") || processLower.Contains("teams") ||
            processLower.Contains("discord") || processLower.Contains("outlook"))
        {
            return "communication";
        }

        // Terminal/CLI work
        if (processLower.Contains("terminal") || processLower.Contains("cmd") ||
            processLower.Contains("powershell") || processLower.Contains("wt"))
        {
            return "terminal_work";
        }

        // Default: context switching or exploration
        return "exploration";
    }
}

/// <summary>
/// A recorded snapshot of a perception event — the input, the interpretation, and context.
/// Immutable. Used for replay, comparison, and accuracy tracking.
/// </summary>
public record PerceptionSnapshot
{
    /// <summary>Unique snapshot ID.</summary>
    public string SnapshotId { get; init; } = Guid.NewGuid().ToString("n")[..12];

    /// <summary>When this perception occurred.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>The raw input — what actually happened.</summary>
    public required PerceptionInput Input { get; init; }

    /// <summary>The interpretation — what Engram concluded.</summary>
    public required PerceptionInterpretation Interpretation { get; init; }

    /// <summary>Which strategy produced this interpretation.</summary>
    public string StrategyName { get; init; } = string.Empty;

    /// <summary>Sequence number in the event stream (for ordering during replay).</summary>
    public long SequenceNumber { get; init; }
}

/// <summary>
/// The raw input to a perception event — what actually happened on the machine.
/// </summary>
public record PerceptionInput
{
    public string ProcessName { get; init; } = string.Empty;
    public string WindowTitle { get; init; } = string.Empty;
    public TimeSpan FocusDuration { get; init; }
    public string EventType { get; init; } = string.Empty; // "window_change", "file_change", "idle_transition"
    public Dictionary<string, string> AdditionalContext { get; init; } = new();
}

/// <summary>
/// The interpretation that Engram produced from the input.
/// </summary>
public record PerceptionInterpretation
{
    public string BehavioralMode { get; init; } = string.Empty;
    public string PreviousMode { get; init; } = string.Empty;
    public string ProjectDetected { get; init; } = string.Empty;
    public double Confidence { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new();
}

/// <summary>
/// Result of comparing two interpretations of the same input.
/// </summary>
public record InterpretationComparison
{
    public string SnapshotId { get; init; } = string.Empty;
    public string ExpectedMode { get; init; } = string.Empty;
    public string ActualMode { get; init; } = string.Empty;
    public bool Match => ExpectedMode == ActualMode;
    public string DivergenceReason { get; init; } = string.Empty;
}
