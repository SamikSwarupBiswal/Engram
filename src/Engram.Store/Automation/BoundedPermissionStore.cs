using System;
using System.Collections.Concurrent;

namespace Engram.Store.Automation;

public enum PermissionCategory
{
    Read,          // Screenshot, Wait, GetUrl
    Navigation,    // Navigate
    Interaction,   // Click, Type, KeyPress, Scroll, Select
    FileTransfer,  // Upload, Download
    Destructive    // Deletes, drops, purges
}

/// <summary>
/// Manages user-approved workflow permissions, bounded strictly by action category
/// to prevent category leakage (e.g. read authorization leaking to destructive actions).
/// </summary>
public class BoundedPermissionStore
{
    private readonly ConcurrentDictionary<(string WorkflowId, PermissionCategory Category), ActionPermission> _store = new();

    /// <summary>
    /// Categorizes an automation action based on type and keywords.
    /// </summary>
    public static PermissionCategory GetCategory(AutomationAction action)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));

        if (ContainsDestructiveKeyword(action.Description) || ContainsDestructiveKeyword(action.Value))
        {
            return PermissionCategory.Destructive;
        }

        return action.Type switch
        {
            ActionType.Wait or ActionType.Screenshot => PermissionCategory.Read,
            ActionType.Navigate => PermissionCategory.Navigation,
            ActionType.Upload or ActionType.Download => PermissionCategory.FileTransfer,
            _ => PermissionCategory.Interaction
        };
    }

    /// <summary>
    /// Checks stored permissions for the workflow and action category.
    /// </summary>
    public ActionPermission CheckPermission(string workflowId, AutomationAction action)
    {
        if (string.IsNullOrEmpty(workflowId)) return ActionPermission.Pending;

        var category = GetCategory(action);
        if (_store.TryGetValue((workflowId, category), out var permission))
        {
            return permission;
        }

        return ActionPermission.Pending;
    }

    /// <summary>
    /// Stores the permission decision bounded by action category.
    /// </summary>
    public void RecordPermission(string workflowId, AutomationAction action, ActionPermission permission)
    {
        if (string.IsNullOrEmpty(workflowId)) return;

        var category = GetCategory(action);

        // Destructive actions can never be remembered as auto-approved for safety
        if (category == PermissionCategory.Destructive && permission == ActionPermission.AutoApproved)
        {
            permission = ActionPermission.Pending;
        }

        _store[(workflowId, category)] = permission;
    }

    private static bool ContainsDestructiveKeyword(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        string[] keywords = { "delete", "remove", "rm ", "destroy", "format", "drop ", "purge", "uninstall" };
        foreach (var keyword in keywords)
        {
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
