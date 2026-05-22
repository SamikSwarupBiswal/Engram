using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Engram.Store.Automation;

public class TimelineEvent
{
    public string TimelineEventId { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string WorkflowId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

public class OperationalTimeline
{
    private readonly string _timelineDir;
    private readonly object _lock = new();

    public OperationalTimeline(string? customBaseDir = null)
    {
        var baseDir = customBaseDir ?? Path.Combine(Environment.CurrentDirectory, ".engram");
        _timelineDir = Path.Combine(baseDir, "automation", "timeline");
        Directory.CreateDirectory(_timelineDir);
    }

    public void RecordEvent(string workflowId, string eventType, string description)
    {
        if (string.IsNullOrEmpty(workflowId)) return;

        var ev = new TimelineEvent
        {
            WorkflowId = workflowId,
            EventType = eventType,
            Description = description,
            Timestamp = DateTimeOffset.UtcNow
        };

        RecordEvent(ev);
    }

    public void RecordEvent(TimelineEvent ev)
    {
        if (ev == null || string.IsNullOrEmpty(ev.WorkflowId)) return;

        var filePath = Path.Combine(_timelineDir, $"{ev.WorkflowId}.jsonl");

        lock (_lock)
        {
            try
            {
                var line = JsonSerializer.Serialize(ev);
                File.AppendAllText(filePath, line + Environment.NewLine);
            }
            catch
            {
                // Guarantee execution doesn't block on logging failures
            }
        }
    }

    public List<TimelineEvent> GetEvents(string workflowId)
    {
        var list = new List<TimelineEvent>();
        if (string.IsNullOrEmpty(workflowId)) return list;

        var filePath = Path.Combine(_timelineDir, $"{workflowId}.jsonl");

        lock (_lock)
        {
            try
            {
                if (!File.Exists(filePath)) return list;

                var lines = File.ReadAllLines(filePath);
                foreach (var line in lines)
                {
                    if (string.IsNullOrEmpty(line)) continue;
                    var ev = JsonSerializer.Deserialize<TimelineEvent>(line);
                    if (ev != null)
                    {
                        list.Add(ev);
                    }
                }
            }
            catch
            {
                // Return whatever we could read
            }
        }

        return list;
    }
}
