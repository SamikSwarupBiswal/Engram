using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Engram.Store.Automation;

/// <summary>
/// FailureNarrative — structured log of a step failure and its recovery attempts.
/// Uses operationally neutral language to describe technical issues clearly.
/// </summary>
public class FailureNarrative
{
    public string NarrativeId { get; set; } = Guid.NewGuid().ToString("n")[..8];
    public string WorkflowId { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public string FailedStepId { get; set; } = string.Empty;
    public string StepDescription { get; set; } = string.Empty;
    public string TechnicalDetails { get; set; } = string.Empty;
    public string LegibleExplanation { get; set; } = string.Empty;
    public string AutonomyLevel { get; set; } = "Medium";
    public bool RecoveryAttempted { get; set; }
    public bool RecoverySucceeded { get; set; }
    public string RecoveryExplanation { get; set; } = string.Empty;
    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class FailureNarrativeRecorder
{
    private readonly string _filePath;
    private readonly object _lock = new();

    public FailureNarrativeRecorder(string? customBaseDir = null)
    {
        var baseDir = customBaseDir ?? Path.Combine(Environment.CurrentDirectory, ".engram");
        var dir = Path.Combine(baseDir, "failures");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "narratives.json");
    }

    public async Task RecordFailureNarrativeAsync(FailureNarrative narrative)
    {
        if (narrative == null) throw new ArgumentNullException(nameof(narrative));

        lock (_lock)
        {
            List<FailureNarrative> list;
            if (File.Exists(_filePath))
            {
                try
                {
                    var json = File.ReadAllText(_filePath);
                    list = JsonSerializer.Deserialize<List<FailureNarrative>>(json) ?? new();
                }
                catch
                {
                    list = new();
                }
            }
            else
            {
                list = new();
            }

            list.Add(narrative);

            // Cap at 100 entries to prevent file inflation
            if (list.Count > 100)
            {
                list.RemoveAt(0);
            }

            var serialized = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, serialized);
        }
        await Task.CompletedTask;
    }

    public async Task<List<FailureNarrative>> GetNarrativesAsync()
    {
        lock (_lock)
        {
            if (!File.Exists(_filePath)) return new();
            try
            {
                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<List<FailureNarrative>>(json) ?? new();
            }
            catch
            {
                return new();
            }
        }
    }
}
