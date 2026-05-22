using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Engram.Store.Governance;

/// <summary>
/// Controls cognitive boundaries, preventing over-psychologizing and managing privacy zones.
/// </summary>
public class CognitiveBoundarySystem
{
    private readonly GovernanceConfig _config;

    public CognitiveBoundarySystem(GovernanceConfig config)
    {
        _config = config ?? new GovernanceConfig();
    }

    /// <summary>
    /// Checks if a file path or window process is excluded by the user's Privacy Zones.
    /// </summary>
    public bool IsExcluded(string? path, string? processName)
    {
        if (_config.PrivacyZones == null || !_config.PrivacyZones.Any())
        {
            return false;
        }

        foreach (var zone in _config.PrivacyZones)
        {
            if (!string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(zone.ExcludedPathPattern))
            {
                if (path.Contains(zone.ExcludedPathPattern, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (!string.IsNullOrEmpty(processName) && !string.IsNullOrEmpty(zone.ExcludedAppProcess))
            {
                if (string.Equals(processName, zone.ExcludedAppProcess, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if a given topic or content touches user-defined Sensitive Domains.
    /// </summary>
    public bool IsSensitive(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;

        foreach (var domain in _config.SensitiveDomains)
        {
            if (!domain.SuppressInterventions) continue;

            // Simple keyword trigger for sensitive domains
            var keywords = GetKeywordsForDomain(domain.DomainName);
            if (keywords.Any(k => content.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// NarrativeIntrusionGuard: Prevents over-psychologizing ordinary/temporary behaviors
    /// into dramatic psychological narratives (e.g. "burnout spirals").
    /// </summary>
    public string RestrainNarrative(string originalInterpretation, double confidence)
    {
        if (string.IsNullOrWhiteSpace(originalInterpretation)) return originalInterpretation;

        var creepyKeywords = new[] { "burnout", "depression", "panic", "spiral", "lazy", "procrastinating", "obsessed", "addicted" };

        bool containsCreepy = creepyKeywords.Any(k => originalInterpretation.Contains(k, StringComparison.OrdinalIgnoreCase));

        if (containsCreepy && confidence < 0.9)
        {
            // Suppress/rewrite creepy narratives unless confidence is extremely high (0.9+)
            var sanitized = originalInterpretation;
            foreach (var key in creepyKeywords)
            {
                sanitized = sanitized.Replace(key, "temporary operational context shift", StringComparison.OrdinalIgnoreCase);
            }
            return sanitized;
        }

        return originalInterpretation;
    }

    /// <summary>
    /// InterpretationRestraintEngine: Enforces evidence diversity and persistence rules
    /// before validating major contradiction escalations or identity-level claims.
    /// </summary>
    public bool ValidateInterpretation(string claimType, List<string> sourceTypes, TimeSpan duration)
    {
        // For identity claims, we require multiple source channels (evidence diversity)
        if (string.Equals(claimType, "identity", StringComparison.OrdinalIgnoreCase))
        {
            var uniqueChannels = sourceTypes.Distinct().Count();
            if (uniqueChannels < 2) return false; // Needs diversity (e.g. both file events and active window change)
        }

        // For major contradictions, we require temporal persistence
        if (string.Equals(claimType, "contradiction", StringComparison.OrdinalIgnoreCase))
        {
            if (duration < TimeSpan.FromMinutes(30)) return false; // Must persist at least 30 minutes
        }

        return true;
    }

    private static List<string> GetKeywordsForDomain(string domainName)
    {
        return domainName.ToLower() switch
        {
            "health" => new() { "doctor", "medical", "disease", "symptom", "therapy", "medication", "pill", "clinical" },
            "finances" => new() { "bank", "credit card", "mortgage", "loan", "salary", "invoice", "payment", "tax return", "crypto" },
            "relationships" => new() { "divorce", "dating", "spouse", "partner", "fight", "marriage", "breakup", "counseling" },
            "identity_analysis" => new() { "personality type", "psychology profile", "mental state", "character analysis" },
            _ => new()
        };
    }
}
