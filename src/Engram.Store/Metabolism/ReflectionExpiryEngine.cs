using Microsoft.Extensions.Logging;

namespace Engram.Store.Metabolism;

/// <summary>
/// Reflection expiry — not all narratives should persist forever.
/// 
/// Without this:
/// - Stale interpretations become identity
/// - Old contradictions haunt forever
/// - The organism can't let go
/// - Users feel trapped by past behavior
/// 
/// Implements:
/// - Fading interpretations (confidence decays over time)
/// - Expiring assumptions (interpretations have shelf life)
/// - Reversible identity claims (identity can change)
/// </summary>
public class ReflectionExpiryEngine
{
    private readonly ContradictionHistoryStore _historyStore;
    private readonly InterventionStore _interventionStore;
    private readonly ILogger<ReflectionExpiryEngine>? _logger;

    /// <summary>Days after which an unobserved interpretation starts fading.</summary>
    public int FadeAfterDays { get; set; } = 14;

    /// <summary>Days after which an interpretation expires completely.</summary>
    public int ExpireAfterDays { get; set; } = 60;

    /// <summary>Daily confidence decay rate for fading interpretations.</summary>
    public double DailyConfidenceDecay { get; set; } = 0.05;

    public ReflectionExpiryEngine(
        ContradictionHistoryStore historyStore,
        InterventionStore interventionStore,
        ILogger<ReflectionExpiryEngine>? logger = null)
    {
        _historyStore = historyStore;
        _interventionStore = interventionStore;
        _logger = logger;
    }

    /// <summary>
    /// Process expiry for all active contradictions.
    /// Returns list of expired/faded contradiction IDs.
    /// </summary>
    public List<ExpiryResult> ProcessExpiry()
    {
        var results = new List<ExpiryResult>();
        var active = _historyStore.LoadActive();

        foreach (var contradiction in active)
        {
            var expiry = CheckExpiry(contradiction);
            if (expiry != null)
            {
                results.Add(expiry);

                if (expiry.Action == ExpiryAction.Expire)
                {
                    _historyStore.Resolve(contradiction.ContradictionId,
                        $"Expired after {expiry.DaysSinceLastSeen:F0} days of inactivity");
                    _logger?.LogInformation("Contradiction expired: {Id}", contradiction.ContradictionId);
                }
            }
        }

        // Also process old interventions
        var expiredInterventions = ProcessInterventionExpiry();
        results.AddRange(expiredInterventions);

        _logger?.LogInformation("Processed {Count} expiry actions", results.Count);
        return results;
    }

    /// <summary>
    /// Check if a specific contradiction should fade or expire.
    /// </summary>
    private ExpiryResult? CheckExpiry(ContradictionHistoryEntry record)
    {
        var daysSinceLastSeen = (DateTimeOffset.UtcNow - record.LastSeenAt).TotalDays;
        var daysSinceFirstSeen = (DateTimeOffset.UtcNow - record.FirstSeenAt).TotalDays;

        // Completely expire old contradictions
        if (daysSinceLastSeen > ExpireAfterDays)
        {
            return new ExpiryResult
            {
                ContradictionId = record.ContradictionId,
                Action = ExpiryAction.Expire,
                Reason = $"No observations for {daysSinceLastSeen:F0} days — expired",
                DaysSinceLastSeen = daysSinceLastSeen,
                DaysSinceFirstSeen = daysSinceFirstSeen
            };
        }

        // Fade contradictions that haven't been reinforced
        if (daysSinceLastSeen > FadeAfterDays)
        {
            return new ExpiryResult
            {
                ContradictionId = record.ContradictionId,
                Action = ExpiryAction.Fade,
                Reason = $"No observations for {daysSinceLastSeen:F0} days — fading confidence",
                DaysSinceLastSeen = daysSinceLastSeen,
                DaysSinceFirstSeen = daysSinceFirstSeen,
                ConfidenceDecay = (daysSinceLastSeen - FadeAfterDays) * DailyConfidenceDecay
            };
        }

        return null;
    }

    /// <summary>
    /// Process expiry for old interventions.
    /// </summary>
    private List<ExpiryResult> ProcessInterventionExpiry()
    {
        var results = new List<ExpiryResult>();
        var all = _interventionStore.LoadAll();

        // Expire old pending interventions
        var stalePending = all.Where(i =>
            i.Status == InterventionStatus.Pending &&
            (DateTimeOffset.UtcNow - i.GeneratedAt).TotalDays > ExpireAfterDays)
            .ToList();

        foreach (var intervention in stalePending)
        {
            intervention.Status = InterventionStatus.Dismissed;
            intervention.RespondedAt = DateTimeOffset.UtcNow;
            intervention.UserResponse = "Auto-expired";
            _interventionStore.Save(intervention);

            results.Add(new ExpiryResult
            {
                InterventionId = intervention.InterventionId,
                Action = ExpiryAction.Expire,
                Reason = $"Pending intervention expired after {ExpireAfterDays} days"
            });
        }

        return results;
    }

    /// <summary>
    /// Compute the expiry health of the system.
    /// </summary>
    public ExpiryHealth ComputeExpiryHealth()
    {
        var active = _historyStore.LoadActive();
        var all = _historyStore.LoadAll();
        var interventions = _interventionStore.LoadAll();

        var staleContradictions = active.Count(c =>
            (DateTimeOffset.UtcNow - c.LastSeenAt).TotalDays > FadeAfterDays);

        var expiredContradictions = all.Count(c =>
            c.Status == ContradictionStatus.Resolved &&
            c.Resolution?.Contains("Expired") == true);

        var staleInterventions = interventions.Count(i =>
            i.Status == InterventionStatus.Pending &&
            (DateTimeOffset.UtcNow - i.GeneratedAt).TotalDays > FadeAfterDays);

        return new ExpiryHealth
        {
            ActiveContradictions = active.Count,
            StaleContradictions = staleContradictions,
            ExpiredContradictions = expiredContradictions,
            StaleInterventions = staleInterventions,
            IsHealthy = staleContradictions < active.Count * 0.5,
            Status = GetExpiryStatus(staleContradictions, active.Count)
        };
    }

    private static string GetExpiryStatus(int stale, int total)
    {
        if (total == 0) return "No active contradictions — clean state";
        var staleRatio = (double)stale / total;
        if (staleRatio > 0.5)
            return "Many stale contradictions — expiry processing needed";
        if (staleRatio > 0.2)
            return "Some stale contradictions — normal aging";
        return "Contradictions are fresh — healthy state";
    }
}

/// <summary>
/// Result of an expiry check.
/// </summary>
public class ExpiryResult
{
    public string? ContradictionId { get; set; }
    public string? InterventionId { get; set; }
    public ExpiryAction Action { get; set; }
    public string Reason { get; set; } = string.Empty;
    public double DaysSinceLastSeen { get; set; }
    public double DaysSinceFirstSeen { get; set; }
    public double ConfidenceDecay { get; set; }
}

public enum ExpiryAction
{
    Fade,    // Confidence decreasing
    Expire   // Completely removed
}

/// <summary>
/// Expiry health assessment.
/// </summary>
public class ExpiryHealth
{
    public int ActiveContradictions { get; set; }
    public int StaleContradictions { get; set; }
    public int ExpiredContradictions { get; set; }
    public int StaleInterventions { get; set; }
    public bool IsHealthy { get; set; }
    public string Status { get; set; } = string.Empty;
}
