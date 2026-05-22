using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Engram.Store.Inference;

namespace Engram.Store.Automation;

public class CognitiveLoopResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public int StepsExecuted { get; set; }
    public int ReplanCount { get; set; }
    public List<string> History { get; set; } = new();
}

public class CognitiveActionLoop
{
    private readonly TaskPlanner _planner;
    private readonly ActionRuntime _runtime;
    private readonly LocalInferenceEngine? _inferenceEngine;
    private readonly ILogger<CognitiveActionLoop>? _logger;

    public CognitiveActionLoop(
        TaskPlanner planner,
        ActionRuntime runtime,
        LocalInferenceEngine? inferenceEngine = null,
        ILogger<CognitiveActionLoop>? logger = null)
    {
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _inferenceEngine = inferenceEngine;
        _logger = logger;
    }

    public async Task<CognitiveLoopResult> RunAsync(string goal, ExecutionContext context, CancellationToken ct = default)
    {
        _logger?.LogInformation("Starting cognitive action loop for goal: '{Goal}'", goal);
        
        var result = new CognitiveLoopResult();
        result.History.Add($"Starting loop for goal: '{goal}'");

        // 1. Generate initial plan
        ExecutionPlan currentPlan;
        try
        {
            currentPlan = await _planner.PlanAsync(goal, ct);
            result.History.Add($"Initial plan generated with {currentPlan.Steps.Count} steps.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to generate initial plan.");
            result.Success = false;
            result.Message = $"Initial planning failed: {ex.Message}";
            return result;
        }

        int maxReplans = 3;
        int replanCount = 0;

        while (true)
        {
            try
            {
                _logger?.LogInformation("Executing plan (Replan count: {ReplanCount})...", replanCount);
                await _runtime.ExecutePlanAsync(currentPlan, context, ct);
                
                // If we reach here, execution succeeded!
                result.Success = true;
                result.Message = "Goal achieved successfully.";
                result.StepsExecuted += currentPlan.Steps.Count;
                result.ReplanCount = replanCount;
                result.History.Add("Plan execution completed successfully.");
                return result;
            }
            catch (Exception ex)
            {
                result.StepsExecuted += GetExecutedStepsCount(currentPlan);
                _logger?.LogWarning(ex, "Plan execution failed on step. Exception: {Error}", ex.Message);
                result.History.Add($"Execution failed: {ex.Message}");

                if (_inferenceEngine == null || !_inferenceEngine.IsReady)
                {
                    _logger?.LogWarning("Inference engine not ready or not available. Cannot perform cognitive replanning.");
                    result.Success = false;
                    result.Message = $"Execution failed and cannot replan: {ex.Message}";
                    result.ReplanCount = replanCount;
                    return result;
                }

                if (replanCount >= maxReplans)
                {
                    _logger?.LogError("Maximum replan attempts ({MaxReplans}) reached. Aborting.", maxReplans);
                    result.Success = false;
                    result.Message = $"Maximum replan attempts ({maxReplans}) exceeded. Execution failed after {replanCount} replans: {ex.Message}";
                    result.ReplanCount = replanCount;
                    return result;
                }

                replanCount++;
                _logger?.LogInformation("Initiating cognitive plan repair/replan (attempt {ReplanCount}/{MaxReplans})...", replanCount, maxReplans);
                result.History.Add($"Attempting plan repair {replanCount}/{maxReplans}...");

                try
                {
                    currentPlan = await RepairPlanAsync(goal, currentPlan, context, ex.Message, ct);
                    result.History.Add($"Plan repaired successfully. New plan has {currentPlan.Steps.Count} steps.");
                }
                catch (Exception repairEx)
                {
                    _logger?.LogError(repairEx, "Failed to repair plan.");
                    result.Success = false;
                    result.Message = $"Plan repair failed: {repairEx.Message}. Original error: {ex.Message}";
                    result.ReplanCount = replanCount;
                    return result;
                }
            }
        }
    }

    private int GetExecutedStepsCount(ExecutionPlan plan)
    {
        int count = 0;
        foreach (var step in plan.Steps.Values)
        {
            if (step.Status == StepStatus.Completed)
                count++;
        }
        return count;
    }

