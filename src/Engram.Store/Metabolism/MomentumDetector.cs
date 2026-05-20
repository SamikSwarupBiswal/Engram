using Engram.Store.Wiki;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Metabolism;

/// <summary>
/// Positive evidence modeling — growth recognition.
/// 
/// Right now contradictions are sophisticated.
/// But where is growth recognition?
/// 
/// Without this, Engram becomes psychologically asymmetrical:
/// - Only sees problems
/// - Never celebrates progress
/// - Misses improvement signals
/// - Becomes a corrective nag
/// 
/// Detects:
/// - Momentum: sustained positive activity
/// - Improvement: salience recovery, trend reversal
/// - Success: goal completion, milestone achievement
/// - Recovery: return from burnout/distraction
/// </summary>
public class MomentumDetector
{
    private readonly WikiNodeStore _nodeStore;
    private readonly ContradictionHistoryStore _historyStore;
    private readonly ILogger<MomentumDetector>? _logger;

    public MomentumDetector(
        WikiNodeStore nodeStore,
        ContradictionHistoryStore historyStore,
        ILogger<MomentumDetector>? logger = null)
    {
        _nodeStore = nodeStore;
        _historyStore = historyStore;
        _logger = logger;
    }

    /// <summary>
    /// Detect all positive signals in the current state.
    /// </summary>
    public List<PositiveSignal> DetectPositiveSignals()
    {
        var signals = new List<PositiveSignal>();

        signals.AddRange(DetectMomentum());
        signals.AddRange(DetectImprovement());
        signals.AddRange(DetectSuccess());
        signals.AddRange(DetectRecovery());

        _logger?.LogInformation("Detected {Count} positive signals", signals.Count);
        return signals;
    }

    /// <summary>
    /// Detect momentum: sustained positive activity on goals/projects.
    /// </summary>
    private List<PositiveSignal> DetectMomentum()
    {
        var signals = new List<PositiveSignal>();
        var nodes = _nodeStore.LoadAll();

        // Find goals/projects with sustained recent activity
        var activeGoals = nodes.Where(n =>
            (n.NodeType == WikiNodeType.Goal || n.NodeType == WikiNodeType.Project) &&
            n.Salience > 0.4 &&
            (DateTimeOffset.UtcNow - n.LastTouchedAt).TotalDays < 3)
            .ToList();

        foreach (var goal in activeGoals)
        {
            signals.Add(new PositiveSignal
            {
                Type = PositiveSignalType.Momentum,
                Description = $"Sustained activity on '{goal.Title}' — building momentum",
                Strength = SignalStrength.Medium,
                SourceNodeId = goal.NodeId,
                DetectedAt = DateTimeOffset.UtcNow,
                CelebrationLevel = CelebrationLevel.Acknowledge
            });
        }

        // Detect multiple active goals (balanced focus)
        if (activeGoals.Count >= 3)
        {
            signals.Add(new PositiveSignal
            {
                Type = PositiveSignalType.Momentum,
                Description = $"Balanced focus across {activeGoals.Count} active goals",
                Strength = SignalStrength.Strong,
                DetectedAt = DateTimeOffset.UtcNow,
                CelebrationLevel = CelebrationLevel.Celebrate
            });
        }

        return signals;
    }

    /// <summary>
    /// Detect improvement: salience recovery, trend reversal.
    /// </summary>
    private List<PositiveSignal> DetectImprovement()
    {
        var signals = new List<PositiveSignal>();
        var resolved = _historyStore.LoadAll()
            .Where(c => c.Status == ContradictionStatus.Resolved)
            .ToList();

        // Recently resolved contradictions
        var recentlyResolved = resolved.Where(c =>
            c.ResolvedAt.HasValue &&
            c.ResolvedAt.Value > DateTimeOffset.UtcNow.AddDays(7))
            .ToList();

        foreach (var contradiction in recentlyResolved)
        {
            signals.Add(new PositiveSignal
            {
                Type = PositiveSignalType.Improvement,
                Description = $"Contradiction resolved: {contradiction.DeclaredIntent} — {contradiction.Resolution}",
                Strength = SignalStrength.Strong,
                DetectedAt = DateTimeOffset.UtcNow,
                CelebrationLevel = CelebrationLevel.Celebrate
            });
        }

        // Improving trends
        var active = _historyStore.LoadActive();
        var improving = active.Where(c => c.Trend == ContradictionTrend.Improving).ToList();

        foreach (var contradiction in improving)
        {
            signals.Add(new PositiveSignal
            {
                Type = PositiveSignalType.Improvement,
                Description = $"Improving trend: '{contradiction.DeclaredIntent}' is getting better",
                Strength = SignalStrength.Medium,
                DetectedAt = DateTimeOffset.UtcNow,
                CelebrationLevel = CelebrationLevel.Acknowledge
            });
        }

        return signals;
    }

