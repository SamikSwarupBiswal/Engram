using Engram.Store.Identity;
using Engram.Store.Inference;
using Engram.Store.Wiki;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Metabolism;

/// <summary>
/// Detects contradictions between declared intent and observed behavior.
/// 
/// This is Engram's actual moat — behavioral intelligence.
/// 
/// Compares:
/// - Declared priorities VS observed activity
/// - Stated goals VS time allocation
/// - Commitments VS follow-through
/// - Identity claims VS behavioral patterns
/// 
/// This is where Engram becomes behaviorally intelligent
/// instead of just a memory chatbot.
/// </summary>
public class ContradictionDetector
{
    private readonly WikiNodeStore _nodeStore;
    private readonly IdentityStore _identityStore;
    private readonly ILogger<ContradictionDetector>? _logger;

    public ContradictionDetector(
        WikiNodeStore nodeStore,
        IdentityStore identityStore,
        ILogger<ContradictionDetector>? logger = null)
    {
        _nodeStore = nodeStore;
        _identityStore = identityStore;
        _logger = logger;
    }

    /// <summary>
    /// Detect all contradictions in the current state.
    /// Returns a list of behavioral contradictions.
    /// </summary>
    public List<BehavioralContradiction> DetectAll()
    {
        var contradictions = new List<BehavioralContradiction>();

        contradictions.AddRange(DetectGoalActivityContradictions());
        contradictions.AddRange(DetectPriorityDrift());
        contradictions.AddRange(DetectAbandonedCommitments());
        contradictions.AddRange(DetectIdentityBehaviorGaps());

        // Apply epistemic caution scaling under low confidence
        foreach (var c in contradictions)
        {
            c.Severity = ScaleSeverity(c.Severity);
        }

        _logger?.LogInformation("Detected {Count} behavioral contradictions", contradictions.Count);
        return contradictions;
    }

    private ContradictionSeverity ScaleSeverity(ContradictionSeverity originalSeverity)
    {
        var confidence = DegradationTracker.Instance.GetEnvironmentalConfidence();
        if (confidence >= 0.8)
        {
            return originalSeverity;
        }

        if (confidence < 0.5)
        {
            return ContradictionSeverity.Low;
        }

        return originalSeverity switch
        {
            ContradictionSeverity.Critical => ContradictionSeverity.High,
            ContradictionSeverity.High => ContradictionSeverity.Medium,
            ContradictionSeverity.Medium => ContradictionSeverity.Low,
            _ => ContradictionSeverity.Low
        };
    }

