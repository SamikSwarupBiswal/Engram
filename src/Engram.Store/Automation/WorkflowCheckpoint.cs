using System;
using System.Collections.Generic;

namespace Engram.Store.Automation;

/// <summary>
/// Represents a snapshot of the execution state for a long-running workflow.
/// </summary>
public class WorkflowCheckpoint
{
    public string WorkflowId { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public string CurrentPhase { get; set; } = string.Empty;
    public int CurrentStepIndex { get; set; }
    public string ActiveStepId { get; set; } = string.Empty;
    public Dictionary<string, string> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> ExecutedStepIds { get; set; } = new();
    public string PlanJson { get; set; } = string.Empty;
    public DateTimeOffset CheckpointTime { get; set; } = DateTimeOffset.UtcNow;
}
