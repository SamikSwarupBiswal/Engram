using Microsoft.Extensions.Logging;

namespace Engram.Store.Metabolism;

/// <summary>
/// Psychological stability metrics for the organism itself.
/// 
/// Right now telemetry measures activity.
/// Now measure:
/// - contradiction ratio
/// - intervention density
/// - narrative diversity
/// - memory polarity
/// - semantic balance
/// - identity rigidity
/// 
/// These become the health metrics for the cognitive system.
/// </summary>
public class SemanticHealthMonitor
{
    private readonly ContradictionHistoryStore _historyStore;
    private readonly InterventionStore _interventionStore;
    private readonly IdentityStabilityEngine _stabilityEngine;
    private readonly NarrativeBalanceController _balanceController;
    private readonly CounterEvidenceDetector _counterEvidenceDetector;
    private readonly ILogger<SemanticHealthMonitor>? _logger;

    public SemanticHealthMonitor(
        ContradictionHistoryStore historyStore,
        InterventionStore interventionStore,
        IdentityStabilityEngine stabilityEngine,
        NarrativeBalanceController balanceController,
        CounterEvidenceDetector counterEvidenceDetector,
        ILogger<SemanticHealthMonitor>? logger = null)
    {
        _historyStore = historyStore;
        _interventionStore = interventionStore;
        _stabilityEngine = stabilityEngine;
        _balanceController = balanceController;
        _counterEvidenceDetector = counterEvidenceDetector;
        _logger = logger;
    }

    /// <summary>
    /// Compute a complete semantic health snapshot.
    /// </summary>
    public SemanticHealthSnapshot ComputeHealth()
    {
        var snapshot = new SemanticHealthSnapshot
        {
            ComputedAt = DateTimeOffset.UtcNow,
            ContradictionMetrics = ComputeContradictionMetrics(),
            InterventionMetrics = ComputeInterventionMetrics(),
            NarrativeMetrics = ComputeNarrativeMetrics(),
            StabilityMetrics = ComputeStabilityMetrics(),
            OverallHealth = ComputeOverallHealth()
        };

        return snapshot;
    }

    private ContradictionHealthMetrics ComputeContradictionMetrics()
    {
        var all = _historyStore.LoadAll();
        var active = all.Where(c => c.Status == ContradictionStatus.Active).ToList();
        var resolved = all.Where(c => c.Status == ContradictionStatus.Resolved).ToList();

        return new ContradictionHealthMetrics
        {
            TotalContradictions = all.Count,
            ActiveContradictions = active.Count,
            ResolvedContradictions = resolved.Count,
            ResolutionRate = all.Count > 0 ? (double)resolved.Count / all.Count : 0,
            AverageObservationsPerContradiction = all.Count > 0 ? all.Average(c => c.ObservationCount) : 0,
            WorseningCount = active.Count(c => c.Trend == ContradictionTrend.Worsening),
            ImprovingCount = active.Count(c => c.Trend == ContradictionTrend.Improving),
            RecurringCount = active.Count(c => c.Trend == ContradictionTrend.Recurring),
            TypeDistribution = active.GroupBy(c => c.Type)
                .ToDictionary(g => g.Key.ToString(), g => g.Count())
        };
    }

    private InterventionHealthMetrics ComputeInterventionMetrics()
    {
        var all = _interventionStore.LoadAll();
        var recent = _interventionStore.LoadRecent(TimeSpan.FromDays(7));
        var pending = all.Where(i => i.Status == InterventionStatus.Pending).ToList();
        var dismissed = all.Where(i => i.Status == InterventionStatus.Dismissed).ToList();
        var acted = all.Where(i => i.Status == InterventionStatus.Acted).ToList();

        return new InterventionHealthMetrics
        {
            TotalInterventions = all.Count,
            RecentInterventions = recent.Count,
            PendingInterventions = pending.Count,
            DismissedInterventions = dismissed.Count,
            ActedInterventions = acted.Count,
            DismissalRate = all.Count > 0 ? (double)dismissed.Count / all.Count : 0,
            ActionRate = all.Count > 0 ? (double)acted.Count / all.Count : 0,
            AverageSeverity = recent.Count > 0
                ? recent.Average(i => (int)i.Severity)
                : 0
        };
    }

    private NarrativeHealthMetrics ComputeNarrativeMetrics()
    {
        var balanceReport = _balanceController.GetBalanceReport();
        var active = _historyStore.LoadActive();

        // Compute narrative diversity
        var typeDistribution = active.GroupBy(c => c.Type).ToList();
        var dominantTypeRatio = typeDistribution.Count > 0
            ? typeDistribution.Max(g => (double)g.Count() / active.Count)
            : 0;

        return new NarrativeHealthMetrics
        {
            BalanceScore = balanceReport.BalanceScore,
            NarrativeDiversity = 1.0 - dominantTypeRatio, // Higher = more diverse
            IsBalanced = balanceReport.IsHealthy,
            BalanceStatus = balanceReport.Status,
            DailyBudgetRemaining = balanceReport.DailyBudgetRemaining,
            DominantTensionType = typeDistribution
                .OrderByDescending(g => g.Count())
                .FirstOrDefault()?.Key.ToString() ?? "none"
        };
    }

