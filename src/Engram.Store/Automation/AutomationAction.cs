using System.Text.Json.Serialization;

namespace Engram.Store.Automation;

/// <summary>
/// A single automation action — click, type, navigate, wait, screenshot.
/// Each action requires explicit user approval before execution.
/// </summary>
public class AutomationAction
{
    public string ActionId { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public ActionType Type { get; init; }
    public string Description { get; init; } = string.Empty;
    public ActionTarget? Target { get; init; }
    public string? Value { get; init; }
    public ActionPermission Permission { get; set; } = ActionPermission.Pending;
    public ActionStatus Status { get; set; } = ActionStatus.Pending;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExecutedAt { get; set; }
    public string? Result { get; set; }
    public string? Error { get; set; }
    public string? ScreenshotBefore { get; set; }
    public string? ScreenshotAfter { get; set; }
}

public enum ActionType
{
    Navigate,      // Go to URL
    Click,         // Click element
    Type,          // Type text into field
    KeyPress,      // Press keyboard key
    Wait,          // Wait for condition
    Screenshot,    // Take screenshot
    Scroll,        // Scroll page
    Select,        // Select dropdown option
    Upload,        // Upload file
    Download       // Download file
}

public enum ActionPermission
{
    Pending,       // Awaiting user approval
    Approved,      // User approved
    Denied,        // User denied
    AutoApproved   // Safe action, auto-approved
}

public enum ActionStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Denied,
    RolledBack
}

/// <summary>
/// Target element for an action (selector, coordinates, or text).
/// </summary>
public class ActionTarget
{
    public string? Selector { get; init; }   // CSS selector
    public string? Text { get; init; }        // Text to find
    public int? X { get; init; }              // X coordinate
    public int? Y { get; init; }              // Y coordinate
}

/// <summary>
/// An action plan — a sequence of actions to achieve a goal.
/// </summary>
public class ActionPlan
{
    public string PlanId { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public string Goal { get; init; } = string.Empty;
    public ActionPlanStatus Status { get; set; } = ActionPlanStatus.Draft;
    public List<AutomationAction> Actions { get; set; } = new();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public int CurrentActionIndex { get; set; }

    public double Progress => Actions.Count > 0
        ? Math.Min(100, (double)CurrentActionIndex / Actions.Count * 100)
        : 0;
}

public enum ActionPlanStatus
{
    Draft,          // Plan created, not yet submitted
    PendingApproval,// Awaiting user approval
    Executing,      // Running approved actions
    Completed,      // All actions done
    Failed,         // Action failed
    Cancelled       // User cancelled
}

/// <summary>
/// Log entry for an executed action.
/// </summary>
public class ActionLogEntry
{
    public string LogId { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public string ActionId { get; init; } = string.Empty;
    public string PlanId { get; init; } = string.Empty;
    public ActionType Type { get; init; }
    public string Description { get; init; } = string.Empty;
    public ActionPermission Permission { get; init; }
    public ActionStatus Status { get; init; }
    public string? Result { get; init; }
    public string? Error { get; init; }
    public DateTimeOffset ExecutedAt { get; init; } = DateTimeOffset.UtcNow;
    public TimeSpan? Duration { get; init; }
}
