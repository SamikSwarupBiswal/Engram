using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Automation;

public enum FailureErrorType
{
    PermissionDenied,
    EnvironmentChanged,
    Timeout,
    ValidationFailed,
    UserAbandoned,
    ResourceUnavailable
}

public class FailureRecord
{
    public string FailureId { get; set; } = Guid.NewGuid().ToString("n")[..8];
    public string WorkflowId { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public string FailedStepId { get; set; } = string.Empty;
    public string StepDescription { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public FailureErrorType ErrorType { get; set; }
    public Dictionary<string, string> EnvironmentSnapshot { get; set; } = new();
    public bool RecoveryAttempted { get; set; }
    public bool RecoverySucceeded { get; set; }
    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class FailurePattern
{
    public string PatternId { get; set; } = Guid.NewGuid().ToString("n")[..8];
    public string Description { get; set; } = string.Empty;
    public int Occurrences { get; set; }
    public FailureErrorType FailureType { get; set; }
    public Dictionary<string, string> CommonContext { get; set; } = new();
    public string SuggestedMitigation { get; set; } = string.Empty;
}

public class FailureArchaeologyStore
{
    private readonly string _failuresDir;
    private readonly string _lessonsFile;
    private readonly ILogger? _logger;
    private readonly object _lock = new();

    public FailureArchaeologyStore(string? customBaseDir = null, ILogger? logger = null)
    {
        _logger = logger;
        var baseDir = customBaseDir ?? Path.Combine(Environment.CurrentDirectory, ".engram");
        _failuresDir = Path.Combine(baseDir, "automation", "failures");
        _lessonsFile = Path.Combine(_failuresDir, "lessons.json");
        Directory.CreateDirectory(_failuresDir);
    }

    public async Task RecordFailureAsync(FailureRecord record)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));

        var filePath = Path.Combine(_failuresDir, $"{record.FailureId}.json");
        var content = JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true });
        
        lock (_lock)
        {
            File.WriteAllText(filePath, content);
        }

        _logger?.LogInformation("Failure archaeology recorded: {FailureId} for goal: {Goal}", record.FailureId, record.Goal);
        await Task.CompletedTask;
    }

    public async Task<List<FailureRecord>> GetFailuresAsync(string? workflowId = null)
    {
        var list = new List<FailureRecord>();
        
        lock (_lock)
        {
            if (!Directory.Exists(_failuresDir)) return list;

            var files = Directory.GetFiles(_failuresDir, "*.json");
            foreach (var file in files)
            {
                if (Path.GetFileName(file).Equals("lessons.json", StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    var content = File.ReadAllText(file);
                    var record = JsonSerializer.Deserialize<FailureRecord>(content);
                    if (record != null && (workflowId == null || record.WorkflowId.Equals(workflowId, StringComparison.OrdinalIgnoreCase)))
                    {
                        list.Add(record);
                    }
                }
                catch
                {
                    // Ignore malformed files
                }
            }
        }

        return await Task.FromResult(list.OrderByDescending(r => r.RecordedAt).ToList());
    }

    public async Task PruneAsync(TimeSpan retentionPeriod)
    {
        var cutoff = DateTimeOffset.UtcNow - retentionPeriod;
        var failures = await GetFailuresAsync();

        lock (_lock)
        {
            foreach (var record in failures)
            {
                if (record.RecordedAt < cutoff)
                {
                    var filePath = Path.Combine(_failuresDir, $"{record.FailureId}.json");
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                        _logger?.LogInformation("Pruned historical raw failure record: {FailureId}", record.FailureId);
                    }
                }
            }
        }
        await Task.CompletedTask;
    }

    /// <summary>
    /// FailureConsolidator — aggregates raw failure records into high-level patterns/lessons.
    /// These are kept indefinitely in lessons.json.
    /// </summary>
    public async Task<List<FailurePattern>> DetectPatternsAsync()
    {
        var failures = await GetFailuresAsync();
        var lessons = LoadLessons();

        if (failures.Count == 0) return lessons;

        // Group raw failures by description/error type/failed step
        var grouped = failures.GroupBy(f => new { f.ErrorType, f.FailedStepId, f.ErrorMessage });
        
        foreach (var group in grouped)
        {
            var existing = lessons.FirstOrDefault(l => l.FailureType == group.Key.ErrorType && 
                                                       l.Description.Contains(group.Key.FailedStepId, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Occurrences = Math.Max(existing.Occurrences, group.Count());
            }
            else
            {
                lessons.Add(new FailurePattern
                {
                    Description = $"Recurring failure on step '{group.Key.FailedStepId}': {group.Key.ErrorMessage}",
                    Occurrences = group.Count(),
                    FailureType = group.Key.ErrorType,
                    CommonContext = group.First().EnvironmentSnapshot,
                    SuggestedMitigation = group.Key.ErrorType switch
                    {
                        FailureErrorType.PermissionDenied => "Verify execution constraints and request explicit permission elevation prior to running.",
                        FailureErrorType.EnvironmentChanged => "Trigger EnvironmentSynchronization check before executing next steps.",
                        FailureErrorType.Timeout => "Increase the timeout values for actions executed on this target.",
                        _ => "Initiate user collaboration clarification workflow."
                    }
                });
            }
        }

        SaveLessons(lessons);
        return lessons;
    }

    public async Task<FailureRecord?> FindSimilarFailureAsync(string goal, string stepDescription)
    {
        var failures = await GetFailuresAsync();
        return failures.FirstOrDefault(f => f.Goal.Contains(goal, StringComparison.OrdinalIgnoreCase) || 
                                            f.StepDescription.Contains(stepDescription, StringComparison.OrdinalIgnoreCase));
    }

    private List<FailurePattern> LoadLessons()
    {
        lock (_lock)
        {
            if (!File.Exists(_lessonsFile)) return new List<FailurePattern>();

            try
            {
                var content = File.ReadAllText(_lessonsFile);
                return JsonSerializer.Deserialize<List<FailurePattern>>(content) ?? new List<FailurePattern>();
            }
            catch
            {
                return new List<FailurePattern>();
            }
        }
    }

    private void SaveLessons(List<FailurePattern> lessons)
    {
        lock (_lock)
        {
            try
            {
                var content = JsonSerializer.Serialize(lessons, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_lessonsFile, content);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to save consolidated failure lessons.");
            }
        }
    }
}
