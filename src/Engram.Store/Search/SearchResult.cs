namespace Engram.Store.Search;

/// <summary>
/// A single search result with relevance scoring.
/// </summary>
public class SearchResult
{
    /// <summary>The wiki node that matched.</summary>
    public Wiki.WikiNode Node { get; init; } = new();

    /// <summary>Relevance score (0.0 to 1.0).</summary>
    public double Relevance { get; init; }

    /// <summary>Facts that matched the query.</summary>
    public List<Wiki.WikiFact> MatchingFacts { get; init; } = new();

    /// <summary>Which fields matched (title, summary, facts).</summary>
    public List<string> MatchedFields { get; init; } = new();
}

/// <summary>
/// A collection of search results with metadata.
/// </summary>
public class SearchResponse
{
    /// <summary>The original query.</summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>Search results ordered by relevance.</summary>
    public IReadOnlyList<SearchResult> Results { get; init; } = Array.Empty<SearchResult>();

    /// <summary>Total nodes searched.</summary>
    public int NodesSearched { get; init; }

    /// <summary>Search duration.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>When the search was performed.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
