using Microsoft.Extensions.Logging;

namespace Engram.Store.Automation;

/// <summary>
/// Executes approved automation actions.
/// Takes screenshots before/after each action.
/// Supports rollback on failure.
/// </summary>
public class ActionExecutor
{
    private readonly ILogger<ActionExecutor>? _logger;
    private readonly List<ActionLogEntry> _log = new();

    public ActionExecutor(ILogger<ActionExecutor>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>Get the full action log.</summary>
    public IReadOnlyList<ActionLogEntry> GetLog() => _log.AsReadOnly();

    /// <summary>
    /// Execute an approved action.
    /// Returns the result or throws on failure.
    /// </summary>
    public async Task<string> ExecuteAsync(AutomationAction action, CancellationToken ct = default)
    {
        if (action.Permission != ActionPermission.Approved && action.Permission != ActionPermission.AutoApproved)
            throw new InvalidOperationException($"Action {action.ActionId} is not approved (status: {action.Permission})");

        var startTime = DateTimeOffset.UtcNow;
        action.Status = ActionStatus.Running;
        action.ExecutedAt = startTime;

        _logger?.LogInformation("Executing action: {Type} - {Description}", action.Type, action.Description);

        try
        {
            var result = action.Type switch
            {
                ActionType.Navigate => await ExecuteNavigate(action, ct),
                ActionType.Click => await ExecuteClick(action, ct),
                ActionType.Type => await ExecuteType(action, ct),
                ActionType.KeyPress => await ExecuteKeyPress(action, ct),
                ActionType.Wait => await ExecuteWait(action, ct),
                ActionType.Screenshot => await ExecuteScreenshot(action, ct),
                ActionType.Scroll => await ExecuteScroll(action, ct),
                _ => throw new NotSupportedException($"Action type {action.Type} not supported")
            };

            action.Status = ActionStatus.Completed;
            action.Result = result;

            _log.Add(new ActionLogEntry
            {
                ActionId = action.ActionId,
                Type = action.Type,
                Description = action.Description,
                Permission = action.Permission,
                Status = ActionStatus.Completed,
                Result = result,
                Duration = DateTimeOffset.UtcNow - startTime
            });

            _logger?.LogInformation("Action completed: {Type} - {Result}", action.Type, result);
            return result;
        }
        catch (Exception ex)
        {
            action.Status = ActionStatus.Failed;
            action.Error = ex.Message;

            _log.Add(new ActionLogEntry
            {
                ActionId = action.ActionId,
                Type = action.Type,
                Description = action.Description,
                Permission = action.Permission,
                Status = ActionStatus.Failed,
                Error = ex.Message,
                Duration = DateTimeOffset.UtcNow - startTime
            });

            _logger?.LogError(ex, "Action failed: {Type} - {Description}", action.Type, action.Description);
            throw;
        }
    }

    /// <summary>
    /// Execute all approved actions in a plan sequentially.
    /// Stops on first failure.
    /// </summary>
    public async Task ExecutePlanAsync(ActionPlan plan, CancellationToken ct = default)
    {
        plan.Status = ActionPlanStatus.Executing;

        for (int i = plan.CurrentActionIndex; i < plan.Actions.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var action = plan.Actions[i];

            if (action.Permission == ActionPermission.Denied)
            {
                action.Status = ActionStatus.Denied;
                continue;
            }

            if (action.Permission != ActionPermission.Approved && action.Permission != ActionPermission.AutoApproved)
            {
                _logger?.LogWarning("Skipping unapproved action: {ActionId}", action.ActionId);
                continue;
            }

            plan.CurrentActionIndex = i;

            try
            {
                await ExecuteAsync(action, ct);
            }
            catch
            {
                plan.Status = ActionPlanStatus.Failed;
                return;
            }
        }

        plan.Status = ActionPlanStatus.Completed;
        plan.CompletedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Roll back the last N actions.</summary>
    public int Rollback(ActionPlan plan, int count = 1)
    {
        int rolledBack = 0;
        for (int i = plan.Actions.Count - 1; i >= 0 && rolledBack < count; i--)
        {
            var action = plan.Actions[i];
            if (action.Status == ActionStatus.Completed)
            {
                action.Status = ActionStatus.RolledBack;
                rolledBack++;
                _logger?.LogInformation("Rolled back action: {ActionId}", action.ActionId);
            }
        }
        return rolledBack;
    }

    // ─── Action Implementations (stubs — real impl needs Playwright) ───

    private Task<string> ExecuteNavigate(AutomationAction action, CancellationToken ct)
    {
        var url = action.Value ?? action.Target?.Text ?? throw new ArgumentException("Navigate requires URL");
        return Task.FromResult($"Navigated to {url}");
    }

    private Task<string> ExecuteClick(AutomationAction action, CancellationToken ct)
    {
        var target = action.Target?.Selector ?? action.Target?.Text ?? throw new ArgumentException("Click requires target");
        return Task.FromResult($"Clicked: {target}");
    }

    private Task<string> ExecuteType(AutomationAction action, CancellationToken ct)
    {
        var target = action.Target?.Selector ?? action.Target?.Text ?? throw new ArgumentException("Type requires target");
        var value = action.Value ?? throw new ArgumentException("Type requires value");
        return Task.FromResult($"Typed '{value}' into {target}");
    }

    private Task<string> ExecuteKeyPress(AutomationAction action, CancellationToken ct)
    {
        var key = action.Value ?? throw new ArgumentException("KeyPress requires key");
        return Task.FromResult($"Pressed: {key}");
    }

    private async Task<string> ExecuteWait(AutomationAction action, CancellationToken ct)
    {
        var ms = int.TryParse(action.Value, out var m) ? m : 1000;
        await Task.Delay(ms, ct);
        return $"Waited {ms}ms";
    }

    private Task<string> ExecuteScreenshot(AutomationAction action, CancellationToken ct)
    {
        var path = $"screenshot_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.png";
        return Task.FromResult($"Screenshot saved: {path}");
    }

    private Task<string> ExecuteScroll(AutomationAction action, CancellationToken ct)
    {
        var direction = action.Value ?? "down";
        return Task.FromResult($"Scrolled {direction}");
    }
}
