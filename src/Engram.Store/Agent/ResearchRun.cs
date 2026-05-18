using System.Text.Json;
using System.Text.Json.Serialization;

namespace Engram.Store.Agent;

/// <summary>
/// Represents a single research run — a multi-step investigation
/// into a topic, with sources, citations, and a final summary.
/// Persisted to .engram/research/{runId}.json.
/// </summary>
public class ResearchRun
{
    public string RunId { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public string Query { get; init; } = string.Empty;
    public ResearchStatus Status { get; set; } = ResearchStatus.Pending;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? PausedAt { get; set; }

    /// <summary>Steps executed during this research run.</summary>
    public List<ResearchStep> Steps { get; set; } = new();

    /// <summary>Sources collected during research.</summary>
    public List<ResearchSource> Sources { get; set; } = new();

    /// <summary>Final synthesized summary with citations.</summary>
    public string? Summary { get; set; }

    /// <summary>Error message if run failed.</summary>
    public string? Error { get; set; }

    /// <summary>Current step index (for resumable runs).</summary>
    public int CurrentStepIndex { get; set; }

    /// <summary>Total steps planned.</summary>
    public int TotalSteps { get; set; }

    /// <summary>Progress percentage (0-100).</summary>
    public double Progress => TotalSteps > 0
        ? Math.Min(100, (double)CurrentStepIndex / TotalSteps * 100)
        : 0;

    /// <summary>Duration of the run.</summary>
    public TimeSpan Duration => (CompletedAt ?? DateTimeOffset.UtcNow) - CreatedAt;
}

public enum ResearchStatus
{
    Pending,
    Running,
    Paused,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// A single step in a research run.
/// Each step represents one action: search, scrape, analyze, synthesize.
/// </summary>
public class ResearchStep
{
    public string StepId { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public ResearchStepType Type { get; init; }
    public string Description { get; init; } = string.Empty;
    public ResearchStepStatus Status { get; set; } = ResearchStepStatus.Pending;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Input to this step (query, URL, etc.)</summary>
    public string? Input { get; init; }

    /// <summary>Output from this step (extracted text, analysis, etc.)</summary>
    public string? Output { get; set; }

    /// <summary>Sources discovered in this step.</summary>
    public List<string> SourceUrls { get; set; } = new();

    /// <summary>Error if step failed.</summary>
    public string? Error { get; set; }
}

public enum ResearchStepType
{
    Search,        // Web search for query
    Scrape,        // Fetch and extract content from URL
    Analyze,       // Analyze extracted content
    Synthesize,    // Combine findings into summary
    CiteLink       // Link citations to wiki facts
}

public enum ResearchStepStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Skipped
}

/// <summary>
/// A source discovered during research.
/// Links back to a URL with extracted content and metadata.
/// </summary>
public class ResearchSource
{
    public string SourceId { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public string Url { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Domain { get; init; } = string.Empty;
    public string ExtractedText { get; init; } = string.Empty;
    public DateTimeOffset DiscoveredAt { get; init; } = DateTimeOffset.UtcNow;
    public double RelevanceScore { get; init; }

    /// <summary>Citation key for referencing in summary (e.g., [1], [2]).</summary>
    public int CitationIndex { get; init; }
}

/// <summary>
/// A citation linking a fact in the summary to a source.
/// </summary>
public class Citation
{
    public int Index { get; init; }
    public string SourceId { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Snippet { get; init; } = string.Empty;
    public string Quote { get; init; } = string.Empty;
}
