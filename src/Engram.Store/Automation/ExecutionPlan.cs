using System;
using System.Collections.Generic;

namespace Engram.Store.Automation;

public class ExecutionPlan
{
    public string PlanId { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public string Goal { get; init; } = string.Empty;
    public Dictionary<string, ExecutionStep> Steps { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public void Validate()
    {
        // 1. Check for missing dependencies
        foreach (var step in Steps.Values)
        {
            foreach (var depId in step.DependsOn)
            {
                if (!Steps.ContainsKey(depId))
                {
                    throw new InvalidOperationException($"Step '{step.Id}' depends on missing step '{depId}'");
                }
            }
        }

        // 2. Cycle detection using Depth-First Search (DFS) three-color marking
        // Colors: 0 = Unvisited, 1 = Visiting (in recursion stack), 2 = Visited
        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in Steps.Keys)
        {
            state[key] = 0;
        }

        var path = new List<string>();

        void Dfs(string stepId)
        {
            state[stepId] = 1; // Visiting
            path.Add(stepId);

            var step = Steps[stepId];
            foreach (var depId in step.DependsOn)
            {
                if (state[depId] == 1) // Cycle detected!
                {
                    // Find the cycle path
                    var cycleStartIdx = path.IndexOf(depId);
                    var cyclePath = string.Join(" -> ", path.GetRange(cycleStartIdx, path.Count - cycleStartIdx)) + " -> " + depId;
                    throw new InvalidOperationException($"Dependency cycle detected: {cyclePath}");
                }
                else if (state[depId] == 0)
                {
                    Dfs(depId);
                }
            }

            path.RemoveAt(path.Count - 1);
            state[stepId] = 2; // Visited
        }

        foreach (var stepId in Steps.Keys)
        {
            if (state[stepId] == 0)
            {
                Dfs(stepId);
            }
        }
    }
}
