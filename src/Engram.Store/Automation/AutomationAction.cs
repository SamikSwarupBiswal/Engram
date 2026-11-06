using System.Text.Json.Serialization;

namespace Engram.Store.Automation;

/// <summary>
/// A single automation action — click, type, navigate, wait, screenshot.
/// Execution follows the validated automatic scope or explicit user approval.
/// </summary>
public class AutomationAction
{
    public string ActionId { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public ActionType Type { get; set; }
    public string Description { get; set; } = string.Empty;
    public ActionTarget? Target { get; set; }
    public string? Value { get; set; }
    public ActionPermission Permission { get; set; } = ActionPermission.Pending;
    public ActionStatus Status { get; set; } = ActionStatus.Pending;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExecutedAt { get; set; }
    public string? Result { get; set; }
    public string? Error { get; set; }
    public string? ScreenshotBefore { get; set; }
    public string? ScreenshotAfter { get; set; }
    public TransitionalState ExpectedTransition { get; set; } = TransitionalState.Stable;
    public AutomationRiskLevel RiskLevel { get; set; } = AutomationRiskLevel.Low;
    public string? ConfirmationReason { get; set; }
    [JsonIgnore]
    internal string? AutomaticApprovalFingerprint { get; set; }
    [JsonIgnore]
    internal TextFileOutputBinding? TextFileOutput { get; set; }
    /// <summary>
    /// Stable, provider-neutral capability requested by the planner. The plugin host
    /// resolves this identifier to a trusted implementation at execution time.
    /// </summary>
    public string? CapabilityId { get; set; }
    /// <summary>
    /// Typed capability arguments. These are kept separate from the legacy Value
    /// payload so plugin dispatch never needs to reinterpret free-form instructions.
    /// </summary>
    public Dictionary<string, string> CapabilityArguments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record AutomationOwnershipHandle(
    string Kind,
    string BindingId,
    int? ProcessId = null,
    long? WindowHandle = null,
    string? Path = null,
    string? Url = null);

public enum AutomationRiskLevel
{
    Low,
    Medium,
    High
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
    Download,      // Download file
    RunCommand,    // Run shell command
    Extract,       // Extract text content from DOM element
    MovePointer,   // Move mouse pointer without clicking
    DoubleClick,   // Double-click desktop coordinates
    RightClick,    // Right-click desktop coordinates
    Drag,          // Drag from X,Y to X2,Y2
    LaunchApplication, // Launch a trusted desktop application directly
    FocusApplication,  // Focus an already-running verified desktop application
    FileWrite,     // Write user-authorized content to a specific local file
    DeleteFile,    // Delete a specific local file after confirmation
    MoveFile,      // Rename/move one explicit local file without implicit overwrite
    RescanApplications, // Refresh and persist the installed-application inventory
    OrganizeFiles, // Execute one approved, immutable filesystem-organization manifest
    Calculate // Evaluate one arithmetic expression in Windows Calculator and verify its display
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
    public string? Selector { get; set; }   // CSS selector
    public string? Text { get; set; }        // Text to find
    public int? X { get; set; }              // X coordinate
    public int? Y { get; set; }              // Y coordinate
    public int? X2 { get; set; }             // Drag destination X coordinate
    public int? Y2 { get; set; }             // Drag destination Y coordinate
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
