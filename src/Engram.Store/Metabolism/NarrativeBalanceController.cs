using Microsoft.Extensions.Logging;

namespace Engram.Store.Metabolism;

/// <summary>
/// Controls the balance of behavioral context in prompts.
/// 
/// Without this, Engram becomes an unbearable life coach.
/// The organism must know when NOT to speak.
/// 
/// Implements:
/// - Intervention cooldowns (don't repeat the same message)
/// - Silence windows (periods where no interventions are generated)
/// - Novelty weighting (new tensions get priority over stale ones)
/// - Intervention budgets (max N interventions per time period)
/// - Positive balance (ensure positive/neutral context alongside negative)
/// </summary>
public class NarrativeBalanceController
{
    private readonly InterventionStore _interventionStore;
    private readonly ContradictionHistoryStore _historyStore;
    private readonly ILogger<NarrativeBalanceController>? _logger;

    /// <summary>Cooldown period before the same tension can be re-injected.</summary>
    public TimeSpan TensionCooldown { get; set; } = TimeSpan.FromHours(12);

    /// <summary>Maximum interventions per 24-hour period.</summary>
    public int DailyInterventionBudget { get; set; } = 5;

    /// <summary>Maximum pending interventions at any time.</summary>
    public int MaxPendingInterventions { get; set; } = 3;

    /// <summary>Hours to suppress interventions after user dismissal.</summary>
    public int DismissalSuppressionHours { get; set; } = 24;

    public NarrativeBalanceController(
        InterventionStore interventionStore,
        ContradictionHistoryStore historyStore,
        ILogger<NarrativeBalanceController>? logger = null)
    {
        _interventionStore = interventionStore;
        _historyStore = historyStore;
        _logger = logger;
    }

    /// <summary>
    /// Check if an intervention should be generated based on rate limits.
    /// Returns true if allowed, false if suppressed.
    /// </summary>
    public InterventionRateCheck CanGenerateIntervention()
    {
        var check = new InterventionRateCheck { IsAllowed = true };

        // Check 1: Daily budget
        var recentInterventions = _interventionStore.LoadRecent(TimeSpan.FromHours(24));
        if (recentInterventions.Count >= DailyInterventionBudget)
        {
            check.IsAllowed = false;
            check.Reason = $"Daily intervention budget reached ({recentInterventions.Count}/{DailyInterventionBudget})";
            check.SuppressedCount++;
            return check;
        }

        // Check 2: Pending intervention limit
        var pending = _interventionStore.LoadByStatus(InterventionStatus.Pending);
        if (pending.Count >= MaxPendingInterventions)
        {
            check.IsAllowed = false;
            check.Reason = $"Too many pending interventions ({pending.Count}/{MaxPendingInterventions})";
            check.SuppressedCount++;
            return check;
        }

        // Check 3: Recent dismissal suppression
        var recentDismissals = _interventionStore.LoadAll()
            .Where(i => i.Status == InterventionStatus.Dismissed &&
                        i.RespondedAt.HasValue &&
                        i.RespondedAt.Value > DateTimeOffset.UtcNow.AddHours(-DismissalSuppressionHours))
            .ToList();

        if (recentDismissals.Count > 0)
        {
            check.IsAllowed = false;
            check.Reason = $"User recently dismissed intervention — suppressing for {DismissalSuppressionHours}h";
            check.SuppressedCount++;
            return check;
        }

        check.Reason = "Intervention allowed — within rate limits";
        return check;
    }

    /// <summary>
    /// Check if a specific tension should be injected into a prompt.
    /// Enforces cooldown per tension type.
    /// </summary>
    public TensionInjectionCheck CanInjectTension(ContradictionType tensionType, string declaredIntent)
    {
        var check = new TensionInjectionCheck { IsAllowed = true };

        // Check if similar tension was recently injected
        var recentInterventions = _interventionStore.LoadRecent(TensionCooldown);
        var similarRecent = recentInterventions.FirstOrDefault(i =>
            i.Source == tensionType.ToString() ||
            (i.DeclaredIntent?.Contains(declaredIntent, StringComparison.OrdinalIgnoreCase) == true));

        if (similarRecent != null)
        {
            var timeSinceLast = DateTimeOffset.UtcNow - similarRecent.GeneratedAt;
            check.IsAllowed = false;
            check.Reason = $"Similar tension injected {timeSinceLast.TotalHours:F1}h ago (cooldown: {TensionCooldown.TotalHours}h)";
            check.NextAllowedAt = similarRecent.GeneratedAt + TensionCooldown;
            return check;
        }

        check.Reason = "Tension injection allowed — no recent similar";
        return check;
    }

