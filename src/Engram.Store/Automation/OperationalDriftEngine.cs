using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Engram.Store.Events;

namespace Engram.Store.Automation;

public enum OperationalDriftType
{
    AbandonedWorkflow,
    ExecutionStagnation,
    PerpetualRetry,
    ContextCollapse,
    WorkflowLoop,
    ConfidenceDecay
}

public enum DriftSeverity
{
    Low,
    Medium,
    High,
    Critical
}

public enum DriftAlertStatus
{
    Pending,
    Dismissed,
    Accepted,
    Resolved
}

public class OperationalDriftAlert
{
    public string AlertId { get; set; } = Guid.NewGuid().ToString("n")[..8];
    public string WorkflowId { get; set; } = string.Empty;
    public OperationalDriftType Type { get; set; }
    public DriftSeverity Severity { get; set; }
    public string Description { get; set; } = string.Empty;
    public List<string> Evidence { get; set; } = new();
    public string Recommendation { get; set; } = "Continue";
    public DateTimeOffset DetectedAt { get; set; } = DateTimeOffset.UtcNow;
    public DriftAlertStatus Status { get; set; } = DriftAlertStatus.Pending;
}

public class OperationalDriftEngine
{
    private readonly OperationalWorldModel _worldModel;
    private readonly ExecutionTelemetryEngine _telemetry;
    private readonly OperationalTimeline _timeline;
    private readonly IEventBus _eventBus;
    private readonly ILogger? _logger;
    private readonly string _driftDir;
    
    private readonly ConcurrentDictionary<string, List<OperationalDriftAlert>> _alerts = new();
    private readonly object _lock = new();

    public OperationalDriftEngine(
        OperationalWorldModel worldModel,
        ExecutionTelemetryEngine telemetry,
        OperationalTimeline timeline,
        IEventBus eventBus,
        string? customBaseDir = null,
        ILogger? logger = null)
    {
        _worldModel = worldModel ?? throw new ArgumentNullException(nameof(worldModel));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logger = logger;

        var baseDir = customBaseDir ?? Path.Combine(Environment.CurrentDirectory, ".engram");
        _driftDir = Path.Combine(baseDir, "automation", "drift");
        Directory.CreateDirectory(_driftDir);
        LoadAllAlerts();
    }

