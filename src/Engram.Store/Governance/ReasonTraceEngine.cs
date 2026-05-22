using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Engram.Store.Governance;

/// <summary>
/// Logger and indexing engine for causal reason traces.
/// Keeps a persistent history of why governance and cognitive decisions are made.
/// </summary>
public class ReasonTraceEngine
{
    private readonly string _tracesFilePath;
    private readonly List<ReasonTrace> _traces = new();
    private readonly object _lock = new();
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public ReasonTraceEngine(WorkspacePaths paths)
    {
        var dir = Path.Combine(paths.Config, "governance");
        Directory.CreateDirectory(dir);
        _tracesFilePath = Path.Combine(dir, "reason_traces.json");
        LoadTraces();
    }

    public void AddTrace(TraceTriggerType triggerType, string targetEntityId, string description, List<string> causalFactors, string component)
    {
        var trace = new ReasonTrace
        {
            TriggerType = triggerType,
            TargetEntityId = targetEntityId,
            Description = description,
            CausalFactors = causalFactors,
            SystemComponent = component
        };

        lock (_lock)
        {
            _traces.Add(trace);
            // Cap at 1000 traces in memory/file to prevent bloating
            if (_traces.Count > 1000)
            {
                _traces.RemoveAt(0);
            }
            SaveTraces();
        }
    }

    public IReadOnlyList<ReasonTrace> GetTracesForEntity(string targetEntityId)
    {
        lock (_lock)
        {
            return _traces.Where(t => string.Equals(t.TargetEntityId, targetEntityId, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }

    public IReadOnlyList<ReasonTrace> GetAllTraces()
    {
        lock (_lock)
        {
            return _traces.ToList();
        }
    }

    private void LoadTraces()
    {
        lock (_lock)
        {
            if (!File.Exists(_tracesFilePath)) return;
            try
            {
                var json = File.ReadAllText(_tracesFilePath);
                var loaded = JsonSerializer.Deserialize<List<ReasonTrace>>(json, JsonOptions);
                if (loaded != null)
                {
                    _traces.Clear();
                    _traces.AddRange(loaded);
                }
            }
            catch
            {
                // Graceful fallback on load error
            }
        }
    }

    private void SaveTraces()
    {
        lock (_lock)
        {
            try
            {
                var tmpPath = _tracesFilePath + ".tmp";
                var json = JsonSerializer.Serialize(_traces, JsonOptions);
                File.WriteAllText(tmpPath, json);
                File.Move(tmpPath, _tracesFilePath, overwrite: true);
            }
            catch
            {
                // Graceful fallback on save error
            }
        }
    }
}

/// <summary>
/// Converts internal Causal Traces into emotionally neutral, non-creepy, human-readable narratives.
/// </summary>
public static class DecisionNarrator
{
    public static string Narrate(ReasonTrace trace)
    {
        if (trace == null) return "No information available.";

        string triggerDescription = trace.TriggerType switch
        {
            TraceTriggerType.Intervention => "An intervention was initiated",
            TraceTriggerType.SalienceShift => "Priority tracking updated",
            TraceTriggerType.Pause => "Operational workflow paused",
            TraceTriggerType.Escalation => "A notification was prepared for user review",
            TraceTriggerType.ExecutionDecision => "A task execution decision was recorded",
            _ => "An action occurred"
        };

        var factors = trace.CausalFactors != null && trace.CausalFactors.Any()
            ? string.Join(", ", trace.CausalFactors.Select(CleanFactor))
            : "general system events";

        return $"{triggerDescription} because: {factors}.";
    }

    private static string CleanFactor(string factor)
    {
        if (string.IsNullOrWhiteSpace(factor)) return string.Empty;

        // Strip internal technical patterns
        var clean = factor
            .Replace("salience propagated", "activity related")
            .Replace("edge weight", "relevance")
            .Replace("decay rate", "relevance fade")
            .Replace("Confidence", "confidence score")
            .Replace("attention storm", "sudden activity spike");

        return clean.Trim();
    }
}
