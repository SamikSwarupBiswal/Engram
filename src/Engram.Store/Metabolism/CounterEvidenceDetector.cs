using Engram.Store.Wiki;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Metabolism;

/// <summary>
/// Finds balancing evidence for contradictions.
/// 
/// Right now contradictions accumulate, but where is the balancing evidence?
/// 
/// Example:
///   deep work contradiction exists
///   BUT 3 productive coding sessions happened
/// 
/// Without this, Engram collapses into negativity.
/// 
/// Counter-evidence types:
/// - Positive activity: related nodes with high salience and recent touch
/// - Resolution signals: goal salience recovery, activity resumed
/// - Contradicting facts: wiki facts that contradict the contradiction
/// - Temporal evidence: recent positive trend in related areas
/// </summary>
public class CounterEvidenceDetector
{
    private readonly WikiNodeStore _nodeStore;
    private readonly ContradictionHistoryStore _historyStore;
    private readonly ILogger<CounterEvidenceDetector>? _logger;

    public CounterEvidenceDetector(
        WikiNodeStore nodeStore,
        ContradictionHistoryStore historyStore,
        ILogger<CounterEvidenceDetector>? logger = null)
    {
        _nodeStore = nodeStore;
        _historyStore = historyStore;
        _logger = logger;
    }

    /// <summary>
    /// Find counter-evidence for all active contradictions.
    /// Returns a map of contradiction ID → list of counter-evidence.
    /// </summary>
    public Dictionary<string, List<CounterEvidence>> FindCounterEvidence()
    {
        var active = _historyStore.LoadActive();
        var nodes = _nodeStore.LoadAll();
        var result = new Dictionary<string, List<CounterEvidence>>();

        foreach (var contradiction in active)
        {
            var evidence = FindCounterEvidenceForContradiction(contradiction, nodes);
            if (evidence.Count > 0)
            {
                result[contradiction.ContradictionId] = evidence;
            }
        }

        _logger?.LogInformation(
            "Found counter-evidence for {Count}/{Total} active contradictions",
            result.Count, active.Count);

        return result;
    }

    /// <summary>
    /// Find counter-evidence for a specific contradiction.
    /// </summary>
    public List<CounterEvidence> FindCounterEvidenceForContradiction(
        ContradictionHistoryEntry contradiction,
        IReadOnlyList<WikiNode>? nodes = null)
    {
        nodes ??= _nodeStore.LoadAll();
        var evidence = new List<CounterEvidence>();

        // Type-specific counter-evidence
        evidence.AddRange(contradiction.Type switch
        {
            ContradictionType.GoalActivityGap => FindGoalActivityCounterEvidence(contradiction, nodes),
            ContradictionType.PriorityDrift => FindPriorityDriftCounterEvidence(contradiction, nodes),
            ContradictionType.AbandonedCommitment => FindAbandonedCommitmentCounterEvidence(contradiction, nodes),
            ContradictionType.IdentityBehaviorGap => FindIdentityGapCounterEvidence(contradiction, nodes),
            _ => new List<CounterEvidence>()
        });

        // Generic counter-evidence: recent positive activity in related areas
        evidence.AddRange(FindRelatedPositiveActivity(contradiction, nodes));

        return evidence;
    }

