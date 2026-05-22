using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Engram.Store.Identity;

namespace Engram.Store.Automation;

public class WorkflowPriority
{
    public string WorkflowId { get; set; } = string.Empty;
    public double PriorityScore { get; set; } = 0.0;
    public int Rank { get; set; }
    public bool ShouldSuspend { get; set; }
    public TimeSpan AttentionBudget { get; set; }
}

public class OperationalPriorityGraph
{
    private readonly OperationalWorldModel _worldModel;
    private readonly WorkflowConfidenceEngine _confidenceEngine;
    private readonly IdentityStore? _identityStore;
    private readonly ILogger? _logger;

    public OperationalPriorityGraph(
        OperationalWorldModel worldModel,
        WorkflowConfidenceEngine confidenceEngine,
        IdentityStore? identityStore = null,
        ILogger? logger = null)
    {
        _worldModel = worldModel ?? throw new ArgumentNullException(nameof(worldModel));
        _confidenceEngine = confidenceEngine ?? throw new ArgumentNullException(nameof(confidenceEngine));
        _identityStore = identityStore;
        _logger = logger;
    }

    public List<WorkflowPriority> ComputePriorities(IEnumerable<string> activeWorkflowIds, ExecutionContext context)
    {
        if (activeWorkflowIds == null) throw new ArgumentNullException(nameof(activeWorkflowIds));

        // 1. Determine Dynamic Priority Fusion Weights based on context
        // Default: Normal State
        double identityWeight = 0.4;
        double operationalWeight = 0.6;

        var constraints = _worldModel.EnvironmentalConstraints;
        if (constraints.ContainsKey("deadline_pressure") || constraints.ContainsKey("high_priority_deadline"))
        {
            // Deadline pressure state: focus heavily on operational urgency
            identityWeight = 0.15;
            operationalWeight = 0.85;
            _logger?.LogDebug("Dynamic Priority Fusion: Deadline pressure state active.");
        }
        else if (constraints.ContainsKey("recovery") || constraints.ContainsKey("fatigue") || constraints.ContainsKey("recovery_mode"))
        {
            // Recovery/fatigue state: focus heavily on identity/alignment
            identityWeight = 0.7;
            operationalWeight = 0.3;
            _logger?.LogDebug("Dynamic Priority Fusion: Recovery/fatigue state active.");
        }

        var list = new List<WorkflowPriority>();
        var idList = activeWorkflowIds.ToList();

        // Load identity details once if store is available
        var userGoals = new List<string>();
        var userPriorities = new List<string>();

        if (_identityStore != null)
        {
            try
            {
                var profile = _identityStore.LoadProfile();
                if (profile != null)
                {
                    userGoals.AddRange(profile.Goals);
                }
                var priorities = _identityStore.LoadPriorities();
                if (priorities != null)
                {
                    userPriorities.AddRange(priorities.Select(p => p.Description));
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load identity details during priority computation");
            }
        }

        foreach (var workflowId in idList)
        {
            // Try to extract goal and steps from context or build dummy plan if none exists
            var goal = context.GetVariable<string>($"workflow.{workflowId}.goal") ?? "Generic Automation";
            var plan = context.GetVariable<ExecutionPlan>($"workflow.{workflowId}.plan") ?? new ExecutionPlan { Goal = goal };

            // Compute confidence
            var confidence = _confidenceEngine.ComputeConfidence(workflowId, plan, context);

            // Compute operational score
            double stepRatio = plan.Steps.Count > 0 
                ? (double)plan.Steps.Values.Count(s => s.Status == StepStatus.Completed) / plan.Steps.Count 
                : 0.5;

            // Recency of progress
            double recencyScore = 1.0;
            if (_worldModel.ActiveWorkflow == workflowId)
            {
                recencyScore = 1.0;
            }
            else
            {
                recencyScore = 0.5; // lower priority for background workflows
            }

            double operationalScore = (confidence.ExecutionConfidence * 0.4) + (recencyScore * 0.3) + (stepRatio * 0.3);

            // Compute identity alignment score
            double identityScore = 0.5; // default baseline
            if (userGoals.Count > 0 || userPriorities.Count > 0)
            {
                int matches = 0;
                foreach (var g in userGoals)
                {
                    if (goal.Contains(g, StringComparison.OrdinalIgnoreCase) || g.Contains(goal, StringComparison.OrdinalIgnoreCase))
                        matches++;
                }
                foreach (var p in userPriorities)
                {
                    if (goal.Contains(p, StringComparison.OrdinalIgnoreCase) || p.Contains(goal, StringComparison.OrdinalIgnoreCase))
                        matches++;
                }
                if (matches > 0)
                {
                    identityScore = Math.Min(1.0, 0.5 + (matches * 0.25));
                }
                else
                {
                    identityScore = 0.3; // penalize unrelated tasks when goals exist
                }
            }

            // Fuse scores
            double score = (identityScore * identityWeight) + (operationalScore * operationalWeight);
            score = Math.Clamp(score, 0.0, 1.0);

            list.Add(new WorkflowPriority
            {
                WorkflowId = workflowId,
                PriorityScore = score,
                AttentionBudget = TimeSpan.FromMinutes(score * 120) // Up to 2 hours
            });
        }

        // Sort by priority score descending and assign rank
        var sorted = list.OrderByDescending(p => p.PriorityScore).ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            sorted[i].Rank = i + 1;
        }

        // Determine if low priority should suspend
        double highestScore = sorted.Count > 0 ? sorted[0].PriorityScore : 0.0;
        foreach (var p in sorted)
        {
            if (p.PriorityScore < 0.2 && highestScore > 0.7)
            {
                p.ShouldSuspend = true;
            }
        }

        return sorted;
    }
}
