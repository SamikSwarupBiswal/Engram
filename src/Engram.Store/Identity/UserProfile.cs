namespace Engram.Store.Identity;

/// <summary>
/// User identity profile. Stored in .engram/wiki/user_identity.md.
/// Acts as the System Prompt Constraint for all AI interventions.
/// </summary>
public class UserProfile
{
    public string UserId { get; set; } = "default";
    public string DisplayName { get; set; } = string.Empty;
    public List<string> Goals { get; set; } = new();
    public List<string> ComfortTriggers { get; set; } = new();
    public List<string> RecurringAnxieties { get; set; } = new();
    public List<string> Preferences { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastUpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public int Version { get; set; } = 1;
}

/// <summary>
/// A user priority with confidence level.
/// </summary>
public class Priority
{
    public string Id { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public PriorityCategory Category { get; set; }
    public double Confidence { get; set; } = 1.0;
    public string? Source { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum PriorityCategory
{
    Career,
    Health,
    Relationships,
    Finance,
    Learning,
    Creative,
    Spiritual,
    Other
}

/// <summary>
/// An anti-goal — something the user explicitly wants to avoid.
/// Has severity that determines how strictly it's enforced.
/// </summary>
public class AntiGoal
{
    public string Id { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public AntiGoalSeverity Severity { get; set; } = AntiGoalSeverity.Medium;
    public string? Context { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum AntiGoalSeverity
{
    Low,       // Reduce confidence, but allow
    Medium,    // Block unless explicitly overridden
    High,      // Block with warning
    Critical   // Always block, no override
}