    /// <summary>
    /// Find counter-evidence for GoalActivityGap contradictions.
    /// Look for: goal salience recovery, related activity, recent touches.
    /// </summary>
    private List<CounterEvidence> FindGoalActivityCounterEvidence(
        ContradictionHistoryEntry contradiction,
        IReadOnlyList<WikiNode> nodes)
    {
        var evidence = new List<CounterEvidence>();

        // Find the goal node
        var goalNode = nodes.FirstOrDefault(n =>
            contradiction.RelatedNodeIds.Contains(n.NodeId) &&
            n.NodeType == WikiNodeType.Goal);

        if (goalNode == null) return evidence;

        // Evidence 1: Goal salience is actually recovering
        if (goalNode.Salience > 0.3)
        {
            evidence.Add(new CounterEvidence
            {
                Type = CounterEvidenceType.SalienceRecovery,
                Description = $"Goal '{goalNode.Title}' salience is {goalNode.Salience:F2} — above fading threshold",
                Strength = CounterEvidenceStrength.Medium,
                SourceNodeId = goalNode.NodeId,
                DetectedAt = DateTimeOffset.UtcNow
            });
        }

        // Evidence 2: Goal was recently touched
        var daysSinceTouch = (DateTimeOffset.UtcNow - goalNode.LastTouchedAt).TotalDays;
        if (daysSinceTouch < 3)
        {
            evidence.Add(new CounterEvidence
            {
                Type = CounterEvidenceType.RecentActivity,
                Description = $"Goal '{goalNode.Title}' was touched {daysSinceTouch:F0} days ago",
                Strength = CounterEvidenceStrength.Strong,
                SourceNodeId = goalNode.NodeId,
                DetectedAt = DateTimeOffset.UtcNow
            });
        }

        // Evidence 3: Related nodes show activity
        var relatedNodes = nodes.Where(n =>
            n.NodeId != goalNode.NodeId &&
            (n.Title.Contains(goalNode.Title, StringComparison.OrdinalIgnoreCase) ||
             goalNode.Facts.Any(f => n.Facts.Any(nf =>
                 nf.Text.Contains(f.Text[..Math.Min(20, f.Text.Length)], StringComparison.OrdinalIgnoreCase)))))
            .ToList();

        var activeRelated = relatedNodes.Where(n =>
            (DateTimeOffset.UtcNow - n.LastTouchedAt).TotalDays < 7 && n.Salience > 0.3).ToList();

        if (activeRelated.Count > 0)
        {
            evidence.Add(new CounterEvidence
            {
                Type = CounterEvidenceType.RelatedActivity,
                Description = $"{activeRelated.Count} related nodes active in past week: {string.Join(", ", activeRelated.Take(3).Select(n => n.Title))}",
                Strength = CounterEvidenceStrength.Medium,
                DetectedAt = DateTimeOffset.UtcNow
            });
        }

        return evidence;
    }

