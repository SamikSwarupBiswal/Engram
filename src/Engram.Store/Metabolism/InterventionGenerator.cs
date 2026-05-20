using Engram.Store.Events;
using Engram.Store.Identity;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Metabolism;

/// <summary>
/// The beginning of actual agency.
/// 
/// Right now Engram waits passively. That's incomplete.
/// The InterventionGenerator proactively generates interventions
/// when it detects behavioral contradictions, drift, or tension.
/// 
/// Examples:
/// - "You repeatedly mention wanting deep work, but your timeline shows constant context switching."
/// - "You said this deadline mattered, but no related activity has occurred in 5 days."
/// 
/// THIS is the real product — not memory, not retrieval, not inference.
/// The ability to say: "Hey, I noticed something."
/// </summary>
public class InterventionGenerator
{
    private readonly IdentityStore _identityStore;
    private readonly IEventBus? _eventBus;
    private readonly ILogger<InterventionGenerator>? _logger;

    /// <summary>Minimum severity to generate an intervention.</summary>
    public InterventionThreshold Threshold { get; set; } = InterventionThreshold.Medium;

    public InterventionGenerator(
        IdentityStore identityStore,
        IEventBus? eventBus = null,
        ILogger<InterventionGenerator>? logger = null)
    {
        _identityStore = identityStore;
        _eventBus = eventBus;
        _logger = logger;
    }

    /// <summary>
    /// Generate interventions from behavioral contradictions.
    /// </summary>
    public List<Intervention> GenerateInterventions(List<BehavioralContradiction> contradictions)
    {
        var interventions = new List<Intervention>();

        foreach (var contradiction in contradictions)
        {
            if (!ShouldIntervene(contradiction)) continue;

            var intervention = CreateIntervention(contradiction);
            if (intervention != null)
            {
                interventions.Add(intervention);

                // Emit event
                _eventBus?.Publish(new EventEnvelope
                {
                    EventType = "intervention.generated",
                    Source = "intervention_generator",
                    Payload = intervention
                });
            }
        }

        // Check for tension synthesis (multiple related contradictions)
        var synthesized = SynthesizeTensions(contradictions);
        interventions.AddRange(synthesized);

        _logger?.LogInformation("Generated {Count} interventions from {Contradictions} contradictions",
            interventions.Count, contradictions.Count);

        return interventions;
    }

    /// <summary>
    /// Generate interventions from tension reports.
    /// </summary>
    public List<Intervention> GenerateFromTensions(List<TensionReport> tensions)
    {
        var interventions = new List<Intervention>();

        foreach (var tension in tensions)
        {
            if (tension.Severity < Salience.DriftSeverity.High) continue;

            var intervention = new Intervention
            {
                Type = InterventionType.TensionAlert,
                Severity = MapSeverity(tension.Severity),
                Message = tension.Description,
                Source = tension.Source,
                RelatedNodeId = tension.RelatedNodeId,
                GeneratedAt = DateTimeOffset.UtcNow
            };

            interventions.Add(intervention);

            _eventBus?.Publish(new EventEnvelope
            {
                EventType = "intervention.generated",
                Source = "intervention_generator",
                Payload = intervention
            });
        }

        return interventions;
    }

    /// <summary>
    /// Check if we should intervene based on contradiction severity and threshold.
    /// </summary>
    private bool ShouldIntervene(BehavioralContradiction contradiction)
    {
        return Threshold switch
        {
            InterventionThreshold.Low => true,
            InterventionThreshold.Medium => contradiction.Severity >= ContradictionSeverity.Medium,
            InterventionThreshold.High => contradiction.Severity >= ContradictionSeverity.High,
            InterventionThreshold.Critical => contradiction.Severity >= ContradictionSeverity.Critical,
            _ => contradiction.Severity >= ContradictionSeverity.Medium
        };
    }

