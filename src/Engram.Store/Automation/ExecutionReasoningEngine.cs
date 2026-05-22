using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Engram.Store.Inference;

namespace Engram.Store.Automation;

/// <summary>
/// Execution Reasoning Engine runs the plan observation, reasoning, branch prediction, and adaptation loops.
/// </summary>
public class ExecutionReasoningEngine
{
    private readonly LocalInferenceEngine? _inferenceEngine;
    private readonly OperationalWorldModel _worldModel;
    private readonly ILogger<ExecutionReasoningEngine>? _logger;

    public ExecutionReasoningEngine(
        OperationalWorldModel worldModel,
        LocalInferenceEngine? inferenceEngine = null,
        ILogger<ExecutionReasoningEngine>? logger = null)
    {
        _worldModel = worldModel ?? throw new ArgumentNullException(nameof(worldModel));
        _inferenceEngine = inferenceEngine;
        _logger = logger;
    }

    public async Task<ExecutionPlan> ReasonAndAdaptAsync(
        string goal,
        ExecutionPlan currentPlan,
        ExecutionContext context,
        string observation,
        CancellationToken ct = default)
    {
        _logger?.LogInformation("ExecutionReasoningEngine: Analyzing current execution trajectory for goal: '{Goal}'", goal);
        _worldModel.CurrentPhase = "Reasoning";

        if (_inferenceEngine == null || !_inferenceEngine.IsReady)
        {
            _logger?.LogWarning("Inference engine not ready. Falling back to plan repair heuristics.");
            return currentPlan; // Fallback: return unchanged
        }

        // Format historical steps
        var stepsList = new List<object>();
        foreach (var step in currentPlan.Steps.Values)
        {
            stepsList.Add(new
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
            if (kvp.Value is string or int or double or float or decimal or bool)
            {
                variablesCopy[kvp.Key] = kvp.Value.ToString() ?? string.Empty;
            }
        }

        var systemPrompt = """
You are Engram's Execution Reasoning Engine.
The agent is currently executing an automation plan to achieve the user's goal, but has run into an obstacle or is checking if the plan should be optimized.
Analyze the goal, current variables, the execution history of the plan, and the observation.
Generate a new, revised JSON execution plan to continue execution and achieve the goal.
Include ONLY the steps that need to run from the current state (either new recovery steps, alternative paths, or remaining steps). Do NOT include successfully completed steps.
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

Do not include any conversational commentary. Return ONLY the raw JSON array.
""";

        var userMessage = $@"Goal: '{goal}'
Observation: {observation}
Context Variables: {JsonSerializer.Serialize(variablesCopy)}
Plan Execution History: {JsonSerializer.Serialize(stepsList)}";

        var messages = new ChatMessage[]
        {
            new ChatMessage { Role = "system", Content = systemPrompt },
            new ChatMessage { Role = "user", Content = userMessage }
        };

        var inferenceResult = await _inferenceEngine.ChatCompletionAsync(messages, 1024, cancellationToken: ct);
        if (!inferenceResult.Success || string.IsNullOrEmpty(inferenceResult.Content))
        {
            throw new InvalidOperationException("Failed to generate response from inference engine during reasoning.");
        }

        var json = ExtractJsonArray(inferenceResult.Content);
        if (string.IsNullOrEmpty(json))
        {
            _logger?.LogWarning("Inference engine output did not contain a valid JSON array: {Content}", inferenceResult.Content);
            return currentPlan;
        }

        var plannedSteps = JsonSerializer.Deserialize<List<PlannedStepDto>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (plannedSteps == null || plannedSteps.Count == 0)
        {
            _logger?.LogWarning("Deserialized plan is empty. Returning original plan.");
            return currentPlan;
        }

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
        
        // Log to trajectory
        _worldModel.AddTrajectoryMilestone($"Plan adapted due to: {observation}");
        _worldModel.ExecutionConfidence = 0.92;

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
