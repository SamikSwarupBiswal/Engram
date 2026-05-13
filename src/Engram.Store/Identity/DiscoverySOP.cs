using Microsoft.Extensions.Logging;

namespace Engram.Store.Identity;

/// <summary>
/// Discovery SOP — interactive interview that extracts user identity.
/// CLI-based flow: asks questions, extracts structured data, writes identity files.
/// </summary>
public class DiscoverySOP
{
    private readonly IdentityStore _store;
    private readonly ILogger<DiscoverySOP>? _logger;

    public DiscoverySOP(IdentityStore store, ILogger<DiscoverySOP>? logger = null)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Run the discovery interview. Returns the extracted profile.
    /// In production, this would be an interactive CLI flow.
    /// For testing, accepts pre-built answers.
    /// </summary>
    public DiscoveryResult RunDiscovery(DiscoveryAnswers answers)
    {
        _logger?.LogInformation("Starting discovery SOP");

        var profile = new UserProfile
        {
            UserId = "default",
            DisplayName = answers.DisplayName,
            Goals = answers.Goals.ToList(),
            ComfortTriggers = answers.ComfortTriggers.ToList(),
            RecurringAnxieties = answers.RecurringAnxieties.ToList(),
            Preferences = answers.Preferences.ToList()
        };

        var priorities = answers.Priorities.Select(p => new Priority
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Description = p.Description,
            Category = p.Category,
            Confidence = 1.0
        }).ToList();

        var antiGoals = answers.AntiGoals.Select(ag => new AntiGoal
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Description = ag.Description,
            Severity = ag.Severity,
            Context = ag.Context
        }).ToList();

        return new DiscoveryResult
        {
            Profile = profile,
            Priorities = priorities,
            AntiGoals = antiGoals,
            CompletedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Save discovery results to identity files.
    /// </summary>
    public void SaveDiscoveryResults(DiscoveryResult result)
    {
        _store.SaveProfile(result.Profile);
        _store.SavePriorities(result.Priorities);
        _store.SaveAntiGoals(result.AntiGoals);

        _logger?.LogInformation("Discovery results saved: {Goals} goals, {Priorities} priorities, {AntiGoals} anti-goals",
            result.Profile.Goals.Count, result.Priorities.Count, result.AntiGoals.Count);
    }

    /// <summary>
    /// Check if discovery has been completed.
    /// </summary>
    public bool IsDiscoveryComplete()
    {
        return _store.AllIdentityFilesExist();
    }
}

/// <summary>
/// Pre-built answers for discovery (from CLI input or test data).
/// </summary>
public class DiscoveryAnswers
{
    public string DisplayName { get; set; } = string.Empty;
    public List<string> Goals { get; set; } = new();
    public List<string> ComfortTriggers { get; set; } = new();
    public List<string> RecurringAnxieties { get; set; } = new();
    public List<string> Preferences { get; set; } = new();
    public List<PriorityAnswer> Priorities { get; set; } = new();
    public List<AntiGoalAnswer> AntiGoals { get; set; } = new();
}

public class PriorityAnswer
{
    public string Description { get; set; } = string.Empty;
    public PriorityCategory Category { get; set; }
}

public class AntiGoalAnswer
{
    public string Description { get; set; } = string.Empty;
    public AntiGoalSeverity Severity { get; set; } = AntiGoalSeverity.Medium;
    public string? Context { get; set; }
}

/// <summary>
/// Result of a discovery session.
/// </summary>
public class DiscoveryResult
{
    public UserProfile Profile { get; set; } = new();
    public List<Priority> Priorities { get; set; } = new();
    public List<AntiGoal> AntiGoals { get; set; } = new();
    public DateTimeOffset CompletedAt { get; set; }
}
