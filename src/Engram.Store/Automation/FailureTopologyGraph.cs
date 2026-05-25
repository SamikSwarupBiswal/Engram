using System;
using System.Collections.Generic;
using System.Linq;

namespace Engram.Store.Automation;

public class FailureImpact
{
    public bool CanRollbackCleanly { get; set; } = true;
    public List<string> UncertainSteps { get; set; } = new();
    public List<string> BlockedSteps { get; set; } = new();
    public List<TrackedPropagation> UnresolvedPropagations { get; set; } = new();
}

/// <summary>
/// Models cascading failure effects and maps downstream mutation dependencies.
/// </summary>
public class FailureTopologyGraph
{
    private readonly Dictionary<string, MutationBoundarySemantics> _semantics = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<TrackedPropagation>> _stepPropagations = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterStepSemantics(string stepId, MutationBoundarySemantics semantics)
    {
        _semantics[stepId] = semantics;
    }

    public void RegisterPropagation(string stepId, TrackedPropagation propagation)
    {
        if (!_stepPropagations.TryGetValue(stepId, out var list))
        {
            list = new List<TrackedPropagation>();
            _stepPropagations[stepId] = list;
        }
        list.Add(propagation);
    }

    /// <summary>
    /// Analyzes the impact of a failure at a given step on previously completed steps and downstream steps.
    /// </summary>
    public FailureImpact AssessFailureImpact(string failedStepId, List<ExecutionStep> completedSteps)
    {
        var impact = new FailureImpact();

        // 1. Identify downstream steps that depend directly or indirectly on the failed step
        // We will build a list of blocked steps.
        var blockedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        blockedSet.Add(failedStepId);

        // Simple BFS/DFS to find all downstream dependent steps from completedSteps or graph dependencies
        bool added;
        do
        {
            added = false;
            foreach (var step in completedSteps)
            {
                if (!blockedSet.Contains(step.Id) && step.DependsOn.Any(d => blockedSet.Contains(d)))
                {
                    if (blockedSet.Add(step.Id))
                    {
                        impact.BlockedSteps.Add(step.Id);
                        added = true;
                    }
                }
            }
        } while (added);

        // 2. Audit already completed steps for rollback feasibility
        foreach (var step in completedSteps)
        {
            if (step.Id.Equals(failedStepId, StringComparison.OrdinalIgnoreCase)) continue;

            // If the completed step has non-reversible or external propagation semantics
            if (_semantics.TryGetValue(step.Id, out var semantics))
            {
                if (semantics.IsIrreversible || !semantics.IsReversible)
                {
                    impact.CanRollbackCleanly = false;
                    impact.UncertainSteps.Add(step.Id);
                }

                if (semantics.IsExternallyPropagated)
                {
                    impact.CanRollbackCleanly = false;
                    if (_stepPropagations.TryGetValue(step.Id, out var props))
                    {
                        impact.UnresolvedPropagations.AddRange(props);
                    }
                }
            }
        }

        return impact;
    }
}
