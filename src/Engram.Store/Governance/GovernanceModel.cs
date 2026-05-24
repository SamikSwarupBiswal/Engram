using System;
using System.Collections.Generic;

namespace Engram.Store.Governance;

/// <summary>
/// Types of causal occurrences in Engram.
/// </summary>
public enum TraceTriggerType
{
    Intervention,
    SalienceShift,
    Pause,
    Escalation,
    ExecutionDecision
}

/// <summary>
/// A semantic causal trace that explains why a certain governance or cognitive action occurred.
/// </summary>
public class ReasonTrace
{
    public string TraceId { get; set; } = Guid.NewGuid().ToString("n");
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public TraceTriggerType TriggerType { get; set; }
    public string TargetEntityId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> CausalFactors { get; set; } = new();
    public string SystemComponent { get; set; } = string.Empty;
}

/// <summary>
/// Trust state metrics for a specific domain or action category.
/// </summary>
public class TrustScore
{
    public string Domain { get; set; } = string.Empty; // e.g. "file_deletion", "browser_research"
    public double Score { get; set; } = 0.5; // ranges 0.0 to 1.0
    public DateTimeOffset LastUpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public int SuccessStreak { get; set; }
    public int OverrideCount { get; set; }
}

/// <summary>
/// Entry for the public Semantic Activity Feed.
/// </summary>
public class ActivityEntry
{
    public string EntryId { get; set; } = Guid.NewGuid().ToString("n");
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string Action { get; set; } = string.Empty; // e.g. "Wiki Node Metamorphosis", "Attention Decay", "Forgetting"
    public string Description { get; set; } = string.Empty;
    public string RelatedNodeId { get; set; } = string.Empty;
    public string ImpactLevel { get; set; } = "Low"; // Low, Medium, High
}

/// <summary>
/// Domain specific memory retention policy.
/// </summary>
public class RetentionPolicyRule
{
    public string Domain { get; set; } = string.Empty; // e.g. "workflows", "browsing"
    public TimeSpan RetentionWindow { get; set; } = TimeSpan.FromDays(90);
    public bool AutoExpire { get; set; } = true;
}

/// <summary>
/// User-defined blocked/sensitive domain rules.
/// </summary>
public class SensitiveDomainRule
{
    public string DomainName { get; set; } = string.Empty; // e.g. "health", "finances", "relationships"
    public bool SuppressInterventions { get; set; } = true;
    public bool SuppressPropagation { get; set; } = true;
}

/// <summary>
/// Rules defining excluded semantic zones (e.g. non-indexed directories or apps).
/// </summary>
public class PrivacyZoneRule
{
    public string RuleName { get; set; } = string.Empty;
    public string ExcludedPathPattern { get; set; } = string.Empty; // wildcard/directory path
    public string ExcludedAppProcess { get; set; } = string.Empty;
}

/// <summary>
/// Central configurations for the Trust, Governance & Coexistence model.
/// </summary>
public class GovernanceConfig
{
    public List<RetentionPolicyRule> RetentionPolicies { get; set; } = new()
    {
        new() { Domain = "workflows", RetentionWindow = TimeSpan.FromDays(90) },
        new() { Domain = "browsing", RetentionWindow = TimeSpan.FromDays(14) },
        new() { Domain = "personal_reflections", RetentionWindow = TimeSpan.FromDays(365 * 10), AutoExpire = false },
        new() { Domain = "financial_activity", RetentionWindow = TimeSpan.FromDays(30) }
    };

    public List<SensitiveDomainRule> SensitiveDomains { get; set; } = new()
    {
        new() { DomainName = "health", SuppressInterventions = true, SuppressPropagation = true },
        new() { DomainName = "finances", SuppressInterventions = true, SuppressPropagation = true },
        new() { DomainName = "relationships", SuppressInterventions = true, SuppressPropagation = true },
        new() { DomainName = "identity_analysis", SuppressInterventions = true, SuppressPropagation = true }
    };

    public List<PrivacyZoneRule> PrivacyZones { get; set; } = new();

    public int MaxDailyInterventions { get; set; } = 5;
    public double MinConfidenceToEscalate { get; set; } = 0.7;
    public double DefaultTrustCeiling { get; set; } = 1.0;

    // --- Phase D6: Autonomy Gating & Calibration ---
    public AutonomyLevel BaselineAutonomy { get; set; } = AutonomyLevel.Medium;
    public AutonomyLevel ActiveAutonomy { get; set; } = AutonomyLevel.Medium;
    
    public Dictionary<string, AutonomyLevel> DomainAutonomyCeilings { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        { "destructive", AutonomyLevel.Low },
        { "financial", AutonomyLevel.Low },
        { "filesystem_write", AutonomyLevel.Medium },
        { "email", AutonomyLevel.Medium },
        { "browser_research", AutonomyLevel.Aggressive },
        { "coding", AutonomyLevel.Aggressive }
    };
}

public enum AutonomyLevel
{
    Low,
    Medium,
    Aggressive
}
