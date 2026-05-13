namespace Engram.Store;

/// <summary>
/// Query parameters for filtered raw event replay.
/// All properties are optional — null means "match all".
/// Supports pagination via Offset and Limit.
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

    /// <summary>Skip this many events (pagination). Null = no skip.</summary>
    public int? Offset { get; set; }

    /// <summary>Return at most this many events (pagination). Null = no limit.</summary>
    public int? Limit { get; set; }
}
