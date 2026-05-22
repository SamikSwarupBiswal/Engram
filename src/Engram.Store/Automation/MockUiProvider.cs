using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Engram.Store.Automation;

/// <summary>
/// A mock UI embodiment provider for testing, replay, and dry-runs.
/// </summary>
public class MockUiProvider : IUiEmbodimentProvider
{
    private bool _isSimulationMode = true;

    public bool IsSimulationMode
    {
        get => _isSimulationMode;
        set => _isSimulationMode = value;
    }

    public List<AutomationAction> ExecutedActions { get; } = new();

    public string MockUrl { get; set; } = "https://example.com";
    public string MockProcessName { get; set; } = "explorer";
    public string MockWindowTitle { get; set; } = "File Explorer";
    public Func<AutomationAction, Task<string>>? ActionHandler { get; set; }

    public async Task<string> ExecuteActionAsync(AutomationAction action, CancellationToken ct = default)
    {
        ExecutedActions.Add(action);

        if (ActionHandler != null)
        {
            return await ActionHandler(action);
        }

        action.Status = ActionStatus.Completed;
        action.ExecutedAt = DateTimeOffset.UtcNow;

        var result = action.Type switch
        {
            ActionType.Navigate => $"Navigated to {action.Value ?? action.Target?.Text}",
            ActionType.Click => $"Clicked: {action.Target?.Selector ?? action.Target?.Text ?? $"({action.Target?.X}, {action.Target?.Y})"}",
            ActionType.Type => $"Typed '{action.Value}' into {action.Target?.Selector ?? action.Target?.Text}",
            ActionType.KeyPress => $"Pressed key '{action.Value}'",
            ActionType.Wait => $"Waited {action.Value ?? "1000"}ms",
            ActionType.Screenshot => "Screenshot saved",
            ActionType.Scroll => $"Scrolled {action.Value ?? "down"}",
            _ => $"Executed {action.Type}"
        };

        action.Result = result;
        return result;
    }

    public Task<(string ProcessName, string WindowTitle)> GetActiveWindowAsync(CancellationToken ct = default)
    {
        return Task.FromResult((MockProcessName, MockWindowTitle));
    }

    public Task<string> GetUrlAsync(CancellationToken ct = default)
    {
        return Task.FromResult(MockUrl);
    }
}
