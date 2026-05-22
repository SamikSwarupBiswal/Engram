using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Automation;

public class ExecutionReplayEvent
{
    public string EventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
}

public class ExecutionReplay
{
    public string WorkflowId { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public List<ExecutionReplayEvent> Events { get; set; } = new();
    public TimeSpan TotalDuration { get; set; }
    public int StepsCompleted { get; set; }
    public int StepsFailed { get; set; }
    public int Interruptions { get; set; }
    public int Recoveries { get; set; }
    public List<(DateTimeOffset Timestamp, double Confidence)> ConfidenceTrajectory { get; set; } = new();
}

public class ExecutionReplayComparison
{
    public string WorkflowId1 { get; set; } = string.Empty;
    public string WorkflowId2 { get; set; } = string.Empty;
    public double SimilarityScore { get; set; }
    public List<string> Differences { get; set; } = new();
}

public class ExecutionReplayEngine
{
    private readonly OperationalTimeline _timeline;
    private readonly WorkflowPersistenceStore _persistenceStore;
    private readonly ILogger? _logger;

    public ExecutionReplayEngine(
        OperationalTimeline timeline,
        WorkflowPersistenceStore persistenceStore,
        ILogger? logger = null)
    {
        _timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));
        _persistenceStore = persistenceStore ?? throw new ArgumentNullException(nameof(persistenceStore));
        _logger = logger;
    }

    public async Task<ExecutionReplay> LoadReplayAsync(string workflowId)
    {
        if (string.IsNullOrEmpty(workflowId)) throw new ArgumentException("Workflow ID cannot be empty", nameof(workflowId));

        var replay = new ExecutionReplay { WorkflowId = workflowId };

        var checkpoint = await _persistenceStore.LoadCheckpointAsync(workflowId);
        if (checkpoint != null)
        {
            replay.Goal = checkpoint.Goal;
        }

        var events = _timeline.GetEvents(workflowId);
        if (events.Count > 0)
        {
            var sortedEvents = events.OrderBy(e => e.Timestamp).ToList();
            var first = sortedEvents.First();
            var last = sortedEvents.Last();
            replay.TotalDuration = last.Timestamp - first.Timestamp;

            foreach (var ev in sortedEvents)
            {
                replay.Events.Add(new ExecutionReplayEvent
                {
                    EventId = ev.TimelineEventId,
                    EventType = ev.EventType,
                    Description = ev.Description,
                    Timestamp = ev.Timestamp
                });

                if (ev.EventType.Equals("StepCompleted", StringComparison.OrdinalIgnoreCase))
                {
                    replay.StepsCompleted++;
                }
                else if (ev.EventType.Equals("StepFailed", StringComparison.OrdinalIgnoreCase))
                {
                    replay.StepsFailed++;
                }
                else if (ev.EventType.Equals("WorkflowPause", StringComparison.OrdinalIgnoreCase))
                {
                    replay.Interruptions++;
                }
                else if (ev.EventType.Equals("StepRetry", StringComparison.OrdinalIgnoreCase) || 
                         ev.EventType.Equals("RecoverySuccess", StringComparison.OrdinalIgnoreCase))
                {
                    replay.Recoveries++;
                }
            }

            // Construct Confidence Trajectory
            double currentConfidence = 1.0;
            foreach (var ev in sortedEvents)
            {
                if (ev.EventType.Equals("StepFailed", StringComparison.OrdinalIgnoreCase))
                {
                    currentConfidence = Math.Max(0.0, currentConfidence - 0.2);
                }
                else if (ev.EventType.Equals("StepCompleted", StringComparison.OrdinalIgnoreCase))
                {
                    currentConfidence = Math.Min(1.0, currentConfidence + 0.05);
                }
                else if (ev.EventType.Equals("WorkflowPause", StringComparison.OrdinalIgnoreCase))
                {
                    currentConfidence = Math.Max(0.0, currentConfidence - 0.1);
                }

                replay.ConfidenceTrajectory.Add((ev.Timestamp, currentConfidence));
            }
        }

        return replay;
    }

    public List<ExecutionReplayEvent> GetEventSequence(string workflowId)
    {
        var events = _timeline.GetEvents(workflowId);
        return events.Select(ev => new ExecutionReplayEvent
        {
            EventId = ev.TimelineEventId,
            EventType = ev.EventType,
            Description = ev.Description,
            Timestamp = ev.Timestamp
        }).OrderBy(e => e.Timestamp).ToList();
    }

    public async Task<ExecutionReplayComparison> CompareAsync(string workflowId1, string workflowId2)
    {
        var replay1 = await LoadReplayAsync(workflowId1);
        var replay2 = await LoadReplayAsync(workflowId2);

        var comparison = new ExecutionReplayComparison
        {
            WorkflowId1 = workflowId1,
            WorkflowId2 = workflowId2
        };

        int matchPoints = 0;
        int totalPoints = 5;

        // Goal similarity
        if (replay1.Goal.Equals(replay2.Goal, StringComparison.OrdinalIgnoreCase))
        {
            matchPoints++;
        }
        else
        {
            comparison.Differences.Add($"Goals differ: '{replay1.Goal}' vs '{replay2.Goal}'");
        }

        // Steps completed comparison
        if (replay1.StepsCompleted == replay2.StepsCompleted)
        {
            matchPoints++;
        }
        else
        {
            comparison.Differences.Add($"Steps completed differ: {replay1.StepsCompleted} vs {replay2.StepsCompleted}");
        }

        // Steps failed comparison
        if (replay1.StepsFailed == replay2.StepsFailed)
        {
            matchPoints++;
        }
        else
        {
            comparison.Differences.Add($"Steps failed differ: {replay1.StepsFailed} vs {replay2.StepsFailed}");
        }

        // Interruptions comparison
        if (replay1.Interruptions == replay2.Interruptions)
        {
            matchPoints++;
        }
        else
        {
            comparison.Differences.Add($"Interruptions count differs: {replay1.Interruptions} vs {replay2.Interruptions}");
        }

        // Duration similarity
        var durationDiff = Math.Abs((replay1.TotalDuration - replay2.TotalDuration).TotalSeconds);
        if (durationDiff < 60.0)
        {
            matchPoints++;
        }
        else
        {
            comparison.Differences.Add($"Duration difference exceeds threshold: {replay1.TotalDuration.TotalSeconds:F1}s vs {replay2.TotalDuration.TotalSeconds:F1}s");
        }

        comparison.SimilarityScore = (double)matchPoints / totalPoints;

        return comparison;
    }
}
