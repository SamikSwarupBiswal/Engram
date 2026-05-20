using Microsoft.Extensions.Logging;

namespace Engram.Store.Metabolism;

/// <summary>
/// Manages tension evolution over time.
/// 
/// Tensions are NOT static. They need:
/// - Escalation (worsening contradictions get stronger signals)
/// - Decay (old unresolved tensions fade if not reinforced)
/// - Reinforcement (recurring contradictions amplify)
/// - Clustering (related tensions group into patterns)
/// - Resolution (tensions disappear when resolved)
/// 
/// Example: Repeated sleep drift → escalating concern → stronger interventions
/// This becomes adaptive behavioral cognition.
/// </summary>
public class TensionEvolutionEngine
{
    private readonly ContradictionHistoryStore _historyStore;
    private readonly ILogger<TensionEvolutionEngine>? _logger;

    /// <summary>Weight for frequency in importance scoring.</summary>
    public double FrequencyWeight { get; set; } = 0.3;

    /// <summary>Weight for persistence (days active) in importance scoring.</summary>
    public double PersistenceWeight { get; set; } = 0.25;

    /// <summary>Weight for severity in importance scoring.</summary>
    public double SeverityWeight { get; set; } = 0.25;

    /// <summary>Weight for trend (worsening = higher) in importance scoring.</summary>
    public double TrendWeight { get; set; } = 0.2;

    /// <summary>Decay rate per day for unobserved tensions (0.0-1.0).</summary>
    public double DailyDecayRate { get; set; } = 0.02;

    public TensionEvolutionEngine(
        ContradictionHistoryStore historyStore,
        ILogger<TensionEvolutionEngine>? logger = null)
    {
        _historyStore = historyStore;
        _logger = logger;
    }

    /// <summary>
    /// Compute the current importance score for all active tensions.
    /// Higher score = more important = stronger intervention signal.
    /// </summary>
    public List<TensionScore> ScoreActiveTensions()
    {
        var active = _historyStore.LoadActive();
        var scores = new List<TensionScore>();

        foreach (var record in active)
        {
            var score = ComputeImportanceScore(record);
            scores.Add(new TensionScore
            {
                ContradictionId = record.ContradictionId,
                Type = record.Type,
                DeclaredIntent = record.DeclaredIntent,
                ImportanceScore = score,
                Frequency = record.ObservationCount,
                DaysActive = (DateTimeOffset.UtcNow - record.FirstSeenAt).TotalDays,
                CurrentSeverity = record.CurrentSeverity,
                Trend = record.Trend
            });
        }

        return scores.OrderByDescending(s => s.ImportanceScore).ToList();
    }

    /// <summary>
    /// Cluster related tensions into patterns.
    /// Multiple contradictions of the same type → pattern alert.
    /// </summary>
    public List<TensionCluster> ClusterTensions()
    {
        var active = _historyStore.LoadActive();
        var clusters = new List<TensionCluster>();

        // Group by type
        var groups = active.GroupBy(c => c.Type);
        foreach (var group in groups)
        {
            if (group.Count() < 2) continue;

            var records = group.ToList();
            var avgSeverity = records.Average(r => (int)r.CurrentSeverity);
            var totalObservations = records.Sum(r => r.ObservationCount);

            clusters.Add(new TensionCluster
            {
                Type = group.Key,
                ContradictionCount = records.Count,
                TotalObservations = totalObservations,
                AverageSeverity = (ContradictionSeverity)(int)avgSeverity,
                ContradictionIds = records.Select(r => r.ContradictionId).ToList(),
                Pattern = GenerateClusterPattern(records),
                ClusterImportance = records.Sum(r => ComputeImportanceScore(r))
            });
        }

        return clusters.OrderByDescending(c => c.ClusterImportance).ToList();
    }

    /// <summary>
    /// Compute importance score for a single contradiction record.
    /// </summary>
    private double ComputeImportanceScore(ContradictionHistoryEntry record)
    {
        // Frequency score (normalized, diminishing returns)
        var frequencyScore = Math.Min(1.0, record.ObservationCount / 10.0);

        // Persistence score (days active, capped at 30)
        var daysActive = (DateTimeOffset.UtcNow - record.FirstSeenAt).TotalDays;
        var persistenceScore = Math.Min(1.0, daysActive / 30.0);

        // Severity score
        var severityScore = (int)record.CurrentSeverity / 3.0;

        // Trend score (worsening = 1.0, stable = 0.5, improving = 0.0)
        var trendScore = record.Trend switch
        {
            ContradictionTrend.Worsening => 1.0,
            ContradictionTrend.Recurring => 0.8,
            ContradictionTrend.Stable => 0.5,
            ContradictionTrend.Improving => 0.2,
            _ => 0.5
        };

        // Apply decay for stale observations
        var daysSinceLastSeen = (DateTimeOffset.UtcNow - record.LastSeenAt).TotalDays;
        var decayFactor = Math.Max(0.1, 1.0 - (daysSinceLastSeen * DailyDecayRate));

        var rawScore = (frequencyScore * FrequencyWeight) +
                       (persistenceScore * PersistenceWeight) +
                       (severityScore * SeverityWeight) +
                       (trendScore * TrendWeight);

        return rawScore * decayFactor;
    }

    /// <summary>
    /// Generate a human-readable pattern description for a cluster.
    /// </summary>
    private static string GenerateClusterPattern(List<ContradictionHistoryEntry> records)
    {
        var type = records.First().Type;
        var intents = records.Select(r => r.DeclaredIntent).Distinct().Take(3);
        var totalObs = records.Sum(r => r.ObservationCount);

        return type switch
        {
            ContradictionType.GoalActivityGap =>
                $"Pattern: {records.Count} goals fading while unrelated activity is high. " +
                $"Goals: {string.Join(", ", intents)}. Total observations: {totalObs}.",

            ContradictionType.PriorityDrift =>
                $"Pattern: {records.Count} declared priorities not reflected in behavior. " +
                $"Priorities: {string.Join(", ", intents)}. Total observations: {totalObs}.",

            ContradictionType.AbandonedCommitment =>
                $"Pattern: {records.Count} commitments with no follow-through. " +
                $"Commitments: {string.Join(", ", intents)}. Total observations: {totalObs}.",

            ContradictionType.IdentityBehaviorGap =>
                $"Pattern: {records.Count} identity claims unsupported by behavior. " +
                $"Claims: {string.Join(", ", intents)}. Total observations: {totalObs}.",

            _ => $"Pattern: {records.Count} contradictions of type {type}. Total observations: {totalObs}."
        };
    }
}

public class TensionScore
{
    public string ContradictionId { get; set; } = string.Empty;
    public ContradictionType Type { get; set; }
    public string DeclaredIntent { get; set; } = string.Empty;
    public double ImportanceScore { get; set; }
    public int Frequency { get; set; }
    public double DaysActive { get; set; }
    public ContradictionSeverity CurrentSeverity { get; set; }
    public ContradictionTrend Trend { get; set; }
}

public class TensionCluster
{
    public ContradictionType Type { get; set; }
    public int ContradictionCount { get; set; }
    public int TotalObservations { get; set; }
    public ContradictionSeverity AverageSeverity { get; set; }
    public List<string> ContradictionIds { get; set; } = new();
    public string Pattern { get; set; } = string.Empty;
    public double ClusterImportance { get; set; }
}
