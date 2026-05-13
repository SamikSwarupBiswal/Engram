using Microsoft.Extensions.Logging;

namespace Engram.Store.Identity;

public class InterventionPolicy : IDisposable
{
    private readonly IdentityStore _store;
    private readonly ILogger<InterventionPolicy>? _logger;
    private List<AntiGoal>? _cachedAntiGoals;
    private UserProfile? _cachedProfile;

    public InterventionPolicy(IdentityStore store, ILogger<InterventionPolicy>? logger = null)
    {
        _store = store;
        _logger = logger;
    }

    public InterventionResult Evaluate(InterventionRequest request)
    {
        var antiGoals = GetAntiGoals();
        var profile = GetProfile();

        // Check ALL anti-goals, return strictest match
        AntiGoal? strictestMatch = null;
        foreach (var ag in antiGoals)
        {
            if (MatchesAntiGoal(request, ag))
            {
                if (strictestMatch == null || ag.Severity > strictestMatch.Severity)
                    strictestMatch = ag;
            }
        }

        if (strictestMatch != null)
        {
            var reason = "Blocked by anti-goal: " + strictestMatch.Description + " (severity: " + strictestMatch.Severity + ")";

            if (strictestMatch.Severity >= AntiGoalSeverity.Medium)
            {
                _logger?.LogWarning("Intervention BLOCKED: {Reason}", reason);
                return new InterventionResult { Allowed = false, Reason = reason, Severity = strictestMatch.Severity };
            }

            // Low severity: reduce confidence but allow
            _logger?.LogInformation("Intervention ALLOWED with reduced confidence: {Reason}", reason);
            return new InterventionResult { Allowed = true, Reason = reason, Confidence = 0.5, Severity = strictestMatch.Severity };
        }

        // Check recurring anxieties
        if (profile != null && RelatesToAnxiety(request, profile))
        {
            _logger?.LogInformation("Intervention relates to user anxiety");
            return new InterventionResult { Allowed = true, Reason = "Relates to user anxiety", Confidence = 1.0 };
        }

        return new InterventionResult { Allowed = true, Reason = "No identity constraints triggered", Confidence = 1.0 };
    }

    public void InvalidateCache()
    {
        _cachedAntiGoals = null;
        _cachedProfile = null;
    }

    private List<AntiGoal> GetAntiGoals() => _cachedAntiGoals ??= _store.LoadAntiGoals();
    private UserProfile? GetProfile() => _cachedProfile ??= _store.LoadProfile();

    private static bool MatchesAntiGoal(InterventionRequest request, AntiGoal antiGoal)
    {
        var requestText = (request.Action + " " + request.Context + " " + request.Category).ToLowerInvariant();
        var antiGoalText = antiGoal.Description.ToLowerInvariant();
        var keywords = antiGoalText.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var keyword in keywords)
        {
            if (keyword.Length >= 3 && requestText.Contains(keyword))
                return true;
        }
        return false;
    }

    private static bool RelatesToAnxiety(InterventionRequest request, UserProfile profile)
    {
        var requestText = (request.Action + " " + request.Context).ToLowerInvariant();
        foreach (var anxiety in profile.RecurringAnxieties)
        {
            var keywords = anxiety.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var kw in keywords)
            {
                if (kw.Length >= 3 && requestText.Contains(kw))
                    return true;
            }
        }
        return false;
    }

    public void Dispose() { }
}

public class InterventionRequest
{
    public string Action { get; set; } = string.Empty;
    public string Context { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
}

public class InterventionResult
{
    public bool Allowed { get; set; }
    public string Reason { get; set; } = string.Empty;
    public double Confidence { get; set; } = 1.0;
    public AntiGoalSeverity? Severity { get; set; }
}
