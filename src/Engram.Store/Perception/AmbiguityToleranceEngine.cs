using Microsoft.Extensions.Logging;

namespace Engram.Store.Perception;

/// <summary>
/// The Ambiguity Tolerance Engine — teaches Engram when to say "I don't know."
/// 
/// Ground truth ambiguity is the hardest unsolved problem:
/// human behavior is NOT objectively labeled.
/// 
/// 2 hours on YouTube could be:
/// - procrastination
/// - research
/// - decompression
/// - burnout recovery
/// - curiosity
/// - social connection
/// - avoidance
/// 
/// There is NO correct interpretation. The system must learn to:
/// 1. Recognize ambiguous situations
/// 2. Offer multiple interpretations (not just the most negative one)
/// 3. Explicitly state uncertainty
/// 4. Default to "unclassifiable" when confidence is low
/// 5. Never present uncertain interpretations as facts
/// 
/// This is epistemic humility as infrastructure.
/// </summary>
public class AmbiguityToleranceEngine
{
    private readonly ILogger<AmbiguityToleranceEngine>? _logger;
    private readonly List<AmbiguityEvent> _events = new();
    private readonly object _lock = new();

    /// <summary>
    /// Confidence threshold below which an interpretation should be flagged as ambiguous.
    /// </summary>
    public double AmbiguityThreshold { get; set; } = 0.4;

    /// <summary>
    /// Minimum competing interpretations required before an observation is considered ambiguous.
    /// </summary>
    public int MinCompetingInterpretations { get; set; } = 2;

