using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Engram.Store.Automation;

public class TrackedPropagation
{
    public string WorkflowId { get; init; } = string.Empty;
    public string StepId { get; init; } = string.Empty;
    public string DestinationType { get; init; } = string.Empty; // e.g. "Email", "UrlUpload", "API"
    public string DestinationValue { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public class ExternalPropagationTracker
{
    private readonly List<TrackedPropagation> _propagations = new();
    private readonly object _lock = new();

    public void TrackPropagation(string workflowId, string stepId, string destinationType, string destinationValue)
    {
        lock (_lock)
        {
            _propagations.Add(new TrackedPropagation
            {
                WorkflowId = workflowId,
                StepId = stepId,
                DestinationType = destinationType,
                DestinationValue = destinationValue
            });
        }
    }

    public List<TrackedPropagation> GetPropagations(string workflowId)
    {
        lock (_lock)
        {
            return _propagations.Where(p => p.WorkflowId.Equals(workflowId, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _propagations.Clear();
        }
    }
}
