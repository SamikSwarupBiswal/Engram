using System.Text;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Identity;

/// <summary>
/// Reads and writes identity files to .engram/wiki/.
/// Files: user_identity.md, priorities.md, anti_goals.md
/// </summary>
public class IdentityStore : IDisposable
{
    private readonly string _wikiPath;
    private readonly ILogger<IdentityStore>? _logger;
    private bool _disposed;

    public IdentityStore(WorkspacePaths paths, ILogger<IdentityStore>? logger = null)
    {
        _wikiPath = paths.Wiki;
        _logger = logger;
    }

    // --- User Profile ---

    public UserProfile? LoadProfile()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var path = Path.Combine(_wikiPath, "user_identity.md");
        if (!File.Exists(path)) return null;

        try
        {
            var content = File.ReadAllText(path);
            return ParseProfile(content);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load user profile");
            return null;
        }
    }

    public void SaveProfile(UserProfile profile)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        profile.LastUpdatedAt = DateTimeOffset.UtcNow;
        var content = SerializeProfile(profile);
        var path = Path.Combine(_wikiPath, "user_identity.md");
        WriteAtomic(path, content);
        _logger?.LogInformation("User profile saved");
    }

    // --- Priorities ---

    public List<Priority> LoadPriorities()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var path = Path.Combine(_wikiPath, "priorities.md");
        if (!File.Exists(path)) return new List<Priority>();

        try
        {
            var content = File.ReadAllText(path);
            return ParsePriorities(content);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load priorities");
            return new List<Priority>();
        }
    }

    public void SavePriorities(List<Priority> priorities)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var content = SerializePriorities(priorities);
        var path = Path.Combine(_wikiPath, "priorities.md");
        WriteAtomic(path, content);
        _logger?.LogInformation("Saved {Count} priorities", priorities.Count);
    }

    // --- Anti-Goals ---

    public List<AntiGoal> LoadAntiGoals()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var path = Path.Combine(_wikiPath, "anti_goals.md");
        if (!File.Exists(path)) return new List<AntiGoal>();

        try
        {
            var content = File.ReadAllText(path);
            return ParseAntiGoals(content);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load anti-goals");
            return new List<AntiGoal>();
        }
    }

    public void SaveAntiGoals(List<AntiGoal> antiGoals)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var content = SerializeAntiGoals(antiGoals);
        var path = Path.Combine(_wikiPath, "anti_goals.md");
        WriteAtomic(path, content);
        _logger?.LogInformation("Saved {Count} anti-goals", antiGoals.Count);
    }

    // --- Existence Checks ---

    public bool ProfileExists() => File.Exists(Path.Combine(_wikiPath, "user_identity.md"));
    public bool PrioritiesExist() => File.Exists(Path.Combine(_wikiPath, "priorities.md"));
    public bool AntiGoalsExist() => File.Exists(Path.Combine(_wikiPath, "anti_goals.md"));

    public bool AllIdentityFilesExist() => ProfileExists() && PrioritiesExist() && AntiGoalsExist();

    // --- Serialization ---

    private static string SerializeProfile(UserProfile p)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine("user_id: " + p.UserId);
        sb.AppendLine("display_name: " + p.DisplayName);
        sb.AppendLine("created_at: " + p.CreatedAt.ToString("O"));
        sb.AppendLine("last_updated_at: " + p.LastUpdatedAt.ToString("O"));
        sb.AppendLine("version: " + p.Version);
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("# User Identity");
        sb.AppendLine();

        if (p.Goals.Count > 0)
        {
            sb.AppendLine("## Goals");
            sb.AppendLine();
            foreach (var g in p.Goals) sb.AppendLine("- " + g);
            sb.AppendLine();
        }

        if (p.ComfortTriggers.Count > 0)
        {
            sb.AppendLine("## Comfort Triggers");
            sb.AppendLine();
            foreach (var t in p.ComfortTriggers) sb.AppendLine("- " + t);
            sb.AppendLine();
        }

        if (p.RecurringAnxieties.Count > 0)
        {
            sb.AppendLine("## Recurring Anxieties");
            sb.AppendLine();
            foreach (var a in p.RecurringAnxieties) sb.AppendLine("- " + a);
            sb.AppendLine();
        }

        if (p.Preferences.Count > 0)
        {
            sb.AppendLine("## Preferences");
            sb.AppendLine();
            foreach (var pref in p.Preferences) sb.AppendLine("- " + pref);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static UserProfile ParseProfile(string content)
    {
        var profile = new UserProfile();
        var lines = content.Split('\n');
        bool inFrontMatter = false;
        string? section = null;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed == "---")
            {
                inFrontMatter = !inFrontMatter;
                continue;
            }

            if (inFrontMatter)
            {
                var colonIdx = trimmed.IndexOf(':');
                if (colonIdx > 0)
                {
                    var key = trimmed[..colonIdx].Trim();
                    var val = trimmed[(colonIdx + 1)..].Trim();
                    switch (key)
                    {
                        case "user_id": profile.UserId = val; break;
                        case "display_name": profile.DisplayName = val; break;
                    }
                }
                continue;
            }

            if (trimmed.StartsWith("## ")) { section = trimmed[3..].Trim(); continue; }

            if (trimmed.StartsWith("- ") && section != null)
            {
                var item = trimmed[2..];
                switch (section)
                {
                    case "Goals": profile.Goals.Add(item); break;
                    case "Comfort Triggers": profile.ComfortTriggers.Add(item); break;
                    case "Recurring Anxieties": profile.RecurringAnxieties.Add(item); break;
                    case "Preferences": profile.Preferences.Add(item); break;
                }
            }
        }

        return profile;
    }

    private static string SerializePriorities(List<Priority> priorities)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Priorities");
        sb.AppendLine();
        foreach (var p in priorities)
        {
            sb.AppendLine("- [" + p.Id + "] " + p.Description + " (confidence: " + p.Confidence.ToString("F1") + ", category: " + p.Category + ")");
        }
        return sb.ToString();
    }

    private static List<Priority> ParsePriorities(string content)
    {
        var priorities = new List<Priority>();
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("- [")) continue;

            // Parse: - [id] description (confidence: X, category: Y)
            var idEnd = trimmed.IndexOf(']');
            if (idEnd < 0) continue;
            var id = trimmed[3..idEnd];

            var rest = trimmed[(idEnd + 2)..].Trim();
            var confIdx = rest.IndexOf("(confidence:");
            var description = confIdx > 0 ? rest[..confIdx].Trim() : rest;
            var confidence = 1.0;
            var category = PriorityCategory.Other;

            if (confIdx > 0)
            {
                var confStr = rest[(confIdx + 13)..].Trim().TrimEnd(')');
                var parts = confStr.Split(',');
                if (parts.Length >= 1) double.TryParse(parts[0].Trim(), out confidence);
                if (parts.Length >= 2)
                {
                    var catStr = parts[1].Trim().Replace("category:", "").Trim();
                    Enum.TryParse<PriorityCategory>(catStr, true, out category);
                }
            }

            priorities.Add(new Priority { Id = id, Description = description, Confidence = confidence, Category = category });
        }
        return priorities;
    }

    private static string SerializeAntiGoals(List<AntiGoal> antiGoals)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Anti-Goals");
        sb.AppendLine();
        sb.AppendLine("These are explicit rules for what Engram should NOT do.");
        sb.AppendLine();
        foreach (var ag in antiGoals)
        {
            sb.AppendLine("- [" + ag.Id + "] (" + ag.Severity + ") " + ag.Description);
            if (!string.IsNullOrEmpty(ag.Context))
                sb.AppendLine("  Context: " + ag.Context);
        }
        return sb.ToString();
    }

    private static List<AntiGoal> ParseAntiGoals(string content)
    {
        var antiGoals = new List<AntiGoal>();
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("- [")) continue;

            // Parse: - [id] (severity) description
            var idEnd = trimmed.IndexOf(']');
            if (idEnd < 0) continue;
            var id = trimmed[3..idEnd];

            var rest = trimmed[(idEnd + 2)..].Trim();
            var sevStart = rest.IndexOf('(');
            var sevEnd = rest.IndexOf(')');
            var severity = AntiGoalSeverity.Medium;
            var description = rest;

            if (sevStart >= 0 && sevEnd > sevStart)
            {
                var sevStr = rest[(sevStart + 1)..sevEnd];
                Enum.TryParse<AntiGoalSeverity>(sevStr, true, out severity);
                description = rest[(sevEnd + 1)..].Trim();
            }

            antiGoals.Add(new AntiGoal { Id = id, Description = description, Severity = severity });
        }
        return antiGoals;
    }

    private void WriteAtomic(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmpPath = path + ".tmp";
        File.WriteAllText(tmpPath, content);
        File.Move(tmpPath, path, overwrite: true);
    }

    public void Dispose() { _disposed = true; }
}