    public AmbiguityToleranceEngine(ILogger<AmbiguityToleranceEngine>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Evaluate whether an observation is ambiguous.
    /// Returns an ambiguity assessment with recommended action.
    /// </summary>
    public AmbiguityAssessment Evaluate(string observation, List<CompetingInterpretation> interpretations)
    {
        if (interpretations.Count == 0)
        {
            return new AmbiguityAssessment
            {
                Observation = observation,
                IsAmbiguous = true,
                AmbiguityLevel = AmbiguityLevel.Complete,
                RecommendedAction = AmbiguityAction.SayUnknown,
                Reason = "No interpretations available",
                CompetingInterpretations = new List<CompetingInterpretation>()
            };
        }

        // Sort by plausibility
        var sorted = interpretations.OrderByDescending(i => i.Plausibility).ToList();
        var topPlausibility = sorted[0].Plausibility;
        var secondPlausibility = sorted.Count > 1 ? sorted[1].Plausibility : 0;

        // Check for ambiguity signals
        var signals = new List<AmbiguitySignal>();

        // Signal 1: Low top plausibility
        if (topPlausibility < AmbiguityThreshold)
        {
            signals.Add(new AmbiguitySignal
            {
                Type = AmbiguitySignalType.LowConfidence,
                Description = $"Top interpretation has low plausibility ({topPlausibility:F2})",
                Weight = 0.4
            });
        }

        // Signal 2: Close competition (top two are within 0.15)
        if (sorted.Count >= 2 && (topPlausibility - secondPlausibility) < 0.15)
        {
            signals.Add(new AmbiguitySignal
            {
                Type = AmbiguitySignalType.CloseCompetition,
                Description = $"Top interpretations are close ({topPlausibility:F2} vs {secondPlausibility:F2})",
                Weight = 0.3
            });
        }

        // Signal 3: Many competing interpretations
        if (sorted.Count >= MinCompetingInterpretations)
        {
            signals.Add(new AmbiguitySignal
            {
                Type = AmbiguitySignalType.MultipleCandidates,
                Description = $"{sorted.Count} competing interpretations exist",
                Weight = 0.2
            });
        }

        // Signal 4: Negative-default bias check
        // If the top interpretation is negative but alternatives exist, flag it
        var negativeInterpretations = new[] { "procrastination", "drift", "avoidance", "abandonment", "instability" };
        if (negativeInterpretations.Any(neg =>
            sorted[0].Narrative.Contains(neg, StringComparison.OrdinalIgnoreCase)) &&
            sorted.Count > 1)
        {
            signals.Add(new AmbiguitySignal
            {
                Type = AmbiguitySignalType.NegativeDefaultBias,
                Description = "Top interpretation is negative but alternatives exist — potential bias",
                Weight = 0.3
            });
        }

        // Calculate ambiguity level
        var totalWeight = signals.Sum(s => s.Weight);
        var ambiguityLevel = totalWeight switch
        {
            >= 0.7 => AmbiguityLevel.High,
            >= 0.4 => AmbiguityLevel.Moderate,
            >= 0.1 => AmbiguityLevel.Low,
            _ => AmbiguityLevel.None
        };

        // Determine recommended action
        var action = ambiguityLevel switch
        {
            AmbiguityLevel.Complete => AmbiguityAction.SayUnknown,
            AmbiguityLevel.High => AmbiguityAction.OfferMultiple,
            AmbiguityLevel.Moderate => AmbiguityAction.StateUncertainty,
            AmbiguityLevel.Low => AmbiguityAction.ProceedWithCaveat,
            _ => AmbiguityAction.ProceedConfidently
        };

        var isAmbiguous = ambiguityLevel >= AmbiguityLevel.Moderate;

        var assessment = new AmbiguityAssessment
        {
            Observation = observation,
            IsAmbiguous = isAmbiguous,
            AmbiguityLevel = ambiguityLevel,
            RecommendedAction = action,
            CompetingInterpretations = sorted,
            Signals = signals,
            Reason = isAmbiguous
                ? $"Ambiguous: {string.Join("; ", signals.Select(s => s.Description))}"
                : "Clear interpretation"
        };

        // Record the event
        lock (_lock)
        {
            _events.Add(new AmbiguityEvent
            {
                Observation = observation,
                IsAmbiguous = isAmbiguous,
                AmbiguityLevel = ambiguityLevel,
                InterpretationCount = interpretations.Count,
                TopPlausibility = topPlausibility,
                Timestamp = DateTimeOffset.UtcNow
            });
        }

        if (isAmbiguous)
        {
            _logger?.LogInformation("Ambiguity detected: {Level} — {Observation} ({Count} interpretations)",
                ambiguityLevel, observation, interpretations.Count);
        }

        return assessment;
    }

    /// <summary>
    /// Format an ambiguity-aware response.
    /// Instead of "You are procrastinating", say "This could be research, decompression, or avoidance."
    /// </summary>
    public string FormatAmbiguousResponse(AmbiguityAssessment assessment)
    {
        return assessment.AmbiguityLevel switch
        {
            AmbiguityLevel.Complete =>
                $"I'm not sure what to make of: {assessment.Observation}. There isn't enough signal to interpret this.",

            AmbiguityLevel.High =>
                $"This could be interpreted multiple ways: " +
                string.Join(", ", assessment.CompetingInterpretations.Take(3).Select(i => i.Narrative)) +
                ". I'm not confident enough to pick one.",

            AmbiguityLevel.Moderate =>
                $"My best guess is {assessment.CompetingInterpretations[0].Narrative}, " +
                $"but it could also be {assessment.CompetingInterpretations[1].Narrative}. " +
                "I'm not certain.",

            AmbiguityLevel.Low =>
                $"This looks like {assessment.CompetingInterpretations[0].Narrative}, " +
                $"though {assessment.CompetingInterpretations[1].Narrative} is also possible.",

            _ =>
                $"This appears to be {assessment.CompetingInterpretations[0].Narrative}."
        };
    }

    /// <summary>
    /// Get ambiguity statistics — how often is the system uncertain?
    /// </summary>
    public AmbiguityStats GetStats()
    {
        lock (_lock)
        {
            var total = _events.Count;
            var ambiguous = _events.Count(e => e.IsAmbiguous);

            return new AmbiguityStats
            {
                TotalEvaluations = total,
                AmbiguousCount = ambiguous,
                AmbiguityRate = total > 0 ? (double)ambiguous / total : 0,
                LevelDistribution = _events
                    .GroupBy(e => e.AmbiguityLevel)
                    .ToDictionary(g => g.Key, g => g.Count()),
                AvgInterpretationsPerObservation = total > 0
                    ? _events.Average(e => e.InterpretationCount)
                    : 0
            };
        }
    }

    /// <summary>
    /// Check if the system is over-confident (too few ambiguous classifications).
    /// A system that NEVER says "I don't know" is hallucinating.
    /// </summary>
    public bool IsOverConfident()
    {
        var stats = GetStats();
        if (stats.TotalEvaluations < 10) return false; // Not enough data

        // If less than 10% of observations are ambiguous, the system is probably over-confident
        return stats.AmbiguityRate < 0.1;
    }
}

/// <summary>
/// Levels of ambiguity.
/// </summary>
public enum AmbiguityLevel
{
    None,       // Clear interpretation
    Low,        // Minor uncertainty
    Moderate,   // Notable uncertainty, should state caveat
    High,       // High uncertainty, should offer multiple interpretations
    Complete    // No meaningful interpretation possible
}

/// <summary>
/// Recommended action for an ambiguous observation.
/// </summary>
public enum AmbiguityAction
{
    ProceedConfidently,  // Interpretation is clear
    ProceedWithCaveat,   // Interpret with uncertainty note
    StateUncertainty,    // Explicitly state uncertainty
    OfferMultiple,       // Present multiple interpretations
    SayUnknown           // Admit inability to interpret
}

/// <summary>
/// A competing interpretation for an observation.
/// </summary>
public record CompetingInterpretation
{
    public string Narrative { get; init; } = string.Empty;
    public double Plausibility { get; init; }
    public string Evidence { get; init; } = string.Empty;
}

/// <summary>
/// Assessment of ambiguity for an observation.
/// </summary>
public record AmbiguityAssessment
{
    public string Observation { get; init; } = string.Empty;
    public bool IsAmbiguous { get; init; }
    public AmbiguityLevel AmbiguityLevel { get; init; }
    public AmbiguityAction RecommendedAction { get; init; }
    public string Reason { get; init; } = string.Empty;
    public List<CompetingInterpretation> CompetingInterpretations { get; init; } = new();
    public List<AmbiguitySignal> Signals { get; init; } = new();
}

/// <summary>
/// A signal that an observation is ambiguous.
/// </summary>
public record AmbiguitySignal
{
    public AmbiguitySignalType Type { get; init; }
    public string Description { get; init; } = string.Empty;
    public double Weight { get; init; }
}

public enum AmbiguitySignalType
{
    LowConfidence,
    CloseCompetition,
    MultipleCandidates,
    NegativeDefaultBias
}

/// <summary>
/// An ambiguity event for tracking.
/// </summary>
public record AmbiguityEvent
{
    public string Observation { get; init; } = string.Empty;
    public bool IsAmbiguous { get; init; }
    public AmbiguityLevel AmbiguityLevel { get; init; }
    public int InterpretationCount { get; init; }
    public double TopPlausibility { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// Statistics about ambiguity tolerance.
/// </summary>
public record AmbiguityStats
{
    public int TotalEvaluations { get; init; }
    public int AmbiguousCount { get; init; }
    public double AmbiguityRate { get; init; }
    public Dictionary<AmbiguityLevel, int> LevelDistribution { get; init; } = new();
    public double AvgInterpretationsPerObservation { get; init; }
}
