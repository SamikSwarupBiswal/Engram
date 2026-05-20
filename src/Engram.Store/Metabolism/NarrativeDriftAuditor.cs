using Engram.Store.Events;
using Engram.Store.Identity;
using Engram.Store.Wiki;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Metabolism;

/// <summary>
/// Audits whether Engram's self-model of the user still matches reality.
/// 
/// The organism accumulates beliefs about the user over time:
/// goals, priorities, preferences, behavioral patterns, anxieties.
/// 
/// But reality changes. People change. Goals shift. Priorities evolve.
/// Without periodic audits, the self-model drifts from reality and
/// Engram starts responding to a person who no longer exists.
/// 
/// This auditor compares:
/// - Declared goals VS recent activity
/// - Stored preferences VS observed behavior
/// - Accumulated patterns VS current patterns
/// - Identity claims VS behavioral evidence
/// 
/// Produces alignment scores and drift warnings.
/// Should run weekly during long-term usage.
/// </summary>
public class NarrativeDriftAuditor
{
    private readonly WikiNodeStore _nodeStore;
    private readonly IdentityStore _identityStore;
    private readonly ILogger<NarrativeDriftAuditor>? _logger;
    private readonly List<DriftAuditResult> _auditHistory = new();
    private readonly object _lock = new();

    public NarrativeDriftAuditor(
        WikiNodeStore nodeStore,
        IdentityStore identityStore,
        ILogger<NarrativeDriftAuditor>? logger = null)
    {
        _nodeStore = nodeStore;
        _identityStore = identityStore;
        _logger = logger;
    }

    /// <summary>
    /// Run a full narrative drift audit.
    /// Compares stored self-model against current wiki state.
    /// </summary>
    public DriftAuditResult RunAudit()
    {
        var nodes = _nodeStore.LoadAll();
        var profile = _identityStore.LoadProfile();
        var priorities = _identityStore.LoadPriorities();

        var goalAlignment = AuditGoalAlignment(nodes, profile);
        var priorityAlignment = AuditPriorityAlignment(nodes, priorities);
        var freshnessScore = AuditFreshness(nodes);
        var coherenceScore = AuditCoherence(nodes);
        var tensionDensity = AuditTensionDensity(nodes);

        var overallAlignment = (goalAlignment.Score + priorityAlignment.Score +
            freshnessScore + coherenceScore) / 4.0;

        var result = new DriftAuditResult
        {
            AuditId = Guid.NewGuid().ToString("n")[..12],
            AuditedAt = DateTimeOffset.UtcNow,
            OverallAlignment = overallAlignment,
            GoalAlignment = goalAlignment,
            PriorityAlignment = priorityAlignment,
            FreshnessScore = freshnessScore,
            CoherenceScore = coherenceScore,
            TensionDensity = tensionDensity,
            Warnings = GenerateWarnings(goalAlignment, priorityAlignment, freshnessScore, coherenceScore, tensionDensity),
            NodeCount = nodes.Count
        };

        lock (_lock)
        {
            _auditHistory.Add(result);
        }

        _logger?.LogInformation(
            "Narrative drift audit: alignment={Alignment:P1}, goals={Goal:P1}, priorities={Priority:P1}, freshness={Fresh:P1}, coherence={Coherence:P1}",
            overallAlignment, goalAlignment.Score, priorityAlignment.Score, freshnessScore, coherenceScore);

        return result;
    }

    /// <summary>
    /// Get audit history for longitudinal tracking.
    /// </summary>
    public List<DriftAuditResult> GetAuditHistory()
    {
        lock (_lock)
        {
            return _auditHistory.ToList();
        }
    }

    /// <summary>
    /// Get the trend — is alignment improving or degrading?
    /// </summary>
    public DriftTrend GetTrend()
    {
        lock (_lock)
        {
            if (_auditHistory.Count < 2)
                return new DriftTrend { Direction = TrendDirection.InsufficientData };

            var recent = _auditHistory.TakeLast(5).ToList();
            var first = recent.First().OverallAlignment;
            var last = recent.Last().OverallAlignment;
            var delta = last - first;

            return new DriftTrend
            {
                Direction = delta > 0.05 ? TrendDirection.Improving
                    : delta < -0.05 ? TrendDirection.Degrading
                    : TrendDirection.Stable,
                AlignmentDelta = delta,
                FirstAlignment = first,
                LastAlignment = last,
                AuditCount = recent.Count
            };
        }
    }

    private AlignmentDimension AuditGoalAlignment(IReadOnlyList<WikiNode> nodes, UserProfile? profile)
    {
        var goals = nodes.Where(n => n.NodeType == WikiNodeType.Goal).ToList();
        if (goals.Count == 0)
        {
            return new AlignmentDimension
            {
                Dimension = "goals",
                Score = 0.5, // Neutral when no data
                Details = "No goals found in wiki"
            };
        }

        // Check how many goals have recent activity
        var activeGoals = goals.Where(g =>
            (DateTimeOffset.UtcNow - g.LastTouchedAt).TotalDays < 14 &&
            g.Salience > 0.2).ToList();

        var score = (double)activeGoals.Count / goals.Count;

        return new AlignmentDimension
        {
            Dimension = "goals",
            Score = score,
            Details = $"{activeGoals.Count}/{goals.Count} goals active in past 14 days",
            ActiveItems = activeGoals.Select(g => g.Title).ToList(),
            StaleItems = goals.Where(g => !activeGoals.Contains(g)).Select(g => g.Title).ToList()
        };
    }