    private StabilityHealthMetrics ComputeStabilityMetrics()
    {
        var stabilityReport = _stabilityEngine.AssessStability();
        var counterEvidence = _counterEvidenceDetector.FindCounterEvidence();

        // Compute identity rigidity (how locked-in the system is)
        var active = _historyStore.LoadActive();
        var reinforcedCount = active.Count(c => c.ObservationCount >= 3);
        var identityRigidity = active.Count > 0
            ? (double)reinforcedCount / active.Count
            : 0;

        return new StabilityHealthMetrics
        {
            IsStable = stabilityReport.IsHealthy,
            StabilityStatus = stabilityReport.Status,
            WarningCount = stabilityReport.Warnings.Count,
            AverageConfidence = stabilityReport.AverageConfidence,
            IdentityRigidity = identityRigidity,
            CounterEvidenceCount = counterEvidence.Values.Sum(e => e.Count),
            Warnings = stabilityReport.Warnings.Select(w => w.Message).ToList()
        };
    }

    private OverallHealth ComputeOverallHealth()
    {
        var contradictionMetrics = ComputeContradictionMetrics();
        var narrativeMetrics = ComputeNarrativeMetrics();
        var stabilityMetrics = ComputeStabilityMetrics();

        // Compute health score (0.0 = critical, 1.0 = excellent)
        var scores = new List<double>();

        // Contradiction health: good resolution rate = healthy
        scores.Add(contradictionMetrics.ResolutionRate);

        // Narrative health: balanced = healthy
        scores.Add(narrativeMetrics.BalanceScore);

        // Stability health: stable = healthy
        scores.Add(stabilityMetrics.IsStable ? 1.0 : 0.5);

        // Diversity: high diversity = healthy
        scores.Add(narrativeMetrics.NarrativeDiversity);

        // Counter-evidence: having counter-evidence = healthy
        scores.Add(stabilityMetrics.CounterEvidenceCount > 0 ? 0.8 : 0.4);

        var averageScore = scores.Average();

        return new OverallHealth
        {
            HealthScore = averageScore,
            HealthLevel = ClassifyHealth(averageScore),
            Status = GetHealthStatus(averageScore, contradictionMetrics, narrativeMetrics, stabilityMetrics),
            IsHealthy = averageScore >= 0.5
        };
    }

    private static HealthLevel ClassifyHealth(double score)
    {
        return score switch
        {
            >= 0.8 => HealthLevel.Excellent,
            >= 0.6 => HealthLevel.Good,
            >= 0.4 => HealthLevel.Fair,
            >= 0.2 => HealthLevel.Poor,
            _ => HealthLevel.Critical
        };
    }

    private string GetHealthStatus(
        double score,
        ContradictionHealthMetrics contradictionMetrics,
        NarrativeHealthMetrics narrativeMetrics,
        StabilityHealthMetrics stabilityMetrics)
    {
        if (score >= 0.8)
            return "Excellent — balanced, diverse, stable cognition";
        if (score >= 0.6)
            return "Good — minor imbalances detected";
        if (score >= 0.4)
            return "Fair — notable imbalances, monitor closely";
        if (score >= 0.2)
            return "Poor — significant issues, intervention needed";

        var issues = new List<string>();
        if (contradictionMetrics.ResolutionRate < 0.2)
            issues.Add("low resolution rate");
        if (narrativeMetrics.BalanceScore < 0.3)
            issues.Add("narrative imbalance");
        if (!stabilityMetrics.IsStable)
            issues.Add("identity instability");
        if (narrativeMetrics.NarrativeDiversity < 0.3)
            issues.Add("low diversity");

        return $"Critical — {string.Join(", ", issues)}";
    }
}

/// <summary>
/// Complete semantic health snapshot.
/// </summary>
public class SemanticHealthSnapshot
{
    public DateTimeOffset ComputedAt { get; set; }
    public ContradictionHealthMetrics ContradictionMetrics { get; set; } = new();
    public InterventionHealthMetrics InterventionMetrics { get; set; } = new();
    public NarrativeHealthMetrics NarrativeMetrics { get; set; } = new();
    public StabilityHealthMetrics StabilityMetrics { get; set; } = new();
    public OverallHealth OverallHealth { get; set; } = new();
}

public class ContradictionHealthMetrics
{
    public int TotalContradictions { get; set; }
    public int ActiveContradictions { get; set; }
    public int ResolvedContradictions { get; set; }
    public double ResolutionRate { get; set; }
    public double AverageObservationsPerContradiction { get; set; }
    public int WorseningCount { get; set; }
    public int ImprovingCount { get; set; }
    public int RecurringCount { get; set; }
    public Dictionary<string, int> TypeDistribution { get; set; } = new();
}

public class InterventionHealthMetrics
{
    public int TotalInterventions { get; set; }
    public int RecentInterventions { get; set; }
    public int PendingInterventions { get; set; }
    public int DismissedInterventions { get; set; }
    public int ActedInterventions { get; set; }
    public double DismissalRate { get; set; }
    public double ActionRate { get; set; }
    public double AverageSeverity { get; set; }
}

public class NarrativeHealthMetrics
{
    public double BalanceScore { get; set; }
    public double NarrativeDiversity { get; set; }
    public bool IsBalanced { get; set; }
    public string BalanceStatus { get; set; } = string.Empty;
    public int DailyBudgetRemaining { get; set; }
    public string DominantTensionType { get; set; } = string.Empty;
}

public class StabilityHealthMetrics
{
    public bool IsStable { get; set; }
    public string StabilityStatus { get; set; } = string.Empty;
    public int WarningCount { get; set; }
    public double AverageConfidence { get; set; }
    public double IdentityRigidity { get; set; }
    public int CounterEvidenceCount { get; set; }
    public List<string> Warnings { get; set; } = new();
}

public class OverallHealth
{
    public double HealthScore { get; set; }
    public HealthLevel HealthLevel { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsHealthy { get; set; }
}

public enum HealthLevel
{
    Excellent,
    Good,
    Fair,
    Poor,
    Critical
}
