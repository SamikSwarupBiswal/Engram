using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Engram.Store.Automation;

public enum MutationStatus
{
    Pending,
    Completed,
    Reverted,
    Failed,
    Uncertain
}

public class TrackedMutation
{
    public string StepId { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public MutationStatus Status { get; set; } = MutationStatus.Pending;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public class PartialMutationTracker
{
    private readonly ConcurrentBag<TrackedMutation> _mutations = new();

    public void TrackMutation(string stepId, string target, MutationStatus status)
    {
        var existing = _mutations.FirstOrDefault(m => m.StepId == stepId && m.Target == target);
        if (existing != null)
        {
            existing.Status = status;
        }
        else
        {
            _mutations.Add(new TrackedMutation
            {
                StepId = stepId,
                Target = target,
                Status = status
            });
        }
    }

    public List<TrackedMutation> GetUncertainMutations()
    {
        return _mutations.Where(m => m.Status == MutationStatus.Uncertain).ToList();
    }

    public List<TrackedMutation> GetCompletedMutations()
    {
        return _mutations.Where(m => m.Status == MutationStatus.Completed).ToList();
    }

    public void Clear()
    {
        _mutations.Clear();
    }
}
