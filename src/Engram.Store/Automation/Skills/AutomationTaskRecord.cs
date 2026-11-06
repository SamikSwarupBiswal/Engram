namespace Engram.Store.Automation.Skills;

public enum AutomationTaskState
{
    Planned,
    AwaitingConfirmation,
    Running,
    Completed,
    Failed,
    Cancelled
}

public sealed class AutomationTaskRecord
{
    public required string TaskId { get; init; }
    public required string Goal { get; init; }
    public required ExecutionPlan Plan { get; init; }
    public AutomationTaskState State { get; internal set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; internal set; }
    public string? Error { get; internal set; }
    public string? ConversationId { get; init; }
    public string? MemoryError { get; internal set; }
    [System.Text.Json.Serialization.JsonIgnore]
    internal long? MemoryGeneration { get; init; }
    public int PendingConfirmations => Plan.Steps.Values.Count(step => step.Action.Permission == ActionPermission.Pending);
}
