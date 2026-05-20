using Microsoft.Extensions.Logging;

namespace Engram.Store.Metabolism;

/// <summary>
/// Emotional tone regulation for the organism.
/// 
/// Prevents:
/// - Constant seriousness
/// - Intervention harshness
/// - Over-analysis tone
/// - Recursive negativity
/// 
/// The organism must be psychologically sustainable.
/// Not just intelligent — human-compatible.
/// </summary>
public class ToneBalanceEngine
{
    private readonly InterventionStore _interventionStore;
    private readonly ContradictionHistoryStore _historyStore;
    private readonly ILogger<ToneBalanceEngine>? _logger;

    /// <summary>Maximum percentage of recent interventions that can be High/Critical severity.</summary>
    public double MaxSevereInterventionRatio { get; set; } = 0.4;

    /// <summary>Minimum positive interventions required per negative intervention.</summary>
    public double PositiveToNegativeRatio { get; set; } = 0.3;

    public ToneBalanceEngine(
        InterventionStore interventionStore,
        ContradictionHistoryStore historyStore,
        ILogger<ToneBalanceEngine>? logger = null)
    {
        _interventionStore = interventionStore;
        _historyStore = historyStore;
        _logger = logger;
    }

    /// <summary>
    /// Compute the emotional tone balance of recent interventions.
    /// Returns 0.0 (all negative/severe) to 1.0 (balanced/positive).
    /// </summary>
    public ToneBalance ComputeToneBalance()
    {
        var recentInterventions = _interventionStore.LoadRecent(TimeSpan.FromDays(7));
        var activeContradictions = _historyStore.LoadActive();

        var balance = new ToneBalance
        {
            ComputedAt = DateTimeOffset.UtcNow,
            RecentInterventionCount = recentInterventions.Count,
            ActiveContradictionCount = activeContradictions.Count
        };

        if (recentInterventions.Count == 0)
        {
            balance.SevereInterventionRatio = 0;
            balance.ToneScore = 1.0; // Neutral = balanced
            balance.IsBalanced = true;
            balance.Status = "No recent interventions — neutral tone";
            return balance;
        }

        // Compute severity distribution
        var severeCount = recentInterventions.Count(i =>
            i.Severity == InterventionSeverity.High ||
            i.Severity == InterventionSeverity.Critical);
        balance.SevereInterventionRatio = (double)severeCount / recentInterventions.Count;

        // Compute tone score
        var severityWeights = recentInterventions.Select(i => i.Severity switch
        {
            InterventionSeverity.Low => 0.2,
            InterventionSeverity.Medium => 0.5,
            InterventionSeverity.High => 0.8,
            InterventionSeverity.Critical => 1.0,
            _ => 0.5
        }).ToList();

        var averageSeverity = severityWeights.Average();
        balance.ToneScore = 1.0 - averageSeverity; // Invert: lower severity = higher tone score

        // Check balance
        balance.IsBalanced = balance.SevereInterventionRatio <= MaxSevereInterventionRatio;
        balance.Status = GetToneStatus(balance);

        return balance;
    }

    /// <summary>
    /// Check if a new intervention should be softened based on current tone.
    /// Returns tone guidance for the intervention.
    /// </summary>
    public ToneGuidance GetToneGuidance(InterventionSeverity proposedSeverity)
    {
        var balance = ComputeToneBalance();

        var guidance = new ToneGuidance
        {
            OriginalSeverity = proposedSeverity,
            ShouldSoften = false,
            SuggestedSeverity = proposedSeverity
        };

        // If tone is already heavily negative, soften new interventions
        if (!balance.IsBalanced)
        {
            guidance.ShouldSoften = true;
            guidance.SuggestedSeverity = proposedSeverity switch
            {
                InterventionSeverity.Critical => InterventionSeverity.High,
                InterventionSeverity.High => InterventionSeverity.Medium,
                InterventionSeverity.Medium => InterventionSeverity.Low,
                _ => proposedSeverity
            };
            guidance.Reason = $"Tone is imbalanced (severe ratio: {balance.SevereInterventionRatio:P0}). Softening intervention.";
        }

        return guidance;
    }

