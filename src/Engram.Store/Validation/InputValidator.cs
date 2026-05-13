namespace Engram.Store.Validation;

/// <summary>
/// Validates inputs against real-world attack vectors and edge cases.
/// </summary>
public static class InputValidator
{
    private const int MaxEventTextSize = 10 * 1024 * 1024; // 10MB
    private const int MaxEventIdLength = 256;
    private const int MaxEventTypeLength = 128;
    private const int MaxSourceLength = 256;

    public static void ValidateRootPath(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            throw new EngramValidationException("root", "Workspace root path cannot be null or empty.");

        if (root.Contains(".."))
            throw new EngramValidationException("root", "Path traversal ('..') is not allowed in workspace root.");

        var invalidChars = Path.GetInvalidPathChars();
        if (root.Any(c => invalidChars.Contains(c)))
            throw new EngramValidationException("root", "Workspace root contains invalid path characters.");
    }

    public static void ValidateEventId(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId))
            throw new EngramValidationException("event_id", "Event ID cannot be null or empty.");

        if (eventId.Length > MaxEventIdLength)
            throw new EngramValidationException("event_id", $"Event ID exceeds maximum length of {MaxEventIdLength}.");

        if (eventId.Contains("..") || eventId.Contains('/') || eventId.Contains('\\'))
            throw new EngramValidationException("event_id", "Event ID contains path traversal or separator characters.");

        var invalidChars = Path.GetInvalidFileNameChars();
        if (eventId.Any(c => invalidChars.Contains(c)))
            throw new EngramValidationException("event_id", "Event ID contains invalid file name characters.");
    }

    public static void ValidateEventType(string eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType))
            throw new EngramValidationException("event_type", "Event type cannot be null or empty.");

        if (eventType.Length > MaxEventTypeLength)
            throw new EngramValidationException("event_type", $"Event type exceeds maximum length of {MaxEventTypeLength}.");
    }

    public static void ValidateSource(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new EngramValidationException("source", "Source cannot be null or empty.");

        if (source.Length > MaxSourceLength)
            throw new EngramValidationException("source", $"Source exceeds maximum length of {MaxSourceLength}.");
    }

    public static void ValidateTextSize(string? text)
    {
        if (text != null && System.Text.Encoding.UTF8.GetByteCount(text) > MaxEventTextSize)
            throw new EngramValidationException("text", $"Text exceeds maximum size of {MaxEventTextSize / (1024 * 1024)}MB.");
    }

    public static void ValidateRawEvent(RawEvent rawEvent)
    {
        ArgumentNullException.ThrowIfNull(rawEvent);
        ValidateEventId(rawEvent.EventId);
        ValidateEventType(rawEvent.EventType);
        ValidateSource(rawEvent.Source);
        ValidateTextSize(rawEvent.Text);
    }
}
