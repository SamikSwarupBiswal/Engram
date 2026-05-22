using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Engram.Store.Inference;

namespace Engram.Store.Automation;

public class TaskPlanner
{
    private readonly LocalInferenceEngine? _inferenceEngine;

    public TaskPlanner(LocalInferenceEngine? inferenceEngine = null)
    {
        _inferenceEngine = inferenceEngine;
    }

    public async Task<ExecutionPlan> PlanAsync(string userGoal, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userGoal))
            throw new ArgumentException("Goal cannot be empty", nameof(userGoal));

        // 1. If LLM is available, try to plan using LLM
        if (_inferenceEngine != null && _inferenceEngine.IsReady)
        {
            try
            {
                var plan = await PlanWithLlmAsync(userGoal, ct);
                if (plan != null && plan.Steps.Count > 0)
                {
                    plan.Validate();
                    return plan;
                }
            }
            catch
            {
                // Fall through to heuristic planner if LLM fails
            }
        }

        // 2. Heuristic planning fallback
        return PlanWithHeuristics(userGoal);
    }

    private async Task<ExecutionPlan?> PlanWithLlmAsync(string userGoal, CancellationToken ct)
    {
        var systemPrompt = """
You are Engram's automation task planner.
Convert the user's natural language goal into a cycle-validated JSON execution plan.
Supported Action Types: Navigate, Click, Type, KeyPress, Wait, Screenshot, Scroll.

You must return a JSON array of steps in the following schema:
[
  {
    "id": "step_id",
    "type": "ActionType",
    "description": "Short explanation",
    "value": "Optional string value (e.g. URL, text to type, or wait time in milliseconds)",
    "target": {
      "selector": "CSS selector for target element (if applicable)"
    },
    "dependsOn": ["list_of_dependency_step_ids"]
  }
]

Example:
Goal: 'open google.com and search for Engram'
JSON:
[
  {
    "id": "step_1",
    "type": "Navigate",
    "description": "Go to Google",
    "value": "https://google.com",
    "dependsOn": []
  },
  {
    "id": "step_2",
    "type": "Type",
    "description": "Type search term",
    "value": "Engram",
    "target": { "selector": "input[name='q']" },
    "dependsOn": ["step_1"]
  },
  {
    "id": "step_3",
    "type": "KeyPress",
    "description": "Press Enter key",
    "value": "Enter",
    "dependsOn": ["step_2"]
  }
]

Do not include any chat commentary or conversational text. Return ONLY the raw JSON array.
""";

        var messages = new ChatMessage[]
        {
            new ChatMessage { Role = "system", Content = systemPrompt },
            new ChatMessage { Role = "user", Content = $"Goal: '{userGoal}'" }
        };

        var result = await _inferenceEngine!.ChatCompletionAsync(messages, 1024, cancellationToken: ct);
        if (!result.Success || string.IsNullOrEmpty(result.Content))
            return null;

        var json = ExtractJsonArray(result.Content);
        if (string.IsNullOrEmpty(json))
            return null;

        var plannedSteps = JsonSerializer.Deserialize<List<PlannedStepDto>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (plannedSteps == null || plannedSteps.Count == 0)
            return null;

        var plan = new ExecutionPlan { Goal = userGoal };
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

            plan.Steps[step.Id] = step;
        }

        return plan;
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

    public ExecutionPlan PlanWithHeuristics(string userGoal)
    {
        var plan = new ExecutionPlan { Goal = userGoal };

        // Split by standard delimiters like: "then", "and then", ",", "and"
        var clauses = SplitGoalIntoClauses(userGoal);
        var createdStepIds = new List<string>();

        foreach (var clause in clauses)
        {
            var trimmedClause = clause.Trim();
            if (string.IsNullOrEmpty(trimmedClause)) continue;

            var action = ParseClause(trimmedClause);
            if (action == null) continue;

            var stepId = $"step_{createdStepIds.Count + 1}";
            var step = new ExecutionStep
            {
                Id = stepId,
                Action = action,
                DependsOn = createdStepIds.Count > 0 ? new() { createdStepIds[^1] } : new()
            };

            // Set ActionId to match StepId
            typeof(AutomationAction).GetProperty(nameof(AutomationAction.ActionId))?.SetValue(action, stepId);

            plan.Steps[stepId] = step;
            createdStepIds.Add(stepId);
        }

        return plan;
    }

    private static List<string> SplitGoalIntoClauses(string goal)
    {
        // Split on keywords "then", "and then", "," (comma), "and" if they appear as word boundaries
        // E.g. "go to google.com, then click search"
        var normalized = Regex.Replace(goal, @"\band\s+then\b", "then", RegexOptions.IgnoreCase);
        
        // We'll split on "then" or "," or "and" (carefully)
        var parts = Regex.Split(normalized, @"\bthen\b|[,;]", RegexOptions.IgnoreCase);
        var result = new List<string>();

        foreach (var part in parts)
        {
            var p = part.Trim();
            if (p.StartsWith("and ", StringComparison.OrdinalIgnoreCase))
            {
                p = p.Substring(4).Trim();
            }
            if (!string.IsNullOrEmpty(p))
            {
                result.Add(p);
            }
        }

        return result;
    }

    private static AutomationAction? ParseClause(string clause)
    {
        // 1. Navigate pattern
        var navigateMatch = Regex.Match(clause, @"^(?:go\s+to|navigate\s+to|open)\s+(https?://\S+|localhost:\d+\S*|\S+\.\S+)", RegexOptions.IgnoreCase);
        if (navigateMatch.Success)
        {
            var url = navigateMatch.Groups[1].Value;
            if (!url.StartsWith("http://") && !url.StartsWith("https://") && !url.StartsWith("localhost"))
            {
                url = "https://" + url;
            }
            return new AutomationAction
            {
                Type = ActionType.Navigate,
                Description = $"Navigate to {url}",
                Value = url
            };
        }

        // 2. Type pattern: type 'val' into 'target' or type 'val' in 'target'
        var typeMatch = Regex.Match(clause, @"^(?:type|enter|write)\s+['""“](.*?)['""”]\s+(?:into|in)\s+['""•]?(.*?)['""”]?$", RegexOptions.IgnoreCase);
        if (typeMatch.Success)
        {
            var val = typeMatch.Groups[1].Value;
            var target = typeMatch.Groups[2].Value;
            return new AutomationAction
            {
                Type = ActionType.Type,
                Description = $"Type '{val}' into {target}",
                Value = val,
                Target = new ActionTarget { Selector = target }
            };
        }

        // 3. Click pattern
        var clickMatch = Regex.Match(clause, @"^(?:click|click\s+on|press\s+button)\s+(?:on\s+)?['""“]?(.*?)['""”]?\s*(?:button)?$", RegexOptions.IgnoreCase);
        if (clickMatch.Success)
        {
            var target = clickMatch.Groups[1].Value;
            return new AutomationAction
            {
                Type = ActionType.Click,
                Description = $"Click on {target}",
                Target = new ActionTarget { Selector = target }
            };
        }

        // 4. Wait pattern
        var waitMatch = Regex.Match(clause, @"^(?:wait|sleep|delay)\s+(?:for\s+)?(\d+)\s*(?:seconds|sec|s|ms|milliseconds)?", RegexOptions.IgnoreCase);
        if (waitMatch.Success)
        {
            var number = int.Parse(waitMatch.Groups[1].Value);
            var unit = waitMatch.Groups[2].Value.ToLower();
            
            int ms = number;
            if (string.IsNullOrEmpty(unit) || unit.StartsWith("s"))
            {
                ms = number * 1000;
            }

            return new AutomationAction
            {
                Type = ActionType.Wait,
                Description = $"Wait for {ms}ms",
                Value = ms.ToString()
            };
        }

        // 5. Screenshot pattern
        var screenshotMatch = Regex.Match(clause, @"^(?:take\s+a\s+screenshot|screenshot|capture\s+screen)", RegexOptions.IgnoreCase);
        if (screenshotMatch.Success)
        {
            return new AutomationAction
            {
                Type = ActionType.Screenshot,
                Description = "Take a screenshot"
            };
        }

        // 6. Scroll pattern
        var scrollMatch = Regex.Match(clause, @"^(?:scroll)\s+(up|down|left|right)?", RegexOptions.IgnoreCase);
        if (scrollMatch.Success)
        {
            var dir = scrollMatch.Groups[1].Value;
            if (string.IsNullOrEmpty(dir)) dir = "down";
            return new AutomationAction
            {
                Type = ActionType.Scroll,
                Description = $"Scroll {dir}",
                Value = dir
            };
        }

        // Generic fallback action if the command starts with a known verb
        var words = clause.Split(' ');
        if (words.Length > 0)
        {
            var verb = words[0].ToLowerInvariant();
            if (verb == "press" || verb == "hit")
            {
                var key = clause.Substring(verb.Length).Trim().Trim('\'', '"', '‘', '’');
                return new AutomationAction
                {
                    Type = ActionType.KeyPress,
                    Description = $"Press {key}",
                    Value = key
                };
            }
        }

        return null;
    }

    // Helper DTOs for JSON deserialization
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