    /// <summary>
    /// Detect when goals have low salience but unrelated activities are high.
    /// Example: Goal "ship Engram" fading while "YouTube" is highly active.
    /// </summary>
    private List<BehavioralContradiction> DetectGoalActivityContradictions()
    {
        var contradictions = new List<BehavioralContradiction>();
        var nodes = _nodeStore.LoadAll();

        var goals = nodes.Where(n => n.NodeType == WikiNodeType.Goal).ToList();
        var activities = nodes.Where(n => n.NodeType == WikiNodeType.Concept && n.Salience > 0.5).ToList();

        foreach (var goal in goals)
        {
            if (goal.Salience > 0.4) continue; // Only flag fading goals

            // Find high-activity concepts that aren't related to the goal
            var unrelatedActivities = activities.Where(a =>
                !a.Title.Contains(goal.Title, StringComparison.OrdinalIgnoreCase) &&
                !goal.Facts.Any(f => f.Text.Contains(a.Title, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(a => a.Salience)
                .ToList();

            if (unrelatedActivities.Count > 0)
            {
                var topActivity = unrelatedActivities.First();
                var daysSinceGoalTouch = (DateTimeOffset.UtcNow - goal.LastTouchedAt).TotalDays;

                contradictions.Add(new BehavioralContradiction
                {
                    Type = ContradictionType.GoalActivityGap,
                    Severity = daysSinceGoalTouch > 7 ? ContradictionSeverity.High : ContradictionSeverity.Medium,
                    Description = $"Goal '{goal.Title}' is fading (salience: {goal.Salience:F2}, last touched: {daysSinceGoalTouch:F0}d ago) while '{topActivity.Title}' is highly active (salience: {topActivity.Salience:F2})",
                    DeclaredIntent = goal.Title,
                    ObservedBehavior = $"High activity on: {string.Join(", ", unrelatedActivities.Take(3).Select(a => a.Title))}",
                    RelatedNodeIds = new List<string> { goal.NodeId, topActivity.NodeId }
                });
            }
        }

        return contradictions;
    }

    /// <summary>
    /// Detect when declared priorities aren't reflected in activity.
    /// </summary>
    private List<BehavioralContradiction> DetectPriorityDrift()
    {
        var contradictions = new List<BehavioralContradiction>();
        var priorities = _identityStore.LoadPriorities();

        if (priorities.Count == 0) return contradictions;

        var nodes = _nodeStore.LoadAll();
        var recentNodes = nodes
            .Where(n => n.LastTouchedAt > DateTimeOffset.UtcNow.AddDays(-7))
            .OrderByDescending(n => n.Salience)
            .ToList();

        // Check if recent activity aligns with priorities
        foreach (var priority in priorities.Where(p => p.Confidence > 0.7))
        {
            var relatedActivity = recentNodes.FirstOrDefault(n =>
                n.Title.Contains(priority.Description, StringComparison.OrdinalIgnoreCase) ||
                priority.Description.Contains(n.Title, StringComparison.OrdinalIgnoreCase));

            if (relatedActivity == null && recentNodes.Count > 3)
            {
                contradictions.Add(new BehavioralContradiction
                {
                    Type = ContradictionType.PriorityDrift,
                    Severity = ContradictionSeverity.Medium,
                    Description = $"Priority '{priority.Description}' declared but no related activity in 7 days. Recent focus: {string.Join(", ", recentNodes.Take(3).Select(n => n.Title))}",
                    DeclaredIntent = priority.Description,
                    ObservedBehavior = $"Active on: {string.Join(", ", recentNodes.Take(3).Select(n => n.Title))}",
                    RelatedNodeIds = recentNodes.Take(3).Select(n => n.NodeId).ToList()
                });
            }
        }

        return contradictions;
    }

    /// <summary>
    /// Detect commitments that haven't been followed through.
    /// </summary>
    private List<BehavioralContradiction> DetectAbandonedCommitments()
    {
        var contradictions = new List<BehavioralContradiction>();
        var nodes = _nodeStore.LoadAll();

        // Find nodes that look like commitments (tasks, decisions)
        var commitments = nodes.Where(n =>
            (n.NodeType == WikiNodeType.Decision || n.NodeType == WikiNodeType.Concept) &&
            n.Facts.Any(f =>
                f.Text.Contains("will", StringComparison.OrdinalIgnoreCase) ||
                f.Text.Contains("commit", StringComparison.OrdinalIgnoreCase) ||
                f.Text.Contains("plan to", StringComparison.OrdinalIgnoreCase) ||
                f.Text.Contains("going to", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var commitment in commitments)
        {
            var daysSinceTouch = (DateTimeOffset.UtcNow - commitment.LastTouchedAt).TotalDays;
            if (daysSinceTouch < 7) continue; // Only flag old commitments

            // Check if there's any follow-up activity
            var followUp = nodes.FirstOrDefault(n =>
                n.NodeId != commitment.NodeId &&
                n.LastTouchedAt > commitment.LastTouchedAt &&
                (n.Title.Contains(commitment.Title, StringComparison.OrdinalIgnoreCase) ||
                 commitment.Facts.Any(f => n.Facts.Any(nf => nf.Text.Contains(f.Text[..Math.Min(20, f.Text.Length)], StringComparison.OrdinalIgnoreCase)))));

            if (followUp == null)
            {
                contradictions.Add(new BehavioralContradiction
                {
                    Type = ContradictionType.AbandonedCommitment,
                    Severity = daysSinceTouch > 14 ? ContradictionSeverity.High : ContradictionSeverity.Medium,
                    Description = $"Commitment '{commitment.Title}' hasn't been touched in {daysSinceTouch:F0} days with no follow-up activity",
                    DeclaredIntent = commitment.Facts.First().Text,
                    ObservedBehavior = "No follow-up activity detected",
                    RelatedNodeIds = new List<string> { commitment.NodeId }
                });
            }
        }

        return contradictions;
    }

    /// <summary>
    /// Detect gaps between identity claims and behavior.
    /// </summary>
    private List<BehavioralContradiction> DetectIdentityBehaviorGaps()
    {
        var contradictions = new List<BehavioralContradiction>();
        var profile = _identityStore.LoadProfile();

        if (profile == null) return contradictions;

        var nodes = _nodeStore.LoadAll();

        // Check comfort triggers (preferences) vs actual behavior
        foreach (var preference in profile.ComfortTriggers)
        {
            var relatedNodes = nodes.Where(n =>
                n.Title.Contains(preference, StringComparison.OrdinalIgnoreCase) ||
                n.Facts.Any(f => f.Text.Contains(preference, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (relatedNodes.Count == 0)
            {
                contradictions.Add(new BehavioralContradiction
                {
                    Type = ContradictionType.IdentityBehaviorGap,
                    Severity = ContradictionSeverity.Low,
                    Description = $"Preference '{preference}' declared but no related activity observed",
                    DeclaredIntent = preference,
                    ObservedBehavior = "No related activity in wiki",
                    RelatedNodeIds = new List<string>()
                });
            }
        }

        return contradictions;
    }
}

/// <summary>
/// A behavioral contradiction — gap between declared intent and observed behavior.
/// </summary>
public class BehavioralContradiction
{
    public ContradictionType Type { get; set; }
    public ContradictionSeverity Severity { get; set; }
    public string Description { get; set; } = string.Empty;
    public string DeclaredIntent { get; set; } = string.Empty;
    public string ObservedBehavior { get; set; } = string.Empty;
    public List<string> RelatedNodeIds { get; set; } = new();
    public DateTimeOffset DetectedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum ContradictionType
{
    GoalActivityGap,      // Goal fading while unrelated activity is high
    PriorityDrift,        // Declared priorities not reflected in behavior
    AbandonedCommitment,  // Commitment made but no follow-through
    IdentityBehaviorGap   // Identity claims not supported by behavior
}

public enum ContradictionSeverity
{
    Low,       // Minor gap, could be intentional
    Medium,    // Notable gap worth noting
    High,      // Significant behavioral contradiction
    Critical   // Fundamental misalignment
}
