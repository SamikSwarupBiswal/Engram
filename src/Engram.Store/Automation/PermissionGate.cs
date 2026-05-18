using Microsoft.Extensions.Logging;

namespace Engram.Store.Automation;

/// <summary>
/// Gates all automation actions behind user approval.
/// Safe actions can be auto-approved. Dangerous actions require explicit approval.
/// </summary>
public class PermissionGate
{
    private readonly ILogger<PermissionGate>? _logger;

    /// <summary>Actions that are auto-approved (safe, read-only).</summary>
    private static readonly HashSet<ActionType> SafeActions = new()
    {
        ActionType.Screenshot,
        ActionType.Wait,
    };

    /// <summary>Actions that are always blocked (dangerous).</summary>
    private static readonly HashSet<ActionType> BlockedActions = new()
    {
        // Currently no permanently blocked actions — all require approval
    };

    public PermissionGate(ILogger<PermissionGate>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Check permission for an action.
    /// Returns the permission level (auto-approved, pending, or denied).
    /// </summary>
    public ActionPermission CheckPermission(AutomationAction action)
    {
        if (BlockedActions.Contains(action.Type))
        {
            _logger?.LogWarning("Action blocked: {Type} - {Description}", action.Type, action.Description);
            return ActionPermission.Denied;
        }

        if (SafeActions.Contains(action.Type))
        {
            _logger?.LogDebug("Action auto-approved: {Type}", action.Type);
            return ActionPermission.AutoApproved;
        }

        _logger?.LogInformation("Action requires approval: {Type} - {Description}", action.Type, action.Description);
        return ActionPermission.Pending;
    }

    /// <summary>
    /// Approve a specific action.
    /// </summary>
    public bool Approve(AutomationAction action)
    {
        if (action.Permission != ActionPermission.Pending)
            return false;

        action.Permission = ActionPermission.Approved;
        _logger?.LogInformation("Action approved: {ActionId} - {Description}", action.ActionId, action.Description);
        return true;
    }

    /// <summary>
    /// Deny a specific action.
    /// </summary>
    public bool Deny(AutomationAction action)
    {
        if (action.Permission != ActionPermission.Pending)
            return false;

        action.Permission = ActionPermission.Denied;
        action.Status = ActionStatus.Denied;
        _logger?.LogInformation("Action denied: {ActionId} - {Description}", action.ActionId, action.Description);
        return true;
    }

    /// <summary>
    /// Approve all pending actions in a plan.
    /// </summary>
    public int ApproveAll(ActionPlan plan)
    {
        int count = 0;
        foreach (var action in plan.Actions)
        {
            if (action.Permission == ActionPermission.Pending)
            {
                action.Permission = ActionPermission.Approved;
                count++;
            }
        }
        _logger?.LogInformation("Batch approved {Count} actions in plan {PlanId}", count, plan.PlanId);
        return count;
    }

    /// <summary>
    /// Deny all pending actions in a plan.
    /// </summary>
    public int DenyAll(ActionPlan plan)
    {
        int count = 0;
        foreach (var action in plan.Actions)
        {
            if (action.Permission == ActionPermission.Pending)
            {
                action.Permission = ActionPermission.Denied;
                action.Status = ActionStatus.Denied;
                count++;
            }
        }
        return count;
    }
}