    private AlignmentDimension AuditPriorityAlignment(IReadOnlyList<WikiNode> nodes, List<Priority> priorities)
    {
        if (priorities.Count == 0)
        {
            return new AlignmentDimension
            {
                Dimension = "priorities",
                Score = 0.5,
                Details = "No priorities declared"
            };
        }

        var recentNodes = nodes
            .Where(n => (DateTimeOffset.UtcNow - n.LastTouchedAt).TotalDays < 7)
            .ToList();

        int aligned = 0;
        foreach (var priority in priorities.Where(p => p.Confidence > 0.5))
        {
            var hasActivity = recentNodes.Any(n =>
                n.Title.Contains(priority.Description, StringComparison.OrdinalIgnoreCase) ||
                priority.Description.Contains(n.Title, StringComparison.OrdinalIgnoreCase));
            if (hasActivity) aligned++;
        }

        var score = priorities.Count > 0 ? (double)aligned / priorities.Count : 0.5;

        return new AlignmentDimension
        {
            Dimension = "priorities",
            Score = score,
            Details = $"{aligned}/{priorities.Count} priorities have recent activity"
        };
    }

    private double AuditFreshness(IReadOnlyList<WikiNode> nodes)
    {
        if (nodes.Count == 0) return 0.5;

        var avgDaysSinceTouch = nodes.Average(n => (DateTimeOffset.UtcNow - n.LastTouchedAt).TotalDays);

        // Score: 1.0 if avg < 3 days, 0.0 if avg > 30 days
        return Math.Max(0, Math.Min(1.0, 1.0 - (avgDaysSinceTouch / 30.0)));
    }

    private double AuditCoherence(IReadOnlyList<WikiNode> nodes)
    {
        if (nodes.Count < 2) return 1.0;

        // Coherence = how many nodes have relations vs total
        var nodesWithRelations = nodes.Count(n => n.Links.Count > 0);
        return (double)nodesWithRelations / nodes.Count;
    }

    private double AuditTensionDensity(IReadOnlyList<WikiNode> nodes)
    {
        if (nodes.Count == 0) return 0;

        // Tension = nodes with low salience that are still goals/decisions
        var tensionNodes = nodes.Where(n =>
            (n.NodeType == WikiNodeType.Goal || n.NodeType == WikiNodeType.Decision) &&
            n.Salience < 0.3).ToList();

        return (double)tensionNodes.Count / nodes.Count;
    }

    private List<DriftWarning> GenerateWarnings(
        AlignmentDimension goals, AlignmentDimension priorities,
        double freshness, double coherence, double tensionDensity)
    {
        var warnings = new List<DriftWarning>();

        if (goals.Score < 0.3)
            warnings.Add(new DriftWarning
            {
                Severity = DriftWarningSeverity.High,
                Dimension = "goals",
                Message = $"Goal alignment critically low ({goals.Score:P0}). {goals.Details}. Self-model may be outdated."
            });

        if (priorities.Score < 0.3)
            warnings.Add(new DriftWarning
            {
                Severity = DriftWarningSeverity.Medium,
                Dimension = "priorities",
                Message = $"Priority alignment low ({priorities.Score:P0}). {priorities.Details}."
            });

        if (freshness < 0.3)
            warnings.Add(new DriftWarning
            {
                Severity = DriftWarningSeverity.High,
                Dimension = "freshness",
                Message = $"Memory freshness critically low ({freshness:P0}). Knowledge graph may be stale."
            });

        if (coherence < 0.2)
            warnings.Add(new DriftWarning
            {
                Severity = DriftWarningSeverity.Medium,
                Dimension = "coherence",
                Message = $"Knowledge coherence low ({coherence:P0}). Many nodes are isolated."
            });

        if (tensionDensity > 0.5)
            warnings.Add(new DriftWarning
            {
                Severity = DriftWarningSeverity.Medium,
                Dimension = "tension",
                Message = $"High tension density ({tensionDensity:P0}). Many goals/decisions have faded salience."
            });

        return warnings;
    }
}

/// <summary>
/// Result of a narrative drift audit.
/// </summary>
public record DriftAuditResult
{
    public string AuditId { get; init; } = string.Empty;
    public DateTimeOffset AuditedAt { get; init; }
    public double OverallAlignment { get; init; }
    public AlignmentDimension GoalAlignment { get; init; } = new();
    public AlignmentDimension PriorityAlignment { get; init; } = new();
    public double FreshnessScore { get; init; }
    public double CoherenceScore { get; init; }
    public double TensionDensity { get; init; }
    public List<DriftWarning> Warnings { get; init; } = new();
    public int NodeCount { get; init; }
}

/// <summary>
/// A single alignment dimension with score and details.
/// </summary>
public record AlignmentDimension
{
    public string Dimension { get; init; } = string.Empty;
    public double Score { get; init; }
    public string Details { get; init; } = string.Empty;
    public List<string> ActiveItems { get; init; } = new();
    public List<string> StaleItems { get; init; } = new();
}

/// <summary>
/// A drift warning.
/// </summary>
public record DriftWarning
{
    public DriftWarningSeverity Severity { get; init; }
    public string Dimension { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public enum DriftWarningSeverity
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// Trend in alignment over time.
/// </summary>
public record DriftTrend
{
    public TrendDirection Direction { get; init; }
    public double AlignmentDelta { get; init; }
    public double FirstAlignment { get; init; }
    public double LastAlignment { get; init; }
    public int AuditCount { get; init; }
}

public enum TrendDirection
{
    Improving,
    Stable,
    Degrading,
    InsufficientData
}
