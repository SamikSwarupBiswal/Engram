using Microsoft.Extensions.Logging;

namespace Engram.Store.Metabolism;

/// <summary>
/// Prevents recursive identity distortion.
/// 
/// The organism can now recursively influence itself. This creates risks:
/// 1. Bad contradictions reinforce themselves
/// 2. False identities emerge from repeated retrieval
/// 3. Recursive pessimism (only negative context injected)
/// 4. Narrative lock-in (early mistaken interpretations persist)
/// 
/// This engine tracks identity claims and their confidence,
/// detects when a single narrative dominates,
/// and enforces narrative diversity.
/// </summary>
public class IdentityStabilityEngine
{
    private readonly ContradictionHistoryStore _historyStore;
    private readonly ReflectionConfidenceModel _confidenceModel;
    private readonly ILogger<IdentityStabilityEngine>? _logger;

    /// <summary>Maximum percentage of prompts that can contain the same tension.</summary>
    public double MaxTensionDominance { get; set; } = 0.4;

    /// <summary>Minimum confidence threshold for injecting into prompts.</summary>
    public double MinConfidenceForInjection { get; set; } = 0.2;

    /// <summary>Maximum number of tensions to inject per prompt.</summary>
    public int MaxTensionsPerPrompt { get; set; } = 2;

    public IdentityStabilityEngine(
        ContradictionHistoryStore historyStore,
        ReflectionConfidenceModel confidenceModel,
        ILogger<IdentityStabilityEngine>? logger = null)
    {
        _historyStore = historyStore;
        _confidenceModel = confidenceModel;
        _logger = logger;
    }

    /// <summary>
    /// Get tensions safe for prompt injection.
    /// Filters by confidence, applies diversity constraints.
    /// </summary>
    public List<StableTension> GetStableTensions()
    {
        var active = _historyStore.LoadActive();
        var stableTensions = new List<StableTension>();

        foreach (var record in active)
        {
            var confidence = _confidenceModel.ComputeReflectionConfidence(record);

            // Skip low-confidence tensions (could be noise)
            if (confidence.AdjustedConfidence < MinConfidenceForInjection)
                continue;

            stableTensions.Add(new StableTension
            {
                ContradictionId = record.ContradictionId,
                Type = record.Type,
                DeclaredIntent = record.DeclaredIntent,
                Confidence = confidence,
                ObservationCount = record.ObservationCount,
                Trend = record.Trend,
                Severity = record.CurrentSeverity,
                DaysActive = confidence.DaysActive,
                IsReinforced = record.ObservationCount >= 3,
                StabilityScore = ComputeStabilityScore(record, confidence)
            });
        }

        // Sort by stability score (not just severity)
        return stableTensions
            .OrderByDescending(t => t.StabilityScore)
            .Take(MaxTensionsPerPrompt * 2) // Keep more than needed for diversity selection
            .ToList();
    }

    /// <summary>
    /// Select tensions for a specific prompt, enforcing diversity.
    /// Prevents the same tensions from dominating every prompt.
    /// </summary>
    public List<StableTension> SelectTensionsForPrompt(List<string>? recentlyInjectedIds = null)
    {
        var stable = GetStableTensions();
        var selected = new List<StableTension>();
        var usedTypes = new HashSet<ContradictionType>();

        foreach (var tension in stable)
        {
            if (selected.Count >= MaxTensionsPerPrompt)
                break;

            // Skip if this exact tension was recently injected (cooldown)
            if (recentlyInjectedIds?.Contains(tension.ContradictionId) == true)
                continue;

            // Enforce type diversity: don't inject two of the same type
            if (usedTypes.Contains(tension.Type))
                continue;

            selected.Add(tension);
            usedTypes.Add(tension.Type);
        }

        return selected;
    }