    /// <summary>
    /// Compute the balance score of recent interventions.
     /// Returns 0.0 (all negative) to 1.0 (balanced/positive).
    /// </summary>
    public double ComputeNarrativeBalance()
    {
        var recent = _interventionStore.LoadRecent(TimeSpan.FromDays(7));
        if (recent.Count == 0) return 1.0; // Neutral = balanced

        // Count by severity
        var criticalCount = recent.Count(i => i.Severity == InterventionSeverity.Critical);
        var highCount = recent.Count(i => i.Severity == InterventionSeverity.High);
        var mediumCount = recent.Count(i => i.Severity == InterventionSeverity.Medium);
        var lowCount = recent.Count(i => i.Severity == InterventionSeverity.Low);

        // Compute negativity ratio
        var totalWeight = (criticalCount * 4.0) + (highCount * 3.0) + (mediumCount * 2.0) + (lowCount * 1.0);
        var maxPossibleWeight = recent.Count * 4.0;

        if (maxPossibleWeight == 0) return 1.0;

        var negativityRatio = totalWeight / maxPossibleWeight;

        // Balance is inverse of negativity (1.0 = perfect balance, 0.0 = all critical)
        return 1.0 - negativityRatio;
    }

    /// <summary>
    /// Get a narrative balance report.
    /// </summary>
    public NarrativeBalanceReport GetBalanceReport()
    {
        var recent = _interventionStore.LoadRecent(TimeSpan.FromDays(7));
        var pending = _interventionStore.LoadByStatus(InterventionStatus.Pending);
        var dismissed = _interventionStore.LoadAll()
            .Where(i => i.Status == InterventionStatus.Dismissed)
            .ToList();

        return new NarrativeBalanceReport
        {
            BalanceScore = ComputeNarrativeBalance(),
            RecentInterventionCount = recent.Count,
            PendingCount = pending.Count,
            DismissedCount = dismissed.Count,
            DailyBudgetRemaining = Math.Max(0, DailyInterventionBudget - recent.Count),
            IsHealthy = ComputeNarrativeBalance() > 0.3 && pending.Count <= MaxPendingInterventions,
            Status = GetBalanceStatus()
        };
    }

    private string GetBalanceStatus()
    {
        var balance = ComputeNarrativeBalance();
        var pending = _interventionStore.LoadByStatus(InterventionStatus.Pending);

        if (pending.Count >= MaxPendingInterventions)
            return "Suppressed — too many pending interventions";
        if (balance < 0.2)
            return "Unbalanced — excessive negativity";
        if (balance < 0.4)
            return "Leaning negative — monitor closely";
        if (balance > 0.8)
            return "Well balanced — healthy narrative";
        return "Acceptable balance";
    }
}

/// <summary>
/// Result of an intervention rate check.
/// </summary>
public class InterventionRateCheck
{
    public bool IsAllowed { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int SuppressedCount { get; set; }
}

/// <summary>
/// Result of a tension injection check.
/// </summary>
public class TensionInjectionCheck
{
    public bool IsAllowed { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset? NextAllowedAt { get; set; }
}

/// <summary>
/// Report on narrative balance health.
/// </summary>
public class NarrativeBalanceReport
{
    public double BalanceScore { get; set; }
    public int RecentInterventionCount { get; set; }
    public int PendingCount { get; set; }
    public int DismissedCount { get; set; }
    public int DailyBudgetRemaining { get; set; }
    public bool IsHealthy { get; set; }
    public string Status { get; set; } = string.Empty;
}