    /// <summary>
    /// Find counter-evidence for PriorityDrift contradictions.
    /// Look for: priority-related activity, partial alignment.
    /// </summary>
    private List<CounterEvidence> FindPriorityDriftCounterEvidence(
        ContradictionHistoryEntry contradiction,
        IReadOnlyList<WikiNode> nodes)
    {
        var evidence = new List<CounterEvidence>();

        // Find nodes related to the declared priority
        var relatedNodes = nodes.Where(n =>
            n.Title.Contains(contradiction.DeclaredIntent, StringComparison.OrdinalIgnoreCase) ||
            n.Facts.Any(f => f.Text.Contains(contradiction.DeclaredIntent, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // Evidence: Some related activity exists (even if not primary focus)
        if (relatedNodes.Count > 0)
        {
            var recentlyActive = relatedNodes.Where(n =>
                (DateTimeOffset.UtcNow - n.LastTouchedAt).TotalDays < 14).ToList();

            if (recentlyActive.Count > 0)
            {
                evidence.Add(new CounterEvidence
                {
                    Type = CounterEvidenceType.RelatedActivity,
                    Description = $"Priority '{contradiction.DeclaredIntent}' has {recentlyActive.Count} related nodes with recent activity",
                    Strength = CounterEvidenceStrength.Medium,
                    DetectedAt = DateTimeOffset.UtcNow
                });
            }
        }

        return evidence;
    }

    /// <summary>
    /// Find counter-evidence for AbandonedCommitment contradictions.
    /// Look for: resumed activity, partial progress, context switching.
    /// </summary>
    private List<CounterEvidence> FindAbandonedCommitmentCounterEvidence(
        ContradictionHistoryEntry contradiction,
        IReadOnlyList<WikiNode> nodes)
    {
        var evidence = new List<CounterEvidence>();

        // Find the commitment node
        var commitmentNode = nodes.FirstOrDefault(n =>
            contradiction.RelatedNodeIds.Contains(n.NodeId));

        if (commitmentNode == null) return evidence;

        // Evidence: Recent activity on the commitment
        var daysSinceTouch = (DateTimeOffset.UtcNow - commitmentNode.LastTouchedAt).TotalDays;
        if (daysSinceTouch < 7)
        {
            evidence.Add(new CounterEvidence
            {
                Type = CounterEvidenceType.RecentActivity,
                Description = $"Commitment '{commitmentNode.Title}' has recent activity ({daysSinceTouch:F0}d ago)",
                Strength = CounterEvidenceStrength.Strong,
                SourceNodeId = commitmentNode.NodeId,
                DetectedAt = DateTimeOffset.UtcNow
            });
        }

        // Evidence: Related nodes show progress
        var relatedNodes = nodes.Where(n =>
            n.NodeId != commitmentNode.NodeId &&
            n.Title.Contains(commitmentNode.Title, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (relatedNodes.Any(n => (DateTimeOffset.UtcNow - n.LastTouchedAt).TotalDays < 7))
        {
            evidence.Add(new CounterEvidence
            {
                Type = CounterEvidenceType.RelatedActivity,
                Description = $"Related activity detected for commitment '{commitmentNode.Title}'",
                Strength = CounterEvidenceStrength.Medium,
                DetectedAt = DateTimeOffset.UtcNow
            });
        }

        return evidence;
    }

    /// <summary>
    /// Find counter-evidence for IdentityBehaviorGap contradictions.
    /// Look for: behavior matching identity claims.
    /// </summary>
    private List<CounterEvidence> FindIdentityGapCounterEvidence(
        ContradictionHistoryEntry contradiction,
        IReadOnlyList<WikiNode> nodes)
    {
        var evidence = new List<CounterEvidence>();

        // Find nodes matching the declared preference
        var matchingNodes = nodes.Where(n =>
            n.Title.Contains(contradiction.DeclaredIntent, StringComparison.OrdinalIgnoreCase) ||
            n.Facts.Any(f => f.Text.Contains(contradiction.DeclaredIntent, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (matchingNodes.Count > 0)
        {
            evidence.Add(new CounterEvidence
            {
                Type = CounterEvidenceType.BehaviorMatch,
                Description = $"Found {matchingNodes.Count} nodes matching preference '{contradiction.DeclaredIntent}'",
                Strength = CounterEvidenceStrength.Medium,
                DetectedAt = DateTimeOffset.UtcNow
            });
        }

        return evidence;
    }

    /// <summary>
    /// Find generic counter-evidence: recent positive activity in related areas.
    /// </summary>
    private List<CounterEvidence> FindRelatedPositiveActivity(
        ContradictionHistoryEntry contradiction,
        IReadOnlyList<WikiNode> nodes)
    {
        var evidence = new List<CounterEvidence>();

        // Find high-salience, recently-touched nodes related to the contradiction
        var relatedActive = nodes.Where(n =>
            n.Salience > 0.5 &&
            (DateTimeOffset.UtcNow - n.LastTouchedAt).TotalDays < 7 &&
            (contradiction.RelatedNodeIds.Contains(n.NodeId) ||
             n.Title.Contains(contradiction.DeclaredIntent, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (relatedActive.Count >= 2)
        {
            evidence.Add(new CounterEvidence
            {
                Type = CounterEvidenceType.PositiveTrend,
                Description = $"Positive trend: {relatedActive.Count} related nodes active with high salience",
                Strength = CounterEvidenceStrength.Medium,
                DetectedAt = DateTimeOffset.UtcNow
            });
        }

        return evidence;
    }
}

/// <summary>
/// A piece of counter-evidence that contradicts a behavioral contradiction.
/// </summary>
public class CounterEvidence
{
    public CounterEvidenceType Type { get; set; }
    public string Description { get; set; } = string.Empty;
    public CounterEvidenceStrength Strength { get; set; }
    public string? SourceNodeId { get; set; }
    public DateTimeOffset DetectedAt { get; set; }
}

public enum CounterEvidenceType
{
    SalienceRecovery,   // Goal salience increased
    RecentActivity,     // Related activity detected
    RelatedActivity,    // Related nodes active
    BehaviorMatch,      // Behavior matches identity claim
    PositiveTrend       // General positive trend
}

public enum CounterEvidenceStrength
{
    Weak,     // Suggestive but not conclusive
    Medium,   // Moderate evidence
    Strong    // Strong evidence against the contradiction
}
