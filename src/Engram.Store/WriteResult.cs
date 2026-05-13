namespace Engram.Store;

/// <summary>
/// Result of a raw event write operation.
/// </summary>
public enum WriteOutcome
{
    Created,
    Duplicate
}

public class WriteResult
{
    public WriteOutcome Outcome { get; init; }
    public string EventId { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public string Hash { get; init; } = string.Empty;
}
