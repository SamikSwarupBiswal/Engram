namespace Engram.Store.Metabolism;

/// <summary>
/// Confidence-weighted self-modeling.
/// 
/// Every contradiction/intervention needs:
/// - confidence: how sure are we this is real?
/// - reinforcement count: how many times observed?
/// - counter-evidence: what contradicts this interpretation?
/// - temporal stability: how long has this interpretation persisted?
/// 
/// Without this, Engram starts hallucinating personality structure.
/// A contradiction seen once should NOT have the same weight as one seen 20 times.
/// A contradiction with counter-evidence should be weighted less than one without.
/// </summary>
public class ReflectionConfidenceModel
{
    /// <summary>Weight for observation count in confidence scoring.</summary>
    public double ObservationWeight { get; set; } = 0.35;

    /// <summary>Weight for temporal stability in confidence scoring.</summary>
    public double TemporalWeight { get; set; } = 0.25;

    /// <summary>Weight for counter-evidence in confidence scoring.</summary>
    public double CounterEvidenceWeight { get; set; } = 0.25;

    /// <summary>Weight for source diversity in confidence scoring.</summary>
    public double SourceDiversityWeight { get; set; } = 0.15;

    /// <summary>
    /// Compute confidence for a contradiction record.
    /// Returns 0.0 (no confidence) to 1.0 (high confidence).
    /// </summary>
    public double ComputeConfidence(ContradictionHistoryEntry record)
    {
        var observationScore = ComputeObservationScore(record);
        var temporalScore = ComputeTemporalScore(record);
        var counterEvidenceScore = ComputeCounterEvidenceScore(record);
        var sourceDiversityScore = ComputeSourceDiversityScore(record);

        return (observationScore * ObservationWeight) +
               (temporalScore * TemporalWeight) +
               (counterEvidenceScore * CounterEvidenceWeight) +
               (sourceDiversityScore * SourceDiversityWeight);
    }

    /// <summary>
    /// Compute confidence for a reflection (contradiction + context).
    /// </summary>
    public ReflectionConfidence ComputeReflectionConfidence(
        ContradictionHistoryEntry record,
        List<CounterEvidence>? counterEvidence = null)
    {
        var baseConfidence = ComputeConfidence(record);
        var counterEvidencePenalty = counterEvidence?.Count > 0
            ? Math.Min(0.5, counterEvidence.Count * 0.1)
            : 0;

        var adjustedConfidence = Math.Max(0.05, baseConfidence - counterEvidencePenalty);

        return new ReflectionConfidence
        {
            ContradictionId = record.ContradictionId,
            BaseConfidence = baseConfidence,
            CounterEvidenceCount = counterEvidence?.Count ?? 0,
            CounterEvidencePenalty = counterEvidencePenalty,
            AdjustedConfidence = adjustedConfidence,
            ConfidenceLevel = ClassifyConfidence(adjustedConfidence),
            ObservationCount = record.ObservationCount,
            DaysActive = (DateTimeOffset.UtcNow - record.FirstSeenAt).TotalDays,
            DaysSinceLastSeen = (DateTimeOffset.UtcNow - record.LastSeenAt).TotalDays
        };
    }

    /// <summary>
    /// Observation score: more observations = higher confidence, with diminishing returns.
    /// 1 observation = 0.1, 5 = 0.5, 10 = 0.7, 20+ = 0.9
    /// </summary>
    private static double ComputeObservationScore(ContradictionHistoryEntry record)
    {
        // Logarithmic scaling with diminishing returns
        if (record.ObservationCount <= 0) return 0;
        return Math.Min(1.0, Math.Log2(record.ObservationCount + 1) / 5.0);
    }

    /// <summary>
    /// Temporal score: longer consistent observation = higher confidence.
    /// But stale observations (not seen recently) reduce confidence.
    /// </summary>
    private static double ComputeTemporalScore(ContradictionHistoryEntry record)
    {
        var daysActive = (DateTimeOffset.UtcNow - record.FirstSeenAt).TotalDays;
        var daysSinceLastSeen = (DateTimeOffset.UtcNow - record.LastSeenAt).TotalDays;

        // Stability: longer active = more stable (capped at 30 days)
        var stabilityScore = Math.Min(1.0, daysActive / 30.0);

        // Recency penalty: stale observations reduce confidence
        var recencyPenalty = daysSinceLastSeen switch
        {
            < 1 => 0.0,     // Seen today: no penalty
            < 7 => 0.1,     // Seen this week: small penalty
            < 14 => 0.25,   // Seen this month: moderate penalty
            < 30 => 0.4,    // Seen this quarter: significant penalty
            _ => 0.6        // Older: major penalty
        };

        return Math.Max(0.1, stabilityScore - recencyPenalty);
    }

    /// <summary>
    /// Counter-evidence score: more counter-evidence = lower confidence.
    /// Inverse relationship: 0 counter-evidence = 1.0, 5+ = 0.2
    /// </summary>
    private static double ComputeCounterEvidenceScore(ContradictionHistoryEntry record)
    {
        // Use observation count as a proxy for now
        // Real counter-evidence tracking will be added with CounterEvidenceDetector
        var resolutionCount = record.Status == ContradictionStatus.Resolved ? 1 : 0;
        return Math.Max(0.1, 1.0 - (resolutionCount * 0.3));
    }

    /// <summary>
    /// Source diversity score: evidence from multiple observation types = higher confidence.
    /// </summary>
    private static double ComputeSourceDiversityScore(ContradictionHistoryEntry record)
    {
        if (record.Observations.Count == 0) return 0;

        // Check if observations have different severity levels (indicates varied evidence)
        var distinctSeverities = record.Observations
            .Select(o => o.Severity)
            .Distinct()
            .Count();

        // More distinct severities = more diverse evidence
        return Math.Min(1.0, distinctSeverities / 3.0);
    }

    /// <summary>
    /// Classify confidence into human-readable levels.
    /// </summary>
    private static ConfidenceLevel ClassifyConfidence(double confidence)
    {
        return confidence switch
        {
            >= 0.8 => ConfidenceLevel.High,
            >= 0.5 => ConfidenceLevel.Medium,
            >= 0.25 => ConfidenceLevel.Low,
            _ => ConfidenceLevel.Speculative
        };
    }
}

/// <summary>
/// Detailed confidence assessment for a reflection.
/// </summary>
public class ReflectionConfidence
{
    public string ContradictionId { get; set; } = string.Empty;
    public double BaseConfidence { get; set; }
    public int CounterEvidenceCount { get; set; }
    public double CounterEvidencePenalty { get; set; }
    public double AdjustedConfidence { get; set; }
    public ConfidenceLevel ConfidenceLevel { get; set; }
    public int ObservationCount { get; set; }
    public double DaysActive { get; set; }
    public double DaysSinceLastSeen { get; set; }
}

public enum ConfidenceLevel
{
    Speculative, // Very low confidence, could be noise
    Low,         // Some evidence but uncertain
    Medium,      // Reasonable evidence
    High         // Strong evidence, high confidence
}
