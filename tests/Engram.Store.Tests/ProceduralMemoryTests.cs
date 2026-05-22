using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Engram.Store.Automation;
using Xunit;

namespace Engram.Store.Tests;

public class ProceduralMemoryTests : IDisposable
{
    private readonly string _tempDir;

    public ProceduralMemoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"engram_procmem_tests_{Guid.NewGuid():n}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch { }
        }
    }

    [Fact]
    public async Task InitializeAsync_WhenFileDoesNotExist_StartsEmpty()
    {
        // Arrange
        var engine = new ProceduralMemoryEngine(_tempDir);

        // Act
        await engine.InitializeAsync();
        var memories = await engine.GetMemoriesAsync();

        // Assert
        Assert.Empty(memories);
    }

    [Fact]
    public async Task AddMemoryAsync_AddsNewMemoryAndSavesToFile()
    {
        // Arrange
        var engine = new ProceduralMemoryEngine(_tempDir);
        await engine.InitializeAsync();

        // Act
        await engine.AddMemoryAsync("habit", "amazon.com", "Always checkout using 1-click", isSuccessful: true);

        // Assert
        var memories = await engine.GetMemoriesAsync();
        Assert.Single(memories);
        var memory = memories[0];
        Assert.Equal("habit", memory.Type);
        Assert.Equal("amazon.com", memory.Target);
        Assert.Equal("Always checkout using 1-click", memory.Detail);
        Assert.Equal(1, memory.UseCount);
        Assert.Equal(1, memory.SuccessCount);

        // Verify file is saved
        var filePath = Path.Combine(_tempDir, ".engram", "automation", "procedural_memory.json");
        Assert.True(File.Exists(filePath));
        var content = File.ReadAllText(filePath);
        Assert.Contains("amazon.com", content);
        Assert.Contains("Always checkout using 1-click", content);
    }

    [Fact]
    public async Task AddMemoryAsync_AggregatesDuplicates()
    {
        // Arrange
        var engine = new ProceduralMemoryEngine(_tempDir);
        await engine.InitializeAsync();

        // Act
        await engine.AddMemoryAsync("habit", "amazon.com", "detail", isSuccessful: true);
        await engine.AddMemoryAsync("habit", "amazon.com", "detail", isSuccessful: false);

        // Assert
        var memories = await engine.GetMemoriesAsync();
        Assert.Single(memories);
        var memory = memories[0];
        Assert.Equal(2, memory.UseCount);
        Assert.Equal(1, memory.SuccessCount);
    }

    [Fact]
    public async Task GetMemoriesForTargetAsync_FiltersByTargetCaseInsensitively()
    {
        // Arrange
        var engine = new ProceduralMemoryEngine(_tempDir);
        await engine.InitializeAsync();
        await engine.AddMemoryAsync("habit", "amazon.com", "detail1");
        await engine.AddMemoryAsync("habit", "AMAZON.COM", "detail2");
        await engine.AddMemoryAsync("habit", "google.com", "detail3");

        // Act
        var amazonMemories = await engine.GetMemoriesForTargetAsync("amazon.com");
        var googleMemories = await engine.GetMemoriesForTargetAsync("google.com");

        // Assert
        Assert.Equal(2, amazonMemories.Count);
        Assert.Single(googleMemories);
    }

    [Fact]
    public async Task LearnFromExecutionAsync_LearnsStepOutcomesAndGoalSuccess()
    {
        // Arrange
        var engine = new ProceduralMemoryEngine(_tempDir);
        await engine.InitializeAsync();

        var plan = new ExecutionPlan
        {
            Goal = "Search for laptops on amazon.com"
        };
        
        var step1 = new ExecutionStep
        {
            Id = "1",
            Action = new AutomationAction { Type = ActionType.Navigate, Description = "Go to amazon.com", Target = new ActionTarget { Text = "amazon.com" } },
            Status = StepStatus.Completed
        };
        
        var step2 = new ExecutionStep
        {
            Id = "2",
            Action = new AutomationAction { Type = ActionType.Click, Description = "Click search bar", Target = new ActionTarget { Selector = "search" } },
            Status = StepStatus.Failed,
            Error = "Selector not found"
        };

        plan.Steps[step1.Id] = step1;
        plan.Steps[step2.Id] = step2;

        // Act
        await engine.LearnFromExecutionAsync(plan, success: false);

        // Assert
        var memories = await engine.GetMemoriesAsync();
        // Should have learned: 1 sequence (step 1 successful), 1 quirk (step 2 failed), 1 goal_outcome (goal failed)
        Assert.Equal(3, memories.Count);

        var sequenceMem = memories.FirstOrDefault(m => m.Type == "sequence");
        Assert.NotNull(sequenceMem);
        Assert.Equal("amazon.com", sequenceMem.Target);
        Assert.Contains("Successful Navigate action", sequenceMem.Detail);

        var quirkMem = memories.FirstOrDefault(m => m.Type == "quirk");
        Assert.NotNull(quirkMem);
        Assert.Equal("amazon.com", quirkMem.Target);
        Assert.Contains("Failed action", quirkMem.Detail);
        Assert.Contains("Selector not found", quirkMem.Detail);

        var goalMem = memories.FirstOrDefault(m => m.Type == "goal_outcome");
        Assert.NotNull(goalMem);
        Assert.Equal("amazon.com", goalMem.Target);
        Assert.Equal(0, goalMem.SuccessCount);
    }
}