    private async Task<ExecutionPlan> RepairPlanAsync(
        string goal, 
        ExecutionPlan failedPlan, 
        ExecutionContext context, 
        string errorMessage, 
        CancellationToken ct)
    {
        // Construct detailed state for LLM
        var stepsState = new List<object>();
        foreach (var step in failedPlan.Steps.Values)
        {
            stepsState.Add(new
            {
                id = step.Id,
                type = step.Action.Type.ToString(),
                description = step.Action.Description,
                value = step.Action.Value,
                target = step.Action.Target?.Selector,
                status = step.Status.ToString(),
                error = step.Error,
                result = step.Action.Result
            });
        }

        var variablesCopy = new Dictionary<string, string>();
        foreach (var kvp in context.Variables)
        {
            variablesCopy[kvp.Key] = kvp.Value?.ToString() ?? string.Empty;
        }

        var systemPrompt = """
You are Engram's automation plan repair assistant.
An execution plan was running to achieve the user's goal, but it failed on a step.
Analyze the execution history, current context variables, and the failure error.
Generate a new, corrected, cycle-validated JSON execution plan to achieve the goal from the current state.
Include ONLY the remaining steps that need to be run. Do NOT include successfully completed steps unless they need to be repeated.
Supported Action Types: Navigate, Click, Type, KeyPress, Wait, Screenshot, Scroll.

You must return a JSON array of steps in the following schema:
[
  {
    "id": "step_id",
    "type": "ActionType",
    "description": "Short explanation of the step",
    "value": "Optional string value (e.g. URL, text to type, or wait time in milliseconds)",
    "target": {
      "selector": "CSS selector for target element (if applicable)"
    },
    "dependsOn": ["list_of_dependency_step_ids"]
  }
]

Do not include any chat commentary or conversational text. Return ONLY the raw JSON array.
""";

        var userMessage = $@"Goal: '{goal}'
Failure Error: {errorMessage}
Context Variables: {JsonSerializer.Serialize(variablesCopy)}
Plan Execution History: {JsonSerializer.Serialize(stepsState)}";

        var messages = new ChatMessage[]
        {
            new ChatMessage { Role = "system", Content = systemPrompt },
            new ChatMessage { Role = "user", Content = userMessage }
        };

        var result = await _inferenceEngine!.ChatCompletionAsync(messages, 1024, cancellationToken: ct);
        if (!result.Success || string.IsNullOrEmpty(result.Content))
            throw new InvalidOperationException("Failed to generate plan repair response from inference engine.");

        var json = ExtractJsonArray(result.Content);
        if (string.IsNullOrEmpty(json))
            throw new InvalidOperationException($"Invalid plan repair JSON returned by inference engine: {result.Content}");

        var plannedSteps = JsonSerializer.Deserialize<List<PlannedStepDto>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (plannedSteps == null || plannedSteps.Count == 0)
            throw new InvalidOperationException("Repaired plan is empty.");

        var repairedPlan = new ExecutionPlan { Goal = goal };
        foreach (var dto in plannedSteps)
        {
            var actionType = Enum.TryParse<ActionType>(dto.Type, true, out var t) ? t : ActionType.Wait;
            var action = new AutomationAction
            {
                ActionId = dto.Id,
                Type = actionType,
                Description = dto.Description ?? $"{dto.Type} action",
                Value = dto.Value,
                Target = dto.Target != null ? new ActionTarget { Selector = dto.Target.Selector } : null
            };

            var step = new ExecutionStep
            {
                Id = dto.Id,
                Action = action,
                DependsOn = dto.DependsOn ?? new List<string>()
            };

            repairedPlan.Steps[step.Id] = step;
        }

        repairedPlan.Validate();
        return repairedPlan;
    }

    private static string ExtractJsonArray(string content)
    {
        var start = content.IndexOf('[');
        var end = content.LastIndexOf(']');
        if (start != -1 && end != -1 && end > start)
        {
            return content.Substring(start, end - start + 1);
        }
        return string.Empty;
    }

    private class PlannedStepDto
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Value { get; set; }
        public TargetDto? Target { get; set; }
        public List<string>? DependsOn { get; set; }
    }

    private class TargetDto
    {
        public string? Selector { get; set; }
    }
}
