using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Engram.Store.Automation;
using Xunit;

namespace Engram.Store.Tests;

public class ToolAbstractionAndSandboxTests : IDisposable
{
    private readonly string _tempDir;

    public ToolAbstractionAndSandboxTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"engram_sandbox_tests_{Guid.NewGuid():n}");
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

    // ==================================================================
    // SANDBOX MANAGER TESTS
    // ==================================================================

    [Fact]
    public void SandboxManager_Defaults_AllowTempAndCurrentDir()
    {
        // Arrange
        var sandbox = new SandboxManager();

        // Assert
        Assert.True(sandbox.ValidatePathSafety(Path.GetTempPath()));
        Assert.True(sandbox.ValidatePathSafety(Environment.CurrentDirectory));
    }

    [Fact]
    public void SandboxManager_ValidatePathSafety_BlocksDisallowedPaths()
    {
        // Arrange
        var sandbox = new SandboxManager();
        var disallowedPath = "C:\\Windows\\System32\\cmd.exe";

        // Assert
        Assert.False(sandbox.ValidatePathSafety(disallowedPath));
    }

    [Fact]
    public void SandboxManager_AddAllowedDirectory_AllowsSubdirs()
    {
        // Arrange
        var sandbox = new SandboxManager();
        var customDir = Path.Combine(Path.GetDirectoryName(Environment.CurrentDirectory) ?? "C:\\", "engram_custom_test_boundary");
        
        // Act
        sandbox.AddAllowedDirectory(customDir);

        // Assert
        var fileInCustom = Path.Combine(customDir, "sub", "test.txt");
        Assert.True(sandbox.ValidatePathSafety(customDir));
        Assert.True(sandbox.ValidatePathSafety(fileInCustom));

        // Check exact name prefix boundary (e.g. C:\temp vs C:\temp2)
        var adjacentDir = customDir + "2";
        Assert.False(sandbox.ValidatePathSafety(adjacentDir));
    }

    [Fact]
    public void SandboxManager_ValidateCommandSafety_BlocksBlacklistedCommands()
    {
        // Arrange
        var sandbox = new SandboxManager();

        // Assert
        Assert.False(sandbox.ValidateCommandSafety("sudo apt-get install"));
        Assert.False(sandbox.ValidateCommandSafety("rm -rf /"));
        Assert.False(sandbox.ValidateCommandSafety("del /f file.txt"));
        Assert.False(sandbox.ValidateCommandSafety("shutdown -h now"));
        Assert.True(sandbox.ValidateCommandSafety("git status"));
        Assert.True(sandbox.ValidateCommandSafety("dotnet build"));
    }

    [Fact]
    public async Task SandboxManager_VerifyPlanAsync_BlocksUnsafePathsOrScripts()
    {
        // Arrange
        var sandbox = new SandboxManager();
        sandbox.AddAllowedDirectory(_tempDir);

        var plan = new ExecutionPlan { Goal = "Test" };
        
        var step1 = new ExecutionStep
        {
            Id = "1",
            Action = new AutomationAction
            {
                Type = ActionType.Upload,
                Value = "C:\\SystemSecretData.txt",
                Description = "Disallowed upload path"
            }
        };
        plan.Steps[step1.Id] = step1;

        // Act
        var result = await sandbox.VerifyPlanAsync(plan);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task SandboxManager_VerifyPlanAsync_BlocksScriptSelectors()
    {
        // Arrange
        var sandbox = new SandboxManager();
        var plan = new ExecutionPlan { Goal = "Test" };
        
        var step1 = new ExecutionStep
        {
            Id = "1",
            Action = new AutomationAction
            {
                Type = ActionType.Click,
                Target = new ActionTarget { Selector = "//script[contains(text(), 'malicious')]" },
                Description = "Malicious script execution"
            }
        };
        plan.Steps[step1.Id] = step1;

        // Act
        var result = await sandbox.VerifyPlanAsync(plan);

        // Assert
        Assert.False(result);
    }

    // ==================================================================
    // TOOL ABSTRACTION LAYER TESTS
    // ==================================================================

    [Fact]
    public async Task ToolAbstractionLayer_SearchWeb_InSimulation_ReturnsMockOutput()
    {
        // Arrange
        using var browser = new BrowserAgentRuntime { IsSimulationMode = true };
        var desktop = new MockDesktopOperator { IsSimulationMode = true };
        var tool = new ToolAbstractionLayer(browser, desktop);

        // Act
        var result = await tool.SearchWebAsync("best laptop 2026", CancellationToken.None);

        // Assert
        Assert.Contains("[Simulation] Search results", result);
        Assert.Contains("best laptop 2026", result);
    }

    [Fact]
    public async Task ToolAbstractionLayer_CreateDocument_InRealMode_WritesFile()
    {
        // Arrange
        using var browser = new BrowserAgentRuntime { IsSimulationMode = true };
        var desktop = new MockDesktopOperator { IsSimulationMode = false };
        var tool = new ToolAbstractionLayer(browser, desktop);
        var targetFile = Path.Combine(_tempDir, "docs", "new_report.txt");

        // Act
        await tool.CreateDocumentAsync(targetFile, "Hello World Content", CancellationToken.None);

        // Assert
        Assert.True(File.Exists(targetFile));
        Assert.Equal("Hello World Content", await File.ReadAllTextAsync(targetFile));
    }

    [Fact]
    public async Task ToolAbstractionLayer_OpenApplication_InRealMode_TypesKeys()
    {
        // Arrange
        using var browser = new BrowserAgentRuntime { IsSimulationMode = true };
        var desktop = new MockDesktopOperator { IsSimulationMode = false };
        var tool = new ToolAbstractionLayer(browser, desktop);

        // Act
        await tool.OpenApplicationAsync("notepad.exe", CancellationToken.None);

        // Assert
        Assert.Equal("notepad.exe", desktop.TypedText);
        Assert.Equal("Enter", desktop.PressedKey);
    }

    private class MockDesktopOperator : IDesktopOperator
    {
        public bool IsSimulationMode { get; set; }
        public string ClickedAt { get; set; } = string.Empty;
        public string TypedText { get; set; } = string.Empty;
        public string PressedKey { get; set; } = string.Empty;
        public string ActiveProcess { get; set; } = "explorer";
        public string ActiveTitle { get; set; } = "My Folder";

        public Task ClickAsync(int x, int y, CancellationToken ct = default)
        {
            ClickedAt = $"{x},{y}";
            return Task.CompletedTask;
        }

        public Task TypeAsync(string text, CancellationToken ct = default)
        {
            TypedText = text;
            return Task.CompletedTask;
        }

        public Task KeyPressAsync(string key, CancellationToken ct = default)
        {
            PressedKey = key;
            return Task.CompletedTask;
        }

        public Task<(string ProcessName, string WindowTitle)> GetActiveWindowAsync(CancellationToken ct = default)
        {
            return Task.FromResult((ActiveProcess, ActiveTitle));
        }
    }
}
