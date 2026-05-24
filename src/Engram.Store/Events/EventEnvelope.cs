namespace Engram.Store.Events;

/// <summary>
/// An event envelope that flows through the event bus.
/// Contains the event type, payload, and metadata.
/// </summary>
public class EventEnvelope
{
    /// <summary>Unique event ID.</summary>
    public string EventId { get; set; } = Guid.NewGuid().ToString("n");

    /// <summary>
    /// Event type (e.g., "chat.completed", "wiki.node.updated", "capture.detected").
    /// Use dot notation for hierarchical types.
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>When the event occurred.</summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Which subsystem published this event.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Event payload — type-specific data.</summary>
    public object? Payload { get; set; }

    /// <summary>Additional metadata as key-value pairs.</summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>Correlation ID for tracing related events.</summary>
    public string? CorrelationId { get; set; }
}

/// <summary>
/// Well-known event types for type safety and documentation.
/// </summary>
public static class EventTypes
{
    // Chat events
    public const string ChatCompleted = "chat.completed";
    public const string ChatFailed = "chat.failed";

    // Wiki events
    public const string WikiNodeCreated = "wiki.node.created";
    public const string WikiNodeUpdated = "wiki.node.updated";
    public const string WikiNodeArchived = "wiki.node.archived";

    // Capture events
    public const string CaptureDetected = "capture.detected";
    public const string ClipboardChanged = "clipboard.changed";
    public const string FileChanged = "file.changed";

    // Memory events
    public const string MemoryExtracted = "memory.extracted";
    public const string MemoryMetabolized = "memory.metabolized";

    // Research events
    public const string ResearchStarted = "research.started";
    public const string ResearchCompleted = "research.completed";

    // Automation events
    public const string AutomationExecuted = "automation.executed";
    public const string AutomationFailed = "automation.failed";

    // Drift events
    public const string DriftDetected = "drift.detected";

    // Lifecycle events
    public const string SystemStarted = "system.started";
    public const string SystemShuttingDown = "system.shutting_down";

    // Friction events
    public const string FrictionUserDismissed = "friction.user_dismissed";
    public const string FrictionActionCancelled = "friction.action_cancelled";
    public const string FrictionTrustOverride = "friction.trust_override";
}
