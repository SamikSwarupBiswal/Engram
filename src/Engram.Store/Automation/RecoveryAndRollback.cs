using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Engram.Store.Automation;

/// <summary>
/// A recovery policy that retries a failed step after a delay, up to a maximum number of retries.
/// </summary>
public class RetryWithDelayRecovery : IStepRecovery
{
    private readonly int _maxRetries;
    private readonly TimeSpan _delay;
    private int _retryCount;

    public RetryWithDelayRecovery(int maxRetries = 3, TimeSpan? delay = null)
    {
        _maxRetries = maxRetries;
        _delay = delay ?? TimeSpan.FromSeconds(1);
    }

    public async Task<bool> RecoverAsync(ExecutionContext context, Exception exception, CancellationToken ct)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));

        if (_retryCount >= _maxRetries)
        {
            return false;
        }

        _retryCount++;
        await Task.Delay(_delay, ct);
        return true;
    }
}

/// <summary>
/// A recovery policy that runs alternative recovery actions (e.g. refreshing the page or clicking a cancel button) before retrying.
/// </summary>
public class AlternativeStepRecovery : IStepRecovery
{
    private readonly List<AutomationAction> _alternativeActions;

    public AlternativeStepRecovery(AutomationAction alternativeAction)
    {
        if (alternativeAction == null) throw new ArgumentNullException(nameof(alternativeAction));
        _alternativeActions = new List<AutomationAction> { alternativeAction };
    }

    public AlternativeStepRecovery(IEnumerable<AutomationAction> alternativeActions)
    {
        if (alternativeActions == null) throw new ArgumentNullException(nameof(alternativeActions));
        _alternativeActions = new List<AutomationAction>(alternativeActions);
    }

    public async Task<bool> RecoverAsync(ExecutionContext context, Exception exception, CancellationToken ct)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));

        var executor = context.GetVariable<ActionExecutor>("ActionExecutor");
        if (executor == null)
        {
            throw new InvalidOperationException("ActionExecutor is not registered in the ExecutionContext variables under 'ActionExecutor'.");
        }

        // Run each recovery action
        foreach (var action in _alternativeActions)
        {
            // Auto-approve recovery actions so they run without prompting
            action.Permission = ActionPermission.AutoApproved;
            await executor.ExecuteAsync(action, ct);
        }

        return true;
    }
}

/// <summary>
/// A rollback handler that navigates the browser back to a specific url.
/// </summary>
public class NavigateBackRollback : IStepRollback
{
    public string RollbackUrl { get; }

    public NavigateBackRollback(string rollbackUrl)
    {
        RollbackUrl = rollbackUrl ?? throw new ArgumentNullException(nameof(rollbackUrl));
    }

    public async Task RollbackAsync(ExecutionContext context, CancellationToken ct)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));

        var browser = context.GetVariable<BrowserAgentRuntime>("BrowserAgent");
        if (browser != null)
        {
            await browser.NavigateAsync(RollbackUrl, ct);
        }
    }
}

/// <summary>
/// A rollback handler that attempts to close the foreground window (e.g., via Alt+F4).
/// </summary>
public class CloseWindowRollback : IStepRollback
{
    public async Task RollbackAsync(ExecutionContext context, CancellationToken ct)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));

        var op = context.GetVariable<IDesktopOperator>("DesktopOperator");
        if (op != null)
        {
            await op.KeyPressAsync("Alt+F4", ct);
        }
    }
}