    /// <summary>
    /// Create an intervention from a behavioral contradiction.
    /// </summary>
    private Intervention? CreateIntervention(BehavioralContradiction contradiction)
    {
        var message = GenerateMessage(contradiction);
        if (string.IsNullOrWhiteSpace(message)) return null;

        return new Intervention
        {
            Type = MapContradictionType(contradiction.Type),
            Severity = MapSeverity(contradiction.Severity),
            Message = message,
            Source = contradiction.Type.ToString(),
            RelatedNodeId = contradiction.RelatedNodeIds.FirstOrDefault(),
            DeclaredIntent = contradiction.DeclaredIntent,
            ObservedBehavior = contradiction.ObservedBehavior,
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Generate a human-readable intervention message.
    /// </summary>
    private static string GenerateMessage(BehavioralContradiction contradiction)
    {
        return contradiction.Type switch
        {
            ContradictionType.GoalActivityGap =>
                $"Your goal '{contradiction.DeclaredIntent}' seems to be fading " +
                $"(last touched: {contradiction.ObservedBehavior}), " +
                $"while you've been active on other things. Is this still important to you?",

            ContradictionType.PriorityDrift =>
                $"You declared '{contradiction.DeclaredIntent}' as a priority, " +
                $"but your recent activity shows focus on: {contradiction.ObservedBehavior}. " +
                $"Has your priority shifted?",

            ContradictionType.AbandonedCommitment =>
                $"You committed to '{contradiction.DeclaredIntent}', " +
                $"but there's been no follow-up activity. " +
                $"Do you want to revisit this or let it go?",

            ContradictionType.IdentityBehaviorGap =>
                $"You mentioned '{contradiction.DeclaredIntent}' as a preference, " +
                $"but I haven't seen related activity. " +
                $"Is this still relevant to you?",

            _ => contradiction.Description
        };
    }

    /// <summary>
    /// Synthesize multiple related contradictions into a higher-level tension.
    /// </summary>
    private List<Intervention> SynthesizeTensions(List<BehavioralContradiction> contradictions)
    {
        var synthesized = new List<Intervention>();

        // Group by type
        var groups = contradictions.GroupBy(c => c.Type);

        foreach (var group in groups)
        {
            if (group.Count() < 2) continue;

            var related = group.ToList();
            var severity = related.Max(c => c.Severity);

            if (severity < ContradictionSeverity.Medium) continue;

            var intervention = new Intervention
            {
                Type = InterventionType.PatternAlert,
                Severity = MapSeverity(severity),
                Message = $"Pattern detected: {related.Count} similar contradictions in {group.Key}. " +
                          $"Examples: {string.Join("; ", related.Take(2).Select(c => c.DeclaredIntent))}",
                Source = "tension_synthesis",
                GeneratedAt = DateTimeOffset.UtcNow
            };

            synthesized.Add(intervention);
        }

        return synthesized;
    }

    private static InterventionType MapContradictionType(ContradictionType type)
    {
        return type switch
        {
            ContradictionType.GoalActivityGap => InterventionType.GoalDrift,
            ContradictionType.PriorityDrift => InterventionType.PriorityDrift,
            ContradictionType.AbandonedCommitment => InterventionType.CommitmentAlert,
            ContradictionType.IdentityBehaviorGap => InterventionType.IdentityGap,
            _ => InterventionType.General
        };
    }

    private static InterventionSeverity MapSeverity(ContradictionSeverity severity)
    {
        return severity switch
        {
            ContradictionSeverity.Low => InterventionSeverity.Low,
            ContradictionSeverity.Medium => InterventionSeverity.Medium,
            ContradictionSeverity.High => InterventionSeverity.High,
            ContradictionSeverity.Critical => InterventionSeverity.Critical,
            _ => InterventionSeverity.Medium
        };
    }

    private static InterventionSeverity MapSeverity(Salience.DriftSeverity severity)
    {
        return severity switch
        {
            Salience.DriftSeverity.Low => InterventionSeverity.Low,
            Salience.DriftSeverity.Medium => InterventionSeverity.Medium,
            Salience.DriftSeverity.High => InterventionSeverity.High,
            Salience.DriftSeverity.Critical => InterventionSeverity.Critical,
            _ => InterventionSeverity.Medium
        };
    }
}

/// <summary>
/// An intervention — proactive guidance from Engram.
/// </summary>
public class Intervention
{
    public string InterventionId { get; set; } = Guid.NewGuid().ToString("n")[..12];
    public InterventionType Type { get; set; }
    public InterventionSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string? RelatedNodeId { get; set; }
    public string? DeclaredIntent { get; set; }
    public string? ObservedBehavior { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    public InterventionStatus Status { get; set; } = InterventionStatus.Pending;
    public string? UserResponse { get; set; }
    public DateTimeOffset? RespondedAt { get; set; }
}

public enum InterventionType
{
    GoalDrift,           // Goal fading while unrelated activity is high
    PriorityDrift,       // Declared priorities not reflected in behavior
    CommitmentAlert,     // Commitment made but no follow-through
    IdentityGap,         // Identity claims not supported by behavior
    TensionAlert,        // Unresolved tension detected
    PatternAlert,        // Multiple similar contradictions
    General              // General intervention
}

public enum InterventionSeverity
{
    Low,       // Note, not urgent
    Medium,    // Worth mentioning
    High,      // Should be addressed
    Critical   // Requires immediate attention
}

public enum InterventionStatus
{
    Pending,    // Awaiting user acknowledgment
    Acknowledged, // User saw it
    Dismissed,  // User dismissed it
    Acted       // User took action
}

public enum InterventionThreshold
{
    Low,       // Intervene on anything
    Medium,    // Only medium+ severity
    High,      // Only high+ severity
    Critical   // Only critical severity
}