    /// <summary>
    /// Detect success: goal completion, milestone achievement.
    /// </summary>
    private List<PositiveSignal> DetectSuccess()
    {
        var signals = new List<PositiveSignal>();
        var nodes = _nodeStore.LoadAll();

        // High-salience goals (close to completion)
        var highSalienceGoals = nodes.Where(n =>
            n.NodeType == WikiNodeType.Goal &&
            n.Salience > 0.7)
            .ToList();

        foreach (var goal in highSalienceGoals)
        {
            signals.Add(new PositiveSignal
            {
                Type = PositiveSignalType.Success,
                Description = $"Goal '{goal.Title}' has high salience ({goal.Salience:F2}) — strong progress",
                Strength = SignalStrength.Strong,
                SourceNodeId = goal.NodeId,
                DetectedAt = DateTimeOffset.UtcNow,
                CelebrationLevel = CelebrationLevel.Celebrate
            });
        }

        // Recently created nodes (new activity)
        var recentNodes = nodes.Where(n =>
            (DateTimeOffset.UtcNow - n.CreatedAt).TotalDays < 1 &&
            n.NodeType != WikiNodeType.Concept)
            .ToList();

        if (recentNodes.Count >= 2)
        {
            signals.Add(new PositiveSignal
            {
                Type = PositiveSignalType.Success,
                Description = $"Active creation: {recentNodes.Count} new nodes today",
                Strength = SignalStrength.Medium,
                DetectedAt = DateTimeOffset.UtcNow,
                CelebrationLevel = CelebrationLevel.Acknowledge
            });
        }

        return signals;
    }

    /// <summary>
    /// Detect recovery: return from burnout/distraction.
    /// </summary>
    private List<PositiveSignal> DetectRecovery()
    {
        var signals = new List<PositiveSignal>();
        var nodes = _nodeStore.LoadAll();

        // Find goals that were previously inactive but now active
        var goals = nodes.Where(n => n.NodeType == WikiNodeType.Goal).ToList();

        foreach (var goal in goals)
        {
            var daysSinceTouch = (DateTimeOffset.UtcNow - goal.LastTouchedAt).TotalDays;

            // Previously inactive (low salience) but recently touched
            if (goal.Salience < 0.3 && daysSinceTouch < 2)
            {
                signals.Add(new PositiveSignal
                {
                    Type = PositiveSignalType.Recovery,
                    Description = $"Goal '{goal.Title}' reactivated after period of inactivity",
                    Strength = SignalStrength.Strong,
                    SourceNodeId = goal.NodeId,
                    DetectedAt = DateTimeOffset.UtcNow,
                    CelebrationLevel = CelebrationLevel.Celebrate
                });
            }
        }

        return signals;
    }

    /// <summary>
    /// Compute overall momentum score.
    /// Returns 0.0 (no momentum) to 1.0 (strong momentum).
    /// </summary>
    public MomentumScore ComputeMomentumScore()
    {
        var signals = DetectPositiveSignals();
        var active = _historyStore.LoadActive();

        var score = new MomentumScore
        {
            ComputedAt = DateTimeOffset.UtcNow,
            TotalSignals = signals.Count,
            MomentumSignals = signals.Count(s => s.Type == PositiveSignalType.Momentum),
            ImprovementSignals = signals.Count(s => s.Type == PositiveSignalType.Improvement),
            SuccessSignals = signals.Count(s => s.Type == PositiveSignalType.Success),
            RecoverySignals = signals.Count(s => s.Type == PositiveSignalType.Recovery)
        };

        // Compute momentum score
        var signalScore = Math.Min(1.0, signals.Count / 5.0); // 5+ signals = max
        var improvementRatio = active.Count > 0
            ? (double)active.Count(c => c.Trend == ContradictionTrend.Improving) / active.Count
            : 0.5;
        var resolvedRatio = _historyStore.LoadAll().Count > 0
            ? (double)_historyStore.LoadAll().Count(c => c.Status == ContradictionStatus.Resolved) /
              _historyStore.LoadAll().Count
            : 0.5;

        score.Score = (signalScore * 0.4) + (improvementRatio * 0.3) + (resolvedRatio * 0.3);
        score.HasMomentum = score.Score > 0.4;
        score.Status = GetMomentumStatus(score);

        return score;
    }

    private static string GetMomentumStatus(MomentumScore score)
    {
        if (score.Score > 0.7)
            return "Strong momentum — sustained positive progress";
        if (score.Score > 0.4)
            return "Building momentum — positive signals detected";
        if (score.Score > 0.2)
            return "Weak momentum — limited positive signals";
        return "No momentum — focus on recovery";
    }
}

/// <summary>
/// A detected positive signal.
/// </summary>
public class PositiveSignal
{
    public PositiveSignalType Type { get; set; }
    public string Description { get; set; } = string.Empty;
    public SignalStrength Strength { get; set; }
    public string? SourceNodeId { get; set; }
    public DateTimeOffset DetectedAt { get; set; }
    public CelebrationLevel CelebrationLevel { get; set; }
}

public enum PositiveSignalType
{
    Momentum,     // Sustained positive activity
    Improvement,  // Trend reversal, salience recovery
    Success,      // Goal completion, milestone
    Recovery      // Return from burnout/distraction
}

public enum SignalStrength
{
    Weak,
    Medium,
    Strong
}

public enum CelebrationLevel
{
    Silent,      // Don't mention (too minor)
    Acknowledge, // Brief mention
    Celebrate    // Active recognition
}

/// <summary>
/// Overall momentum assessment.
/// </summary>
public class MomentumScore
{
    public DateTimeOffset ComputedAt { get; set; }
    public int TotalSignals { get; set; }
    public int MomentumSignals { get; set; }
    public int ImprovementSignals { get; set; }
    public int SuccessSignals { get; set; }
    public int RecoverySignals { get; set; }
    public double Score { get; set; }
    public bool HasMomentum { get; set; }
    public string Status { get; set; } = string.Empty;
}
