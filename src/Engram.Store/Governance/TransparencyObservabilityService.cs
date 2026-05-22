using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Engram.Store.Governance;

/// <summary>
/// Service coordinating transparency feeds, audit logs, and graph observability.
/// </summary>
public class TransparencyObservabilityService
{
    private readonly string _activityFilePath;
    private readonly string _auditFilePath;
    private readonly List<ActivityEntry> _activityFeed = new();
    private readonly List<string> _actionTimeline = new();
    private readonly List<string> _interventionAudit = new();
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public TransparencyObservabilityService(WorkspacePaths paths)
    {
        var dir = Path.Combine(paths.Config, "governance");
        Directory.CreateDirectory(dir);
        _activityFilePath = Path.Combine(dir, "activity_feed.json");
        _auditFilePath = Path.Combine(dir, "intervention_audit.json");
        LoadLogs();
    }

    /// <summary>
    /// Appends a new event into the Semantic Activity Feed.
    /// </summary>
    public void LogActivity(string action, string description, string relatedNodeId = "", string impact = "Low")
    {
        var entry = new ActivityEntry
        {
            Action = action,
            Description = description,
            RelatedNodeId = relatedNodeId,
            ImpactLevel = impact
        };

        lock (_lock)
        {
            _activityFeed.Add(entry);
            if (_activityFeed.Count > 1000) _activityFeed.RemoveAt(0);
            SaveLogs();
        }
    }

    /// <summary>
    /// Log automation/workflow replay events.
    /// </summary>
    public void LogActionReplay(string actionDescription)
    {
        lock (_lock)
        {
            var entry = $"[{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}] {actionDescription}";
            _actionTimeline.Add(entry);
            if (_actionTimeline.Count > 500) _actionTimeline.RemoveAt(0);
        }
    }

    /// <summary>
    /// Log user intervention feedbacks (dismissals, ignores).
    /// </summary>
    public void LogInterventionFeedback(string interventionId, string feedbackType, string detail)
    {
        lock (_lock)
        {
            var entry = $"[{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}] Intervention {interventionId} - feedback: {feedbackType} ({detail})";
            _interventionAudit.Add(entry);
            if (_interventionAudit.Count > 500) _interventionAudit.RemoveAt(0);
            SaveLogs();
        }
    }

    public IReadOnlyList<ActivityEntry> GetActivityFeed()
    {
        lock (_lock) { return _activityFeed.ToList(); }
    }

    public IReadOnlyList<string> GetActionTimeline()
    {
        lock (_lock) { return _actionTimeline.ToList(); }
    }

    public IReadOnlyList<string> GetInterventionAudit()
    {
        lock (_lock) { return _interventionAudit.ToList(); }
    }

    private void LoadLogs()
    {
        lock (_lock)
        {
            if (File.Exists(_activityFilePath))
            {
                try
                {
                    var json = File.ReadAllText(_activityFilePath);
                    var loaded = JsonSerializer.Deserialize<List<ActivityEntry>>(json, JsonOptions);
                    if (loaded != null)
                    {
                        _activityFeed.Clear();
                        _activityFeed.AddRange(loaded);
                    }
                }
                catch { }
            }

            if (File.Exists(_auditFilePath))
            {
                try
                {
                    var json = File.ReadAllText(_auditFilePath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("intervention_audit", out var pAudit))
                    {
                        _interventionAudit.Clear();
                        _interventionAudit.AddRange(pAudit.Deserialize<List<string>>() ?? new());
                    }
                    if (doc.RootElement.TryGetProperty("action_timeline", out var pTimeline))
                    {
                        _actionTimeline.Clear();
                        _actionTimeline.AddRange(pTimeline.Deserialize<List<string>>() ?? new());
                    }
                }
                catch { }
            }
        }
    }

    private void SaveLogs()
    {
        lock (_lock)
        {
            try
            {
                var tmpActivity = _activityFilePath + ".tmp";
                File.WriteAllText(tmpActivity, JsonSerializer.Serialize(_activityFeed, JsonOptions));
                File.Move(tmpActivity, _activityFilePath, overwrite: true);

                var tmpAudit = _auditFilePath + ".tmp";
                var combined = new
                {
                    intervention_audit = _interventionAudit,
                    action_timeline = _actionTimeline
                };
                File.WriteAllText(tmpAudit, JsonSerializer.Serialize(combined, JsonOptions));
                File.Move(tmpAudit, _auditFilePath, overwrite: true);
            }
            catch { }
        }
    }
}