    /// <summary>
    /// Check if the system is in a healthy narrative state.
    /// Returns warnings if identity distortion is detected.
    /// </summary>
    public IdentityStabilityReport AssessStability()
    {
        var active = _historyStore.LoadActive();
        var report = new IdentityStabilityReport();

        if (active.Count == 0)
        {
            report.IsHealthy = true;
            report.Status = "No active tensions — neutral state";
            return report;
        }

        // Check 1: Tension type dominance
        var typeGroups = active.GroupBy(c => c.Type);
        foreach (var group in typeGroups)
        {
            var ratio = (double)group.Count() / active.Count;
            if (ratio > MaxTensionDominance)
            {
                report.Warnings.Add(new StabilityWarning
                {
                    Type = WarningType.TypeDominance,
                    Message = $"Contradiction type {group.Key} dominates ({ratio:P0} of active tensions)",
                    Severity = WarningSeverity.Medium,
                    RelatedType = group.Key
                });
            }
        }

        // Check 2: Low average confidence (system might be hallucinating patterns)
        var avgConfidence = active.Average(c =>
            _confidenceModel.ComputeReflectionConfidence(c).AdjustedConfidence);
        if (avgConfidence < 0.3)
        {
            report.Warnings.Add(new StabilityWarning
            {
                Type = WarningType.LowConfidence,
                Message = $"Average tension confidence is low ({avgConfidence:F2}). System may be hallucinating patterns.",
                Severity = WarningSeverity.High
            });
        }

        // Check 3: Excessive unresolved tensions (cognitive overload)
        if (active.Count > 10)
        {
            report.Warnings.Add(new StabilityWarning
            {
                Type = WarningType.CognitiveOverload,
                Message = $"{active.Count} active tensions — cognitive overload risk",
                Severity = WarningSeverity.High
            });
        }

        // Check 4: All tensions worsening (recursive negativity)
        var worseningCount = active.Count(c => c.Trend == ContradictionTrend.Worsening);
        if (worseningCount > active.Count * 0.7 && active.Count >= 3)
        {
            report.Warnings.Add(new StabilityWarning
            {
                Type = WarningType.RecursiveNegativity,
                Message = $"{worseningCount}/{active.Count} tensions worsening — recursive negativity risk",
                Severity = WarningSeverity.Critical
            });
        }

        // Check 5: No improving tensions (system can't recognize recovery)
        var improvingCount = active.Count(c => c.Trend == ContradictionTrend.Improving);
        if (improvingCount == 0 && active.Count >= 5)
        {
            report.Warnings.Add(new StabilityWarning
            {
                Type = WarningType.NoRecovery,
                Message = "No improving tensions detected — system may not recognize recovery",
                Severity = WarningSeverity.Medium
            });
        }

        report.IsHealthy = report.Warnings.Count == 0;
        report.Status = report.IsHealthy
            ? "Identity stable — balanced narrative"
            : $"{report.Warnings.Count} stability warnings detected";
        report.ActiveTensionCount = active.Count;
        report.AverageConfidence = avgConfidence;

        return report;
    }

    /// <summary>
    /// Compute overall stability score for a tension.
    /// Higher = more stable/reliable = safer to inject.
    /// </summary>
    private static double ComputeStabilityScore(
        ContradictionHistoryEntry record,
        ReflectionConfidence confidence)
    {
        var baseScore = confidence.AdjustedConfidence;

        // Bonus for reinforced contradictions (seen multiple times)
        var reinforcementBonus = record.ObservationCount >= 3 ? 0.1 : 0;

        // Bonus for stable trend (not fluctuating)
        var trendBonus = record.Trend switch
        {
            ContradictionTrend.Stable => 0.05,
            ContradictionTrend.Recurring => 0.0, // Recurring is ambiguous
            ContradictionTrend.Worsening => -0.05, // Worsening is concerning
            ContradictionTrend.Improving => 0.1, // Improving is good
            _ => 0
        };

        // Penalty for very old unresolved tensions (possible lock-in)
        var agePenalty = confidence.DaysActive > 30 ? -0.1 : 0;

        return Math.Clamp(baseScore + reinforcementBonus + trendBonus + agePenalty, 0, 1);
    }
}

/// <summary>
/// A tension that has been validated for stability and confidence.
/// </summary>
public class StableTension
{
    public string ContradictionId { get; set; } = string.Empty;
    public ContradictionType Type { get; set; }
    public string DeclaredIntent { get; set; } = string.Empty;
    public ReflectionConfidence Confidence { get; set; } = new();
    public int ObservationCount { get; set; }
    public ContradictionTrend Trend { get; set; }
    public ContradictionSeverity Severity { get; set; }
    public double DaysActive { get; set; }
    public bool IsReinforced { get; set; }
    public double StabilityScore { get; set; }
}

/// <summary>
/// Report on identity stability health.
/// </summary>
public class IdentityStabilityReport
{
    public bool IsHealthy { get; set; }
    public string Status { get; set; } = string.Empty;
    public int ActiveTensionCount { get; set; }
    public double AverageConfidence { get; set; }
    public List<StabilityWarning> Warnings { get; set; } = new();
}

public class StabilityWarning
{
    public WarningType Type { get; set; }
    public string Message { get; set; } = string.Empty;
    public WarningSeverity Severity { get; set; }
    public ContradictionType? RelatedType { get; set; }
}

public enum WarningType
{
    TypeDominance,       // One contradiction type dominates
    LowConfidence,       // Average confidence too low
    CognitiveOverload,   // Too many active tensions
    RecursiveNegativity, // All tensions worsening
    NoRecovery           // System can't recognize improvement
}

public enum WarningSeverity
{
    Low,
    Medium,
    High,
    Critical
}
