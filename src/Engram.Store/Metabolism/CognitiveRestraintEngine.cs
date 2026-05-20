using Microsoft.Extensions.Logging;

namespace Engram.Store.Metabolism;

/// <summary>
/// The Cognitive Restraint Engine — controls when Engram should speak.
/// 
/// A smart organism that speaks too much becomes psychologically exhausting.
/// This engine gates interventions based on:
/// - Confidence thresholds (don't speak when uncertain)
/// - Silence thresholds (respect quiet periods)
/// - Interruption discipline (don't interrupt flow states)
/// - Timing intelligence (know when feedback is welcome)
/// - Accuracy history (stay silent on modes with poor track record)
/// 
/// Without this, users uninstall it.
/// </summary>
public class CognitiveRestraintEngine
{
    private readonly ILogger<CognitiveRestraintEngine>? _logger;
    private readonly RestraintPolicy _policy;
    private readonly List<RestraintDecision> _decisions = new();
    private readonly object _lock = new();

    /// <summary>Timestamp of last intervention allowed by this engine.</summary>
    private DateTimeOffset _lastInterventionAt = DateTimeOffset.MinValue;

    /// <summary>Number of consecutive suppressions.</summary>
    private int _consecutiveSuppressions;

    public CognitiveRestraintEngine(
        RestraintPolicy? policy = null,
        ILogger<CognitiveRestraintEngine>? logger = null)
    {
        _policy = policy ?? new RestraintPolicy();
        _logger = logger;
    }

    /// <summary>
    /// Should Engram speak now? Returns a gate decision.
    /// Call this before generating any intervention or interpretation.
    /// </summary>
    public RestraintDecision ShouldSpeak(RestraintContext context)
    {
        var decision = Evaluate(context);

        lock (_lock)
        {
            _decisions.Add(decision);
            if (decision.Allow)
            {
                _lastInterventionAt = DateTimeOffset.UtcNow;
                _consecutiveSuppressions = 0;
            }
            else
            {
                _consecutiveSuppressions++;
            }
        }

        if (!decision.Allow)
        {
            _logger?.LogDebug("Restraint: suppressed ({Reason}). Consecutive: {Count}",
                decision.Reason, _consecutiveSuppressions);
        }

        return decision;
    }

    private RestraintDecision Evaluate(RestraintContext context)
    {
        // 1. Confidence gate — don't speak when uncertain
        if (context.InterpretationConfidence < _policy.MinConfidenceThreshold)
        {
            return RestraintDecision.SuppressDecision(
                $"Confidence {context.InterpretationConfidence:F2} below threshold {_policy.MinConfidenceThreshold:F2}",
                RestraintReason.LowConfidence);
        }

        // 2. Silence threshold — respect quiet periods after last intervention
        var timeSinceLastIntervention = DateTimeOffset.UtcNow - _lastInterventionAt;
        if (timeSinceLastIntervention < _policy.MinSilenceBetweenInterventions)
        {
            return RestraintDecision.SuppressDecision(
                $"Only {timeSinceLastIntervention.TotalSeconds:F0}s since last intervention (min: {_policy.MinSilenceBetweenInterventions.TotalSeconds:F0}s)",
                RestraintReason.SilenceThreshold);
        }

        // 3. Flow state protection — don't interrupt deep work
        if (context.CurrentBehavioralMode == "deep_work" && !_policy.AllowDeepWorkInterruptions)
        {
            return RestraintDecision.SuppressDecision(
                "User is in deep work mode — do not interrupt",
                RestraintReason.FlowStateProtection);
        }

        // 4. Accuracy gate — stay silent on modes with poor accuracy
        if (context.ModeAccuracyRate >= 0 && context.ModeAccuracyRate < _policy.MinAccuracyForIntervention)
        {
            return RestraintDecision.SuppressDecision(
                $"Mode '{context.CurrentBehavioralMode}' accuracy {context.ModeAccuracyRate:P0} below threshold {_policy.MinAccuracyForIntervention:P0}",
                RestraintReason.LowAccuracy);
        }

        // 5. Over-interpretation gate — if mode is flagged as over-interpreted
        if (context.IsOverInterpreted)
        {
            return RestraintDecision.SuppressDecision(
                $"Mode '{context.CurrentBehavioralMode}' is flagged as over-interpreted",
                RestraintReason.OverInterpreted);
        }

        // 6. Calibration gate — if mode has been frequently corrected
        if (context.IsFrequentlyCorrected)
        {
            return RestraintDecision.SuppressDecision(
                $"Mode '{context.CurrentBehavioralMode}' has been frequently corrected by human",
                RestraintReason.FrequentlyCorrected);
        }

        // 7. Category ignored — human said ignore this
        if (context.IsCategoryIgnored)
        {
            return RestraintDecision.SuppressDecision(
                "Category has been explicitly ignored by human",
                RestraintReason.CategoryIgnored);
        }

        // 8. Intervention fatigue — too many interventions in a short period
        if (context.InterventionsInLastHour > _policy.MaxInterventionsPerHour)
        {
            return RestraintDecision.SuppressDecision(
                $"Too many interventions: {context.InterventionsInLastHour} in last hour (max: {_policy.MaxInterventionsPerHour})",
                RestraintReason.InterventionFatigue);
        }

        // 9. Consecutive suppression release — if suppressed too many times,
        //    allow high-severity interventions through
        if (_consecutiveSuppressions >= _policy.MaxConsecutiveSuppressions &&
            context.Severity >= RestraintSeverity.High)
        {
            return RestraintDecision.AllowDecision("High severity after prolonged suppression");
        }

        // All gates passed — speak
        return RestraintDecision.AllowDecision("All restraint gates passed");
    }

