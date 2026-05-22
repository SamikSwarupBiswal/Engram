using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Engram.Store.Automation;
using Xunit;

namespace Engram.Store.Tests;

public class WorkflowConsolidatorTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();
    private readonly ProceduralMemoryEngine _proceduralMemory;
    private readonly ExecutionTelemetryEngine _telemetry;

    public WorkflowConsolidatorTests()
    {
        _proceduralMemory = new ProceduralMemoryEngine(_workspace.Root);
        _telemetry = new ExecutionTelemetryEngine(_workspace.Root);
    }

    public void Dispose()
    {
        _workspace.Dispose();
    }

    [Fact]
    public async Task ConsolidateWorkflowsAsync_WithNoMemories_CreatesDefaultStepsAndPersists()
    {
        var consolidator = new WorkflowConsolidator(_proceduralMemory, _telemetry, _workspace.Root);
        
        var template = await consolidator.ConsolidateWorkflowsAsync(new[] { "wf1", "wf2" }, "search items");

        Assert.NotNull(template);
        Assert.Equal("search items", template!.GoalPattern);
        Assert.Equal(2, template.SourceWorkflowCount);
        Assert.Equal(2, template.StepSequence.Count);
        Assert.Equal("Browse", template.StepSequence[0].ActionType);
        Assert.Equal("Extract", template.StepSequence[1].ActionType);

        // Verify it was persisted
        var files = Directory.GetFiles(Path.Combine(_workspace.Root, "automation", "templates"), "*.json");
        Assert.Single(files);
    }

    [Fact]
    public async Task ConsolidateWorkflowsAsync_WithMatchingMemories_UsesMemoriesForSteps()
    {
        var consolidator = new WorkflowConsolidator(_proceduralMemory, _telemetry, _workspace.Root);

        // Save a procedural memory matching the target pattern
        await _proceduralMemory.AddMemoryAsync("sequence", "Git Commit", "Run git commit command");

        var template = await consolidator.ConsolidateWorkflowsAsync(new[] { "wf1" }, "Git Commit");

        Assert.NotNull(template);
        Assert.Single(template!.StepSequence);
        Assert.Equal("Execute", template.StepSequence[0].ActionType);
        Assert.Contains("git commit", template.StepSequence[0].Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IdentifyTemplates_LoadsAllPersistedTemplates()
    {
        var consolidator = new WorkflowConsolidator(_proceduralMemory, _telemetry, _workspace.Root);

        // Create 2 templates
        await consolidator.ConsolidateWorkflowsAsync(new[] { "wf1" }, "Goal One");
        await consolidator.ConsolidateWorkflowsAsync(new[] { "wf2" }, "Goal Two");

        var templates = consolidator.IdentifyTemplates();

        Assert.Equal(2, templates.Count);
        Assert.Contains(templates, t => t.GoalPattern == "Goal One");
        Assert.Contains(templates, t => t.GoalPattern == "Goal Two");
    }
}
