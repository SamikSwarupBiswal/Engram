using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Engram.Store.Automation;

public class ProceduralMemoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string Type { get; set; } = string.Empty; // e.g. "habit", "recovery", "sequence", "quirk"
    public string Target { get; set; } = string.Empty; // e.g. "amazon.com", "VSCode", "Excel"
    public string Detail { get; set; } = string.Empty;
    public int SuccessCount { get; set; }
    public int UseCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastUsedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Keeps track of procedural patterns, application quirks, and user preferences over time.
/// </summary>
public class ProceduralMemoryEngine
{
    private readonly string _storePath;
    private readonly object _lock = new();
    private List<ProceduralMemoryEntry> _cache = new();

    public ProceduralMemoryEngine(string? baseDir = null)
    {
        var baseDirectory = baseDir ?? Directory.GetCurrentDirectory();
        _storePath = Path.Combine(baseDirectory, ".engram", "automation", "procedural_memory.json");
    }

    public async Task InitializeAsync()
    {
        lock (_lock)
        {
            if (File.Exists(_storePath))
            {
                try
                {
                    var json = File.ReadAllText(_storePath);
                    _cache = JsonSerializer.Deserialize<List<ProceduralMemoryEntry>>(json) ?? new List<ProceduralMemoryEntry>();
                }
                catch
                {
                    _cache = new List<ProceduralMemoryEntry>();
                }
            }
            else
            {
                _cache = new List<ProceduralMemoryEntry>();
            }
        }
        await Task.CompletedTask;
    }

    public async Task AddMemoryAsync(string type, string target, string detail, bool isSuccessful = true)
    {
        lock (_lock)
        {
            var existing = _cache.FirstOrDefault(m => 
                m.Type.Equals(type, StringComparison.OrdinalIgnoreCase) && 
                m.Target.Equals(target, StringComparison.OrdinalIgnoreCase) &&
                m.Detail.Equals(detail, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.UseCount++;
                if (isSuccessful) existing.SuccessCount++;
                existing.LastUsedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                _cache.Add(new ProceduralMemoryEntry
                {
                    Type = type,
                    Target = target,
                    Detail = detail,
                    UseCount = 1,
                    SuccessCount = isSuccessful ? 1 : 0
                });
            }
        }

        await SaveAsync();
    }

    public async Task<List<ProceduralMemoryEntry>> GetMemoriesAsync()
    {
        lock (_lock)
        {
            return _cache.Select(m => new ProceduralMemoryEntry
            {
                Id = m.Id,
                Type = m.Type,
                Target = m.Target,
                Detail = m.Detail,
                SuccessCount = m.SuccessCount,
                UseCount = m.UseCount,
                CreatedAt = m.CreatedAt,
                LastUsedAt = m.LastUsedAt
            }).ToList();
        }
    }

    public async Task<List<ProceduralMemoryEntry>> GetMemoriesForTargetAsync(string target)
    {
        lock (_lock)
        {
            return _cache
                .Where(m => m.Target.Equals(target, StringComparison.OrdinalIgnoreCase))
                .Select(m => new ProceduralMemoryEntry
                {
                    Id = m.Id,
                    Type = m.Type,
                    Target = m.Target,
                    Detail = m.Detail,
                    SuccessCount = m.SuccessCount,
                    UseCount = m.UseCount,
                    CreatedAt = m.CreatedAt,
                    LastUsedAt = m.LastUsedAt
                }).ToList();
        }
    }

    public async Task LearnFromExecutionAsync(ExecutionPlan plan, bool success)
    {
        if (plan == null) return;

        // Auto-extract and learn sequences
        string targetDomain = ExtractDomainFromGoal(plan.Goal);
        
        // Learn successful actions
        foreach (var step in plan.Steps.Values)
        {
            if (step.Status == StepStatus.Completed)
            {
                await AddMemoryAsync("sequence", targetDomain, $"Successful {step.Action.Type} action: {step.Action.Description}", isSuccessful: true);
            }
            else if (step.Status == StepStatus.Failed)
            {
                await AddMemoryAsync("quirk", targetDomain, $"Failed action: {step.Action.Description}. Error: {step.Error}", isSuccessful: false);
            }
        }

        // Aggregate goal success
        await AddMemoryAsync("goal_outcome", targetDomain, $"Goal achieved: {plan.Goal}", isSuccessful: success);
    }

    private static string ExtractDomainFromGoal(string goal)
    {
        if (string.IsNullOrEmpty(goal)) return "global";
        
        // Find domains like "amazon.com", "github.com", or default to a generic keyword
        var parts = goal.Split(' ');
        foreach (var part in parts)
        {
            if (part.Contains('.') && Uri.TryCreate("http://" + part.Trim('.', ',', ';'), UriKind.Absolute, out var uri))
            {
                return uri.Host;
            }
        }

        // Fallback to first non-empty word or "global"
        return parts.FirstOrDefault(p => p.Length > 2) ?? "global";
    }

    private async Task SaveAsync()
    {
        var dir = Path.GetDirectoryName(_storePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        lock (_lock)
        {
            var json = JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_storePath, json);
        }
        await Task.CompletedTask;
    }
}
