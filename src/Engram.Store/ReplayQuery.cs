namespace Engram.Store;

/// <summary>
/// Query parameters for filtered raw event replay.
/// All properties are optional — null means "match all".
/// </summary>
public class ReplayQuery
{
    /// <summary>Include events from this date (inclusive). Null = no lower bound.</summary>
    public DateOnly? FromDate { get; set; }

    /// <summary>Include events up to this date (inclusive). Null = no upper bound.</summary>
    public DateOnly? ToDate { get; set; }

    /// <summary>Filter by event source. Null = match all sources.</summary>
    public string? Source { get; set; }

    /// <summary>Filter by processing status (from sidecar). Null = match all statuses.</summary>
    public string? ProcessingStatus { get; set; }
}
