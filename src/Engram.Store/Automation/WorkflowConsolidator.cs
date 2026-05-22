using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Automation;

public class TemplateStep
{
    public string StepId { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TargetPattern { get; set; } = string.Empty;
}

public class WorkflowTemplate
{
    public string TemplateId { get; set; } = Guid.NewGuid().ToString("n")[..8];
    public string GoalPattern { get; set; } = string.Empty;
    public List<TemplateStep> StepSequence { get; set; } = new();
    public double SuccessRate { get; set; } = 1.0;
    public int AverageStepCount { get; set; }
    public int SourceWorkflowCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastUsedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class WorkflowConsolidator
{
    private readonly ProceduralMemoryEngine _proceduralMemory;
    private readonly ExecutionTelemetryEngine _telemetry;
    private readonly ILogger? _logger;
    private readonly string _templateDir;

    public WorkflowConsolidator(
        ProceduralMemoryEngine proceduralMemory,
        ExecutionTelemetryEngine telemetry,
        string? customBaseDir = null,
        ILogger? logger = null)
    {
        _proceduralMemory = proceduralMemory ?? throw new ArgumentNullException(nameof(proceduralMemory));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger;

        var baseDir = customBaseDir ?? Path.Combine(Environment.CurrentDirectory, ".engram");
        _templateDir = Path.Combine(baseDir, "automation", "templates");
        Directory.CreateDirectory(_templateDir);
    }

    public List<WorkflowTemplate> IdentifyTemplates()
    {
        var list = new List<WorkflowTemplate>();
        try
        {
            if (!Directory.Exists(_templateDir)) return list;

            var files = Directory.GetFiles(_templateDir, "*.json");
            foreach (var file in files)
            {
                try
                {
                    var content = File.ReadAllText(file);
                    var template = JsonSerializer.Deserialize<WorkflowTemplate>(content);
                    if (template != null)
                    {
                        list.Add(template);
                    }
                }
                catch
                {
                    // Skip malformed templates
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load identified templates.");
        }
        return list;
    }

    public async Task<WorkflowTemplate?> ConsolidateWorkflowsAsync(IEnumerable<string> workflowIds, string goalPattern)
    {
        if (workflowIds == null) throw new ArgumentNullException(nameof(workflowIds));
        var ids = workflowIds.ToList();
        if (ids.Count == 0) return null;

        var template = new WorkflowTemplate
        {
            GoalPattern = goalPattern,
            SourceWorkflowCount = ids.Count,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUsedAt = DateTimeOffset.UtcNow
        };

        // Construct steps based on memories associated with target patterns
        var memories = await _proceduralMemory.GetMemoriesAsync();
        var relevantMemories = memories.Where(m => goalPattern.Contains(m.Target, StringComparison.OrdinalIgnoreCase) || 
                                                    m.Target.Contains(goalPattern, StringComparison.OrdinalIgnoreCase)).ToList();

        int stepIndex = 1;
        foreach (var m in relevantMemories.Take(5))
        {
            template.StepSequence.Add(new TemplateStep
            {
                StepId = $"step_{stepIndex++}",
                ActionType = m.Detail.Contains("Browser") ? "Browse" : "Execute",
                Description = m.Detail,
                TargetPattern = m.Target
            });
        }

        // Default fallback steps if no memory matches
        if (template.StepSequence.Count == 0)
        {
            template.StepSequence.Add(new TemplateStep
            {
                StepId = "step_1",
                ActionType = "Browse",
                Description = $"Navigate to target domain for {goalPattern}",
                TargetPattern = goalPattern
            });
            template.StepSequence.Add(new TemplateStep
            {
                StepId = "step_2",
                ActionType = "Extract",
                Description = "Extract information and values",
                TargetPattern = "data"
            });
        }

        template.AverageStepCount = template.StepSequence.Count;

        // Calculate success rate based on telemetry
        var summary = _telemetry.GetSummary();
        template.SuccessRate = summary.SuccessRate > 0 ? summary.SuccessRate : 0.90;

        // Persist template
        try
        {
            var filePath = Path.Combine(_templateDir, $"{template.TemplateId}.json");
            var content = JsonSerializer.Serialize(template, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, content);
            _logger?.LogInformation("Workflow consolidated successfully. Template saved: {TemplateId}", template.TemplateId);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to persist consolidated workflow template.");
        }

        return template;
    }
}
