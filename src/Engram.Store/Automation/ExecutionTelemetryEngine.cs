using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Engram.Store.Automation;

public class TelemetryEntry
{
    public string WorkflowId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public double DurationMs { get; set; }
    public int RetryCount { get; set; }
    public bool RecoverySuccess { get; set; }
    public int HumanInterventions { get; set; }
    public bool Abandoned { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

public class TelemetrySummary
{
    public double SuccessRate { get; set; }
    public int RetryFrequency { get; set; }
    public int FailureCount { get; set; }
    public double RecoverySuccessRate { get; set; }
    public TimeSpan AverageLatency { get; set; }
    public int HumanInterventions { get; set; }
    public double WorkflowAbandonmentRate { get; set; }
}

public class ExecutionTelemetryEngine
{
    private readonly string _telemetryDir;
    private readonly ConcurrentDictionary<string, TelemetryEntry> _entries = new();
    private readonly object _lock = new();

    public ExecutionTelemetryEngine(string? customBaseDir = null)
    {
        var baseDir = customBaseDir ?? Path.Combine(Environment.CurrentDirectory, ".engram");
        _telemetryDir = Path.Combine(baseDir, "automation", "telemetry");
        Directory.CreateDirectory(_telemetryDir);
        LoadEntries();
    }

    private void LoadEntries()
    {
        try
        {
            if (!Directory.Exists(_telemetryDir)) return;

            var files = Directory.GetFiles(_telemetryDir, "*.json");
            foreach (var file in files)
            {
                try
                {
                    var content = File.ReadAllText(file);
                    var entry = JsonSerializer.Deserialize<TelemetryEntry>(content);
                    if (entry != null && !string.IsNullOrEmpty(entry.WorkflowId))
                    {
                        _entries[entry.WorkflowId] = entry;
                    }
                }
                catch
                {
                    // Ignore malformed files
                }
            }
        }
        catch
        {
            // Ignore load errors
        }
    }

    public void RecordWorkflowMetrics(
        string workflowId,
        bool success,
        TimeSpan duration,
        int retryCount,
        bool recoverySuccess,
        int interventions,
        bool abandoned)
    {
        var entry = new TelemetryEntry
        {
            WorkflowId = workflowId,
            Success = success,
            DurationMs = duration.TotalMilliseconds,
            RetryCount = retryCount,
            RecoverySuccess = recoverySuccess,
            HumanInterventions = interventions,
            Abandoned = abandoned,
            Timestamp = DateTimeOffset.UtcNow
        };

        _entries[workflowId] = entry;

        try
        {
            var content = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = true });
            var filePath = Path.Combine(_telemetryDir, $"{workflowId}.json");
            File.WriteAllText(filePath, content);
        }
        catch
        {
            // Ignore write errors to guarantee execution proceeds
        }
    }

    public TelemetrySummary GetSummary()
    {
        lock (_lock)
        {
            var all = _entries.Values.ToList();
            if (all.Count == 0)
            {
                return new TelemetrySummary();
            }

            int successCount = all.Count(e => e.Success);
            int failureCount = all.Count(e => !e.Success);
            int totalRetries = all.Sum(e => e.RetryCount);
            
            var recoveryAttempts = all.Where(e => e.RetryCount > 0 || e.RecoverySuccess).ToList();
            double recoverySuccessRate = recoveryAttempts.Count > 0 
                ? (double)recoveryAttempts.Count(e => e.RecoverySuccess) / recoveryAttempts.Count 
                : 0.0;

            double avgMs = all.Average(e => e.DurationMs);
            int totalInterventions = all.Sum(e => e.HumanInterventions);
            int abandonedCount = all.Count(e => e.Abandoned);

            return new TelemetrySummary
            {
                SuccessRate = (double)successCount / all.Count,
                RetryFrequency = totalRetries,
                FailureCount = failureCount,
                RecoverySuccessRate = recoverySuccessRate,
                AverageLatency = TimeSpan.FromMilliseconds(avgMs),
                HumanInterventions = totalInterventions,
                WorkflowAbandonmentRate = (double)abandonedCount / all.Count
            };
        }
    }
}