    private void LoadAllAlerts()
    {
        try
        {
            if (!Directory.Exists(_driftDir)) return;

            var files = Directory.GetFiles(_driftDir, "*.json");
            foreach (var file in files)
            {
                try
                {
                    var content = File.ReadAllText(file);
                    var workflowId = Path.GetFileNameWithoutExtension(file);
                    var list = JsonSerializer.Deserialize<List<OperationalDriftAlert>>(content);
                    if (list != null)
                    {
                        _alerts[workflowId] = list;
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

    private void SaveAlerts(string workflowId)
    {
        try
        {
            if (_alerts.TryGetValue(workflowId, out var list))
            {
                var filePath = Path.Combine(_driftDir, $"{workflowId}.json");
                var content = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, content);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save drift alerts for workflow {WorkflowId}", workflowId);
        }
    }

    public List<OperationalDriftAlert> GetAlerts(string workflowId)
    {
        if (_alerts.TryGetValue(workflowId, out var list))
        {
            lock (list)
            {
                return list.ToList();
            }
        }
        return new List<OperationalDriftAlert>();
    }

    public void DismissAlert(string alertId)
    {
        foreach (var kvp in _alerts)
        {
            lock (kvp.Value)
            {
                var alert = kvp.Value.FirstOrDefault(a => a.AlertId.Equals(alertId, StringComparison.OrdinalIgnoreCase));
                if (alert != null)
                {
                    alert.Status = DriftAlertStatus.Dismissed;
                    SaveAlerts(kvp.Key);
                    _logger?.LogInformation("Drift alert {AlertId} dismissed.", alertId);
                    return;
                }
            }
        }
    }

    public void AcceptAlert(string alertId)
    {
        foreach (var kvp in _alerts)
        {
            lock (kvp.Value)
            {
                var alert = kvp.Value.FirstOrDefault(a => a.AlertId.Equals(alertId, StringComparison.OrdinalIgnoreCase));
                if (alert != null)
                {
                    alert.Status = DriftAlertStatus.Accepted;
                    SaveAlerts(kvp.Key);
                    _logger?.LogInformation("Drift alert {AlertId} accepted.", alertId);
                    return;
                }
            }
        }
    }

    public List<OperationalDriftAlert> DetectDrift(string workflowId)
    {
        var newAlerts = new List<OperationalDriftAlert>();
        var events = _timeline.GetEvents(workflowId);
        if (events.Count == 0) return newAlerts;

        // 1. AbandonedWorkflow
        // No progress events for > 30 min with active user activity elsewhere
        var lastEvent = events.OrderByDescending(e => e.Timestamp).FirstOrDefault();
        if (lastEvent != null)
        {
            var inactiveDuration = DateTimeOffset.UtcNow - lastEvent.Timestamp;
            if (inactiveDuration > TimeSpan.FromMinutes(30))
            {
                // Verify user is active elsewhere (simulated via world model or document changes)
                if (!string.IsNullOrEmpty(_worldModel.ActiveDocument) || _worldModel.BrowserTabsCount > 0)
                {
                    newAlerts.Add(new OperationalDriftAlert
                    {
                        WorkflowId = workflowId,
                        Type = OperationalDriftType.AbandonedWorkflow,
                        Severity = DriftSeverity.High,
                        Description = "Workflow shows no progress for 30 minutes, but user activity continues elsewhere.",
                        Evidence = new List<string>
                        {
                            $"Last event recorded at {lastEvent.Timestamp}",
                            $"Inactive duration: {inactiveDuration.TotalMinutes:F1} minutes",
                            $"Active Document: {_worldModel.ActiveDocument}"
                        },
                        Recommendation = "SuggestPause"
                    });
                }
            }
        }

        // 2. ExecutionStagnation
        // Same step retried > 3 times without progress
        var retryEvents = events.Where(e => e.EventType.Equals("StepRetry", StringComparison.OrdinalIgnoreCase)).ToList();
        var retryGroups = retryEvents.GroupBy(e => e.Description).Where(g => g.Count() > 3).ToList();
        foreach (var group in retryGroups)
        {
            newAlerts.Add(new OperationalDriftAlert
            {
                WorkflowId = workflowId,
                Type = OperationalDriftType.ExecutionStagnation,
                Severity = DriftSeverity.Critical,
                Description = $"Execution stagnation detected on step: {group.Key}. Retried {group.Count()} times.",
                Evidence = group.Select(e => $"Retry at {e.Timestamp}").ToList(),
                Recommendation = "SuggestPause"
            });
        }

        // 3. PerpetualRetry
        // Retry count exceeding telemetry threshold with declining success rate
        var summary = _telemetry.GetSummary();
        if (summary.RetryFrequency > 10)
        {
            newAlerts.Add(new OperationalDriftAlert
            {
                WorkflowId = workflowId,
                Type = OperationalDriftType.PerpetualRetry,
                Severity = DriftSeverity.High,
                Description = $"High frequency of retries detected in execution telemetry.",
                Evidence = new List<string> { $"Cumulative retries: {summary.RetryFrequency}" },
                Recommendation = "SuggestRestructure"
            });
        }

        // 4. ContextCollapse
        // Environmental constraints accumulated blocking execution
        var constraints = _worldModel.EnvironmentalConstraints;
        if (constraints.ContainsKey("network_offline") || constraints.ContainsKey("permission_denied"))
        {
            newAlerts.Add(new OperationalDriftAlert
            {
                WorkflowId = workflowId,
                Type = OperationalDriftType.ContextCollapse,
                Severity = DriftSeverity.Critical,
                Description = "Blocked by critical environmental constraints.",
                Evidence = constraints.Select(c => $"{c.Key}: {c.Value}").ToList(),
                Recommendation = "SuggestPause"
            });
        }

        // 5. WorkflowLoop
        // Pause/resume cycle > 3 times without step completion
        var stateEvents = events.Where(e => e.EventType.Equals("WorkflowPause", StringComparison.OrdinalIgnoreCase) || 
                                            e.EventType.Equals("WorkflowResume", StringComparison.OrdinalIgnoreCase)).ToList();
        if (stateEvents.Count > 6) // > 3 pause/resumes
        {
            newAlerts.Add(new OperationalDriftAlert
            {
                WorkflowId = workflowId,
                Type = OperationalDriftType.WorkflowLoop,
                Severity = DriftSeverity.Medium,
                Description = "Excessive pause/resume cycle loop detected.",
                Evidence = stateEvents.Select(e => $"{e.EventType} at {e.Timestamp}").ToList(),
                Recommendation = "SuggestClarification"
            });
        }

        // Store new alerts
        if (newAlerts.Count > 0)
        {
            var list = _alerts.GetOrAdd(workflowId, _ => new List<OperationalDriftAlert>());
            lock (list)
            {
                foreach (var na in newAlerts)
                {
                    if (!list.Any(existing => existing.Type == na.Type && existing.Description == na.Description && existing.Status == DriftAlertStatus.Pending))
                    {
                        list.Add(na);
                        
                        _eventBus.Publish(new EventEnvelope
                        {
                            EventType = "automation.drift.detected",
                            Source = "operational_drift_engine",
                            Payload = na
                        });
                    }
                }
            }
            SaveAlerts(workflowId);
        }

        return GetAlerts(workflowId);
    }
}
