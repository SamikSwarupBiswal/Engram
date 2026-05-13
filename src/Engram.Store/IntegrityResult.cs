namespace Engram.Store;

/// <summary>
/// Result of an integrity-verified enumeration.
/// </summary>
public class IntegrityResult
{
    public IReadOnlyList<RawEvent> ValidEvents { get; init; } = Array.Empty<RawEvent>();
    public IReadOnlyList<CorruptedEvent> CorruptedEvents { get; init; } = Array.Empty<CorruptedEvent>();
}

/// <summary>
/// Represents a corrupted raw event file detected during integrity verification.
/// </summary>
public class CorruptedEvent
{
    public string FilePath { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}
