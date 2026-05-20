using Microsoft.Extensions.Logging;

namespace Engram.Store.Metabolism;

/// <summary>
/// Detects when contradictions have been resolved.
/// 
/// Without resolution detection:
/// - interventions become stale
/// - memory becomes accusatory
/// - trust collapses
/// 
/// Resolution signals:
/// - Goal salience increased (user started working on it again)
/// - Related activity appeared (behavior aligned with intent)
/// - Time-based decay (contradiction is old and no longer relevant)
/// - User acknowledgment (explicit resolution)
/// </summary>
public class ContradictionResolutionDetector
{
    private readonly ContradictionHistoryStore _historyStore;
    private readonly Wiki.WikiNodeStore _nodeStore;
    private readonly ILogger<ContradictionResolutionDetector>? _logger;

    /// <summary>Days after which an unobserved contradiction is considered stale.</summary>
    public int StaleDays { get; set; } = 30;

    /// <summary>Minimum salience increase to signal resolution.</summary>
    public double SalienceRecoveryThreshold { get; set; } = 0.4;

    public ContradictionResolutionDetector(
        ContradictionHistoryStore historyStore,
        Wiki.WikiNodeStore nodeStore,
        ILogger<ContradictionResolutionDetector>? logger = null)
    {
        _historyStore = historyStore;
        _nodeStore = nodeStore;
        _logger = logger;
    }

    /// <summary>
    /// Scan all active contradictions and detect resolutions.
    /// Returns list of resolved contradiction IDs.
    /// </summary>
    public List<ResolutionResult> DetectResolutions()
    {
        var results = new List<ResolutionResult>();
        var active = _historyStore.LoadActive();
        var nodes = _nodeStore.LoadAll();

        foreach (var contradiction in active)
        {
            var resolution = CheckResolution(contradiction, nodes);
            if (resolution != null)
            {
                results.Add(resolution);
                _historyStore.Resolve(contradiction.ContradictionId, resolution.Reason);
                _logger?.LogInformation(
                    "Contradiction resolved: {Id} — {Reason}",
                    contradiction.ContradictionId, resolution.Reason);
            }
        }

        return results;
    }

    /// <summary>
    /// Check if a specific contradiction has been resolved.
    /// </summary>
    private ResolutionResult? CheckResolution(ContradictionHistoryEntry record, IReadOnlyList<Wiki.WikiNode> nodes)
    {
        // Signal 1: Goal salience recovered
        if (record.Type == ContradictionType.GoalActivityGap ||
            record.Type == ContradictionType.PriorityDrift)
        {
            var goalNode = nodes.FirstOrDefault(n =>
                record.RelatedNodeIds.Contains(n.NodeId) && n.NodeType == Wiki.WikiNodeType.Goal);

            if (goalNode != null && goalNode.Salience >= SalienceRecoveryThreshold)
            {
                return new ResolutionResult
                {
                    ContradictionId = record.ContradictionId,
                    ResolutionType = ResolutionType.SalienceRecovery,
                    Reason = $"Goal '{goalNode.Title}' salience recovered to {goalNode.Salience:F2}",
                    Confidence = 0.8
                };
            }
        }

        // Signal 2: Related activity appeared
        if (record.Type == ContradictionType.AbandonedCommitment)
        {
            var commitmentNode = nodes.FirstOrDefault(n =>
                record.RelatedNodeIds.Contains(n.NodeId));

            if (commitmentNode != null)
            {
                var daysSinceTouch = (DateTimeOffset.UtcNow - commitmentNode.LastTouchedAt).TotalDays;
                if (daysSinceTouch < 3) // Recently active
                {
                    return new ResolutionResult
                    {
                        ContradictionId = record.ContradictionId,
                        ResolutionType = ResolutionType.ActivityResumed,
                        Reason = $"Commitment '{commitmentNode.Title}' has recent activity ({daysSinceTouch:F0}d ago)",
                        Confidence = 0.7
                    };
                }
            }
        }

        // Signal 3: Stale contradiction (no new observations)
        var daysSinceLastSeen = (DateTimeOffset.UtcNow - record.LastSeenAt).TotalDays;
        if (daysSinceLastSeen > StaleDays)
        {
            return new ResolutionResult
            {
                ContradictionId = record.ContradictionId,
                ResolutionType = ResolutionType.StaleDecay,
                Reason = $"No observations for {daysSinceLastSeen:F0} days",
                Confidence = 0.5
            };
        }

        // Signal 4: Severity decreased to Low
        if (record.Trend == ContradictionTrend.Improving &&
            record.CurrentSeverity == ContradictionSeverity.Low)
        {
            return new ResolutionResult
            {
                ContradictionId = record.ContradictionId,
                ResolutionType = ResolutionType.SeverityDecayed,
                Reason = "Severity decreased to Low with improving trend",
                Confidence = 0.6
            };
        }

        return null;
    }
}

public class ResolutionResult
{
    public string ContradictionId { get; set; } = string.Empty;
    public ResolutionType ResolutionType { get; set; }
    public string Reason { get; set; } = string.Empty;
    public double Confidence { get; set; }
}

public enum ResolutionType
{
    SalienceRecovery,   // Goal salience increased
    ActivityResumed,    // Related activity appeared
    StaleDecay,         // No observations for long time
    SeverityDecayed,    // Severity decreased
    UserAcknowledged    // User explicitly resolved
}