    /// <summary>
    /// Get restraint statistics.
    /// </summary>
    public RestraintStats GetStats()
    {
        lock (_lock)
        {
            var allowed = _decisions.Count(d => d.Allow);
            var suppressed = _decisions.Count(d => !d.Allow);
            var byReason = _decisions
                .Where(d => !d.Allow)
                .GroupBy(d => d.ReasonCode!.Value)
                .ToDictionary(g => g.Key, g => g.Count());

            return new RestraintStats
            {
                TotalDecisions = _decisions.Count,
                Allowed = allowed,
                Suppressed = suppressed,
                SuppressionRate = _decisions.Count > 0 ? (double)suppressed / _decisions.Count : 0,
                ConsecutiveSuppressions = _consecutiveSuppressions,
                SuppressionReasons = byReason
            };
        }
    }

    /// <summary>
    /// Reset stats (for testing).
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _decisions.Clear();
            _lastInterventionAt = DateTimeOffset.MinValue;
            _consecutiveSuppressions = 0;
        }
    }
}

/// <summary>
/// Context for a restraint decision.
/// </summary>
public record RestraintContext
{
    /// <summary>Current behavioral mode (from perception).</summary>
    public string CurrentBehavioralMode { get; init; } = string.Empty;

    /// <summary>Confidence in the current interpretation (0-1).</summary>
    public double InterpretationConfidence { get; init; }

    /// <summary>Accuracy rate for this mode (-1 if no data).</summary>
    public double ModeAccuracyRate { get; init; } = -1;

    /// <summary>Whether this mode is flagged as over-interpreted.</summary>
    public bool IsOverInterpreted { get; init; }

    /// <summary>Whether this mode has been frequently corrected by humans.</summary>
    public bool IsFrequentlyCorrected { get; init; }

    /// <summary>Whether this category has been explicitly ignored.</summary>
    public bool IsCategoryIgnored { get; init; }

    /// <summary>Number of interventions in the last hour.</summary>
    public int InterventionsInLastHour { get; init; }

    /// <summary>Severity of the proposed intervention.</summary>
    public RestraintSeverity Severity { get; init; }
}

/// <summary>
/// Severity levels for restraint decisions.
/// </summary>
public enum RestraintSeverity
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// Reasons for suppression.
/// </summary>
public enum RestraintReason
{
    LowConfidence,
    SilenceThreshold,
    FlowStateProtection,
    LowAccuracy,
    OverInterpreted,
    FrequentlyCorrected,
    CategoryIgnored,
    InterventionFatigue
}

/// <summary>
/// A restraint decision — allow or suppress.
/// </summary>
public record RestraintDecision
{
    public bool Allow { get; init; }
    public string Reason { get; init; } = string.Empty;
    public RestraintReason? ReasonCode { get; init; }

    public static RestraintDecision AllowDecision(string reason) => new()
    {
        Allow = true,
        Reason = reason
    };

    public static RestraintDecision SuppressDecision(string reason, RestraintReason code) => new()
    {
        Allow = false,
        Reason = reason,
        ReasonCode = code
    };
}

/// <summary>
/// Configuration policy for the restraint engine.
/// </summary>
public record RestraintPolicy
{
    /// <summary>Minimum confidence to speak (0-1).</summary>
    public double MinConfidenceThreshold { get; init; } = 0.5;

    /// <summary>Minimum time between interventions.</summary>
    public TimeSpan MinSilenceBetweenInterventions { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Whether to allow interruptions during deep work.</summary>
    public bool AllowDeepWorkInterruptions { get; init; } = false;

    /// <summary>Minimum accuracy rate for a mode to generate interventions.</summary>
    public double MinAccuracyForIntervention { get; init; } = 0.3;

    /// <summary>Maximum interventions per hour.</summary>
    public int MaxInterventionsPerHour { get; init; } = 3;

    /// <summary>Maximum consecutive suppressions before high-severity bypass.</summary>
    public int MaxConsecutiveSuppressions { get; init; } = 10;
}

/// <summary>
/// Statistics about restraint decisions.
/// </summary>
public record RestraintStats
{
    public int TotalDecisions { get; init; }
    public int Allowed { get; init; }
    public int Suppressed { get; init; }
    public double SuppressionRate { get; init; }
    public int ConsecutiveSuppressions { get; init; }
    public Dictionary<RestraintReason, int> SuppressionReasons { get; init; } = new();
}
