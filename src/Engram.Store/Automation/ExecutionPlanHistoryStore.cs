using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Engram.Store.Automation;

/// <summary>
/// Persists execution results, step status, and context variables under .engram/automation/runs/
/// </summary>
public class ExecutionPlanHistoryStore
{
    private readonly string _storeDir;

    public ExecutionPlanHistoryStore(string? baseDir = null)
    {
        var baseDirectory = baseDir ?? Directory.GetCurrentDirectory();
        _storeDir = Path.Combine(baseDirectory, ".engram", "automation", "runs");
    }

    public string StoreDirectory => _storeDir;

    public async Task SaveRunAsync(ExecutionPlan plan, ExecutionContext context)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));
        if (context == null) throw new ArgumentNullException(nameof(context));

        Directory.CreateDirectory(_storeDir);

        var filePath = Path.Combine(_storeDir, $"{plan.PlanId}.json");
        
        var runData = new RunHistoryData
        {
            PlanId = plan.PlanId,
            Goal = plan.Goal,
            SerializedContext = context.SerializeState(),
            Steps = new List<StepHistoryData>()
        };

        foreach (var kvp in plan.Steps)
        {
            var step = kvp.Value;
            runData.Steps.Add(new StepHistoryData
            {
                Id = step.Id,
                ActionId = step.Action.ActionId,
                ActionType = step.Action.Type.ToString(),
                Description = step.Action.Description,
                Status = step.Status.ToString(),
                StartedAt = step.StartedAt,
                CompletedAt = step.CompletedAt,
                Error = step.Error,
                Result = step.Action.Result
            });
        }

        var json = JsonSerializer.Serialize(runData, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(filePath, json);
    }

    public async Task<RunHistoryData?> LoadRunAsync(string planId)
    {
        var filePath = Path.Combine(_storeDir, $"{planId}.json");
        if (!File.Exists(filePath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(filePath);
        return JsonSerializer.Deserialize<RunHistoryData>(json);
    }
}

public class RunHistoryData
{
    public string PlanId { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public string SerializedContext { get; set; } = string.Empty;
    public List<StepHistoryData> Steps { get; set; } = new();
}

public class StepHistoryData
{
    public string Id { get; set; } = string.Empty;
    public string ActionId { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Error { get; set; }
    public string? Result { get; set; }
}