    /// <summary>
    /// Compute the overall emotional tone of the system.
    /// Returns descriptors for the current cognitive atmosphere.
    /// </summary>
    public CognitiveAtmosphere AssessAtmosphere()
    {
        var balance = ComputeToneBalance();
        var active = _historyStore.LoadActive();

        var atmosphere = new CognitiveAtmosphere
        {
            ToneBalance = balance,
            DominantTone = ClassifyDominantTone(balance, active),
            IsSustainable = IsPsychologicallySustainable(balance, active),
            Recommendations = GenerateAtmosphereRecommendations(balance, active)
        };

        return atmosphere;
    }

    private static ToneType ClassifyDominantTone(ToneBalance balance, List<ContradictionHistoryEntry> active)
    {
        if (balance.ToneScore > 0.7)
            return ToneType.Balanced;
        if (balance.ToneScore > 0.5)
            return ToneType.SlightlyCautious;
        if (balance.ToneScore > 0.3)
            return ToneType.Cautious;
        if (active.Any(c => c.Trend == ContradictionTrend.Worsening))
            return ToneType.Corrective;
        return ToneType.Heavy;
    }

    private static bool IsPsychologicallySustainable(ToneBalance balance, List<ContradictionHistoryEntry> active)
    {
        // Unsustainable if: all interventions severe, no recovery, high contradiction count
        if (balance.SevereInterventionRatio > 0.8) return false;
        if (active.Count > 10 && active.All(c => c.Trend != ContradictionTrend.Improving)) return false;
        return true;
    }

    private static List<string> GenerateAtmosphereRecommendations(ToneBalance balance, List<ContradictionHistoryEntry> active)
    {
        var recommendations = new List<string>();

        if (balance.SevereInterventionRatio > 0.5)
            recommendations.Add("Reduce intervention severity — too many critical interventions");

        if (active.Count > 7)
            recommendations.Add("Reduce active tension count — cognitive overload risk");

        if (!active.Any(c => c.Trend == ContradictionTrend.Improving) && active.Count >= 3)
            recommendations.Add("Inject positive signals — no improving tensions detected");

        if (balance.ToneScore < 0.3)
            recommendations.Add("System tone is heavily negative — consider silence window");

        return recommendations;
    }

    private static string GetToneStatus(ToneBalance balance)
    {
        if (balance.ToneScore > 0.7)
            return "Balanced — healthy cognitive atmosphere";
        if (balance.ToneScore > 0.5)
            return "Slightly cautious — monitor tone drift";
        if (balance.ToneScore > 0.3)
            return "Cautious — interventions may feel heavy";
        return "Heavy — psychological sustainability at risk";
    }
}

/// <summary>
/// Emotional tone balance assessment.
/// </summary>
public class ToneBalance
{
    public DateTimeOffset ComputedAt { get; set; }
    public int RecentInterventionCount { get; set; }
    public int ActiveContradictionCount { get; set; }
    public double SevereInterventionRatio { get; set; }
    public double ToneScore { get; set; }
    public bool IsBalanced { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Guidance for toning an intervention.
/// </summary>
public class ToneGuidance
{
    public InterventionSeverity OriginalSeverity { get; set; }
    public InterventionSeverity SuggestedSeverity { get; set; }
    public bool ShouldSoften { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Overall cognitive atmosphere assessment.
/// </summary>
public class CognitiveAtmosphere
{
    public ToneBalance ToneBalance { get; set; } = new();
    public ToneType DominantTone { get; set; }
    public bool IsSustainable { get; set; }
    public List<string> Recommendations { get; set; } = new();
}

public enum ToneType
{
    Balanced,          // Healthy, mixed signals
    SlightlyCautious,  // Leaning corrective but manageable
    Cautious,          // Heavy on corrections
    Corrective,        // Actively addressing problems
    Heavy              // Psychologically unsustainable
}
