using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Engram.Store.Automation;
using Engram.Store.Events;
using Xunit;

namespace Engram.Store.Tests;

public class AttentionAndResilienceTests
{
    [Fact]
    public void OperationalAttentionOrchestrator_DefaultFocus_ContainsBrowserAndShells()
    {
        // Arrange
        var orchestrator = new OperationalAttentionOrchestrator();

        // Assert
        Assert.True(orchestrator.IsRelevant("chrome", ""));
        Assert.True(orchestrator.IsRelevant("msedge", ""));
        Assert.True(orchestrator.IsRelevant("explorer", ""));
        Assert.True(orchestrator.IsRelevant("cmd", ""));
        Assert.True(orchestrator.IsRelevant("powershell", ""));
        Assert.False(orchestrator.IsRelevant("notepad", ""));
    }

    [Fact]
    public void OperationalAttentionOrchestrator_SetFocus_UpdatesRelevance()
    {
        // Arrange
        var orchestrator = new OperationalAttentionOrchestrator();

        // Act
        orchestrator.SetFocus(new[] { "notepad", "excel" }, new[] { "pricing", "invoices" });

        // Assert
        Assert.True(orchestrator.IsRelevant("notepad", ""));
        Assert.True(orchestrator.IsRelevant("excel", ""));
        Assert.False(orchestrator.IsRelevant("chrome", "")); // Old default removed

        // Keyword title checks
        Assert.True(orchestrator.IsRelevant("chrome", "Q3 pricing details"));
        Assert.True(orchestrator.IsRelevant("anyprocess", "invoices list"));
        Assert.False(orchestrator.IsRelevant("anyprocess", "random title"));
    }

    [Fact]
    public void OperationalAttentionOrchestrator_FilterContext_PrunesNoise()
    {
        // Arrange
        var orchestrator = new OperationalAttentionOrchestrator();
        orchestrator.SetFocus(new[] { "chrome" }, new[] { "project x" });

        var contextLines = new[]
        {
            "User opened chrome browser tab",
            "Slack received message from Bob",
            "Window title for process chrome is: project x documentation",
            "System error: Disk space low",
            "This is some background noise line"
        };

        // Act
        var filtered = orchestrator.FilterContext(contextLines);

        // Assert
        Assert.Contains("User opened chrome browser tab", filtered);
        Assert.Contains("Window title for process chrome is: project x documentation", filtered);
        Assert.Contains("System error: Disk space low", filtered); // Kept due to "error"
        Assert.DoesNotContain("Slack received message from Bob", filtered);
        Assert.DoesNotContain("This is some background noise line", filtered);
    }

    [Fact]
    public void EnvironmentalResilienceEngine_TriggerSleepWake_PublishesCorrectEvents()
    {
        // Arrange
        var eventBus = new InMemoryEventBus();
        var resilience = new EnvironmentalResilienceEngine(eventBus);
        var receivedEvents = new List<EventEnvelope>();
        using var sub = eventBus.SubscribeAll(env => receivedEvents.Add(env));

        // Act
        resilience.TriggerSleepTransition();
        resilience.TriggerWakeTransition();

        // Assert
        Assert.Equal(2, receivedEvents.Count);
        Assert.Equal("system.sleep", receivedEvents[0].EventType);
        Assert.Equal("system.wake", receivedEvents[1].EventType);
    }

    [Fact]
    public async Task EnvironmentalResilienceEngine_HandleDisturbances_DismissesExpectedPopups()
    {
        // Arrange
        var eventBus = new InMemoryEventBus();
        var resilience = new EnvironmentalResilienceEngine(eventBus);
        var mockDesktop = new MockDesktopOperator
        {
            ActiveProcess = "explorer.exe",
            ActiveTitle = "Fatal error occurred"
        };
        
        using var browser = new BrowserAgentRuntime();
        EventEnvelope? receivedEnvelope = null;
        using var sub = eventBus.Subscribe("automation.resilience.popup_dismissed", env => receivedEnvelope = env);

        // Act
        await resilience.HandleDisturbancesAsync(browser, mockDesktop, CancellationToken.None);

        // Assert
        Assert.Equal("Escape", mockDesktop.PressedKey);
        Assert.NotNull(receivedEnvelope);
        Assert.Equal("automation.resilience.popup_dismissed", receivedEnvelope.EventType);
        dynamic payload = receivedEnvelope.Payload;
        Assert.Equal("Fatal error occurred", (string)payload.WindowTitle);
        Assert.Equal("explorer.exe", (string)payload.Process);
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
