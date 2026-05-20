using Microsoft.Extensions.Logging;

namespace Engram.Store.Metabolism;

/// <summary>
/// Multiple competing interpretations for behavioral observations.
/// 
/// NOT single deterministic self-story.
/// 
/// Example: Low activity could mean:
/// - burnout
/// - distraction
/// - recovery
/// - exploration
/// - context switching
/// 
/// Without narrative diversity, Engram becomes psychologically brittle.
/// It locks into one interpretation and can't see alternatives.
/// </summary>
public class NarrativeInterpretationEngine
{
    private readonly ILogger<NarrativeInterpretationEngine>? _logger;

    /// <summary>Maximum interpretations to track per observation.</summary>
    public int MaxInterpretationsPerObservation { get; set; } = 3;

    public NarrativeInterpretationEngine(
        ILogger<NarrativeInterpretationEngine>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Generate multiple interpretations for a behavioral contradiction.
    /// Each interpretation has a plausibility score.
    /// </summary>
    public List<NarrativeInterpretation> GenerateInterpretations(
        BehavioralContradiction contradiction,
        List<CounterEvidence>? counterEvidence = null)
    {
        var interpretations = contradiction.Type switch
        {
            ContradictionType.GoalActivityGap => InterpretGoalActivityGap(contradiction, counterEvidence),
            ContradictionType.PriorityDrift => InterpretPriorityDrift(contradiction, counterEvidence),
            ContradictionType.AbandonedCommitment => InterpretAbandonedCommitment(contradiction, counterEvidence),
            ContradictionType.IdentityBehaviorGap => InterpretIdentityBehaviorGap(contradiction, counterEvidence),
            _ => new List<NarrativeInterpretation>()
        };

        // Sort by plausibility and take top N
        return interpretations
            .OrderByDescending(i => i.Plausibility)
            .Take(MaxInterpretationsPerObservation)
            .ToList();
    }

    /// <summary>
    /// Generate interpretations for a ContradictionHistoryEntry.
    /// </summary>
    public List<NarrativeInterpretation> GenerateInterpretations(
        ContradictionHistoryEntry record,
        List<CounterEvidence>? counterEvidence = null)
    {
        var contradiction = new BehavioralContradiction
        {
            Type = record.Type,
            Severity = record.CurrentSeverity,
            DeclaredIntent = record.DeclaredIntent,
            ObservedBehavior = record.Observations.LastOrDefault()?.ObservedBehavior ?? string.Empty,
            Description = record.Observations.LastOrDefault()?.Description ?? string.Empty
        };

        return GenerateInterpretations(contradiction, counterEvidence);
    }

    private List<NarrativeInterpretation> InterpretGoalActivityGap(
        BehavioralContradiction contradiction,
        List<CounterEvidence>? counterEvidence)
    {
        var interpretations = new List<NarrativeInterpretation>();

        // Interpretation 1: Burnout / Overwhelm
        interpretations.Add(new NarrativeInterpretation
        {
            Narrative = "burnout",
            Description = $"User may be burned out or overwhelmed, causing them to avoid '{contradiction.DeclaredIntent}'",
            Plausibility = 0.6,
            SupportingEvidence = "Low activity on goal despite high overall activity",
            CounterEvidence = counterEvidence?.Where(e => e.Type == CounterEvidenceType.RecentActivity).Select(e => e.Description).ToList() ?? new()
        });

        // Interpretation 2: Exploratory Phase
        interpretations.Add(new NarrativeInterpretation
        {
            Narrative = "exploration",
            Description = $"User is in an exploratory phase, gathering context before returning to '{contradiction.DeclaredIntent}'",
            Plausibility = 0.5,
            SupportingEvidence = "High activity on other concepts suggests active exploration",
            CounterEvidence = counterEvidence?.Where(e => e.Type == CounterEvidenceType.RelatedActivity).Select(e => e.Description).ToList() ?? new()
        });

        // Interpretation 3: Priority Shift (legitimate)
        interpretations.Add(new NarrativeInterpretation
        {
            Narrative = "priority_shift",
            Description = $"User has genuinely shifted priorities away from '{contradiction.DeclaredIntent}'",
            Plausibility = 0.4,
            SupportingEvidence = "Extended period of low goal activity",
            CounterEvidence = counterEvidence?.Where(e => e.Type == CounterEvidenceType.SalienceRecovery).Select(e => e.Description).ToList() ?? new()
        });

        // Interpretation 4: Context Switching
        interpretations.Add(new NarrativeInterpretation
        {
            Narrative = "context_switching",
            Description = $"User is context-switching between projects, will return to '{contradiction.DeclaredIntent}'",
            Plausibility = 0.3,
            SupportingEvidence = "Multiple active concepts suggest multi-project work",
            CounterEvidence = new List<string>()
        });

        return interpretations;
    }

    private List<NarrativeInterpretation> InterpretPriorityDrift(
        BehavioralContradiction contradiction,
        List<CounterEvidence>? counterEvidence)
    {
        var interpretations = new List<NarrativeInterpretation>();

        // Interpretation 1: Legitimate Reprioritization
        interpretations.Add(new NarrativeInterpretation
        {
            Narrative = "reprioritization",
            Description = $"User has legitimately reprioritized, moving away from '{contradiction.DeclaredIntent}'",
            Plausibility = 0.6,
            SupportingEvidence = "Consistent focus on different activities",
            CounterEvidence = counterEvidence?.Where(e => e.Type == CounterEvidenceType.RelatedActivity).Select(e => e.Description).ToList() ?? new()
        });

        // Interpretation 2: Temporary Distraction
        interpretations.Add(new NarrativeInterpretation
        {
            Narrative = "distraction",
            Description = $"User is temporarily distracted from '{contradiction.DeclaredIntent}' but will return",
            Plausibility = 0.5,
            SupportingEvidence = "Recent activity on other topics",
            CounterEvidence = new List<string>()
        });

        // Interpretation 3: Hidden Progress
        interpretations.Add(new NarrativeInterpretation
        {
            Narrative = "hidden_progress",
            Description = $"User is making progress on '{contradiction.DeclaredIntent}' in ways not captured by the system",
            Plausibility = 0.3,
            SupportingEvidence = "System may not capture all forms of progress",
            CounterEvidence = counterEvidence?.Where(e => e.Type == CounterEvidenceType.BehaviorMatch).Select(e => e.Description).ToList() ?? new()
        });

        return interpretations;
    }

    private List<NarrativeInterpretation> InterpretAbandonedCommitment(
        BehavioralContradiction contradiction,
        List<CounterEvidence>? counterEvidence)
    {
        var interpretations = new List<NarrativeInterpretation>();

        // Interpretation 1: Genuinely Abandoned
        interpretations.Add(new NarrativeInterpretation
        {
            Narrative = "abandoned",
            Description = $"User has genuinely abandoned the commitment to '{contradiction.DeclaredIntent}'",
            Plausibility = 0.5,
            SupportingEvidence = "No follow-up activity for extended period",
            CounterEvidence = counterEvidence?.Where(e => e.Type == CounterEvidenceType.RecentActivity).Select(e => e.Description).ToList() ?? new()
        });

        // Interpretation 2: Deferred (not abandoned)
        interpretations.Add(new NarrativeInterpretation
        {
            Narrative = "deferred",
            Description = $"User has deferred '{contradiction.DeclaredIntent}', not abandoned it",
            Plausibility = 0.6,
            SupportingEvidence = "Commitment still exists in memory, not explicitly cancelled",
            CounterEvidence = new List<string>()
        });

        // Interpretation 3: External Blocker
        interpretations.Add(new NarrativeInterpretation
        {
            Narrative = "blocked",
            Description = $"User is blocked on '{contradiction.DeclaredIntent}' by external factors",
            Plausibility = 0.3,
            SupportingEvidence = "Commitment exists but no progress — possible blocker",
            CounterEvidence = new List<string>()
        });

        return interpretations;
    }

    private List<NarrativeInterpretation> InterpretIdentityBehaviorGap(
        BehavioralContradiction contradiction,
        List<CounterEvidence>? counterEvidence)
    {
        var interpretations = new List<NarrativeInterpretation>();

        // Interpretation 1: Aspirational Identity
        interpretations.Add(new NarrativeInterpretation
        {
            Narrative = "aspirational",
            Description = $"'{contradiction.DeclaredIntent}' is an aspirational preference, not a current behavior",
            Plausibility = 0.7,
            SupportingEvidence = "Preference declared but not actively practiced",
            CounterEvidence = counterEvidence?.Where(e => e.Type == CounterEvidenceType.BehaviorMatch).Select(e => e.Description).ToList() ?? new()
        });

        // Interpretation 2: Context-Dependent
        interpretations.Add(new NarrativeInterpretation
        {
            Narrative = "context_dependent",
            Description = $"'{contradiction.DeclaredIntent}' is relevant only in certain contexts not recently encountered",
            Plausibility = 0.4,
            SupportingEvidence = "Preference may apply to specific situations",
            CounterEvidence = new List<string>()
        });

        // Interpretation 3: Outdated Identity
        interpretations.Add(new NarrativeInterpretation
        {
            Narrative = "outdated",
            Description = $"'{contradiction.DeclaredIntent}' is an outdated preference that no longer applies",
            Plausibility = 0.3,
            SupportingEvidence = "No related activity despite opportunity",
            CounterEvidence = new List<string>()
        });

        return interpretations;
    }
}

/// <summary>
/// A possible interpretation of a behavioral observation.
/// </summary>
public class NarrativeInterpretation
{
    public string Narrative { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double Plausibility { get; set; }
    public string SupportingEvidence { get; set; } = string.Empty;
    public List<string> CounterEvidence { get; set; } = new();
}
