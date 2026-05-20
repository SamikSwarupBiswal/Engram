using Engram.Store.Wiki;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Metabolism;

/// <summary>
/// Detects memory pollution — degradation of the knowledge graph over time.
/// 
/// As Engram accumulates data, the knowledge graph can become polluted with:
/// - Stale facts (assumptions that were never updated)
/// - Repeated false patterns (same incorrect interpretation stored multiple times)
/// - Overrepresented tensions (a few tensions dominating the graph)
/// - Retrieval loops (same nodes appearing in every search)
/// - Orphaned nodes (nodes with no relations, no recent activity)
/// 
/// Without pollution detection, the graph slowly degrades until
/// retrieval quality collapses and interventions become noise.
/// </summary>
public class MemoryPollutionDetector
{
    private readonly WikiNodeStore _nodeStore;
    private readonly ILogger<MemoryPollutionDetector>? _logger;

    /// <summary>Days since last touch to consider a node stale.</summary>
    public int StaleDaysThreshold { get; set; } = 30;

    /// <summary>Maximum percentage of total nodes that can be one type before overrepresentation.</summary>
    public double OverrepresentationThreshold { get; set; } = 0.4;

    /// <summary>Salience threshold for considering a node as low-value.</summary>
    public double LowSalienceThreshold { get; set; } = 0.1;

    public MemoryPollutionDetector(
        WikiNodeStore nodeStore,
        ILogger<MemoryPollutionDetector>? logger = null)
    {
        _nodeStore = nodeStore;
        _logger = logger;
    }

    /// <summary>
    /// Run a full memory pollution analysis.
    /// </summary>
    public PollutionReport Analyze()
    {
        var nodes = _nodeStore.LoadAll();

        var staleNodes = DetectStaleNodes(nodes);
        var orphanedNodes = DetectOrphanedNodes(nodes);
        var overrepresentation = DetectOverrepresentation(nodes);
        var retrievalLoops = DetectRetrievalLoops(nodes);
        var lowSalienceNodes = DetectLowSalience(nodes);

        var totalIssues = staleNodes.Count + orphanedNodes.Count +
            overrepresentation.Count + retrievalLoops.Count + lowSalienceNodes.Count;

        var pollutionScore = nodes.Count > 0
            ? (double)totalIssues / (nodes.Count * 5) // 5 categories, each can affect each node
            : 0;

        var warnings = new List<PollutionWarning>();

        if (staleNodes.Count > nodes.Count * 0.3)
            warnings.Add(new PollutionWarning
            {
                Severity = PollutionSeverity.High,
                Category = "stale",
                Message = $"{staleNodes.Count} nodes ({(double)staleNodes.Count / nodes.Count:P0}) are stale (>{StaleDaysThreshold} days). Memory graph is aging."
            });

        if (orphanedNodes.Count > nodes.Count * 0.2)
            warnings.Add(new PollutionWarning
            {
                Severity = PollutionSeverity.Medium,
                Category = "orphaned",
                Message = $"{orphanedNodes.Count} orphaned nodes with no relations. Consider pruning."
            });

        if (retrievalLoops.Count > 0)
            warnings.Add(new PollutionWarning
            {
                Severity = PollutionSeverity.High,
                Category = "retrieval_loop",
                Message = $"{retrievalLoops.Count} nodes dominate retrieval (appear in >50% of searches)."
            });

        if (lowSalienceNodes.Count > nodes.Count * 0.4)
            warnings.Add(new PollutionWarning
            {
                Severity = PollutionSeverity.Medium,
                Category = "low_salience",
                Message = $"{lowSalienceNodes.Count} nodes have salience < {LowSalienceThreshold}. Archive candidates."
            });

        _logger?.LogInformation(
            "Memory pollution analysis: {Total} nodes, {Issues} issues, score={Score:P1}",
            nodes.Count, totalIssues, pollutionScore);

        return new PollutionReport
        {
            AnalyzedAt = DateTimeOffset.UtcNow,
            TotalNodes = nodes.Count,
            PollutionScore = pollutionScore,
            StaleNodes = staleNodes.Select(n => n.Title).ToList(),
            OrphanedNodes = orphanedNodes.Select(n => n.Title).ToList(),
            OverrepresentedTypes = overrepresentation,
            RetrievalLoopNodes = retrievalLoops.Select(n => n.Title).ToList(),
            LowSalienceNodes = lowSalienceNodes.Select(n => n.Title).ToList(),
            Warnings = warnings
        };
    }

    /// <summary>
    /// Get nodes that should be pruned or archived.
    /// </summary>
    public List<WikiNode> GetPruneCandidates()
    {
        var nodes = _nodeStore.LoadAll();
        var candidates = new List<WikiNode>();

        // Stale + low salience + no relations = prune candidate
        candidates.AddRange(nodes.Where(n =>
            (DateTimeOffset.UtcNow - n.LastTouchedAt).TotalDays > StaleDaysThreshold &&
            n.Salience < LowSalienceThreshold &&
            n.Links.Count == 0));

        return candidates;
    }

    private List<WikiNode> DetectStaleNodes(IReadOnlyList<WikiNode> nodes)
    {
        return nodes
            .Where(n => (DateTimeOffset.UtcNow - n.LastTouchedAt).TotalDays > StaleDaysThreshold)
            .ToList();
    }

    private List<WikiNode> DetectOrphanedNodes(IReadOnlyList<WikiNode> nodes)
    {
        return nodes
            .Where(n => n.Links.Count == 0 && n.Facts.Count <= 1)
            .ToList();
    }

    private Dictionary<string, int> DetectOverrepresentation(IReadOnlyList<WikiNode> nodes)
    {
        if (nodes.Count == 0) return new Dictionary<string, int>();

        var typeCounts = nodes
            .GroupBy(n => n.NodeType.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        return typeCounts
            .Where(kv => (double)kv.Value / nodes.Count > OverrepresentationThreshold)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    private List<WikiNode> DetectRetrievalLoops(IReadOnlyList<WikiNode> nodes)
    {
        // Nodes with very high salience AND many facts AND recent touches
        // tend to dominate retrieval
        return nodes
            .Where(n => n.Salience > 0.8 && n.Facts.Count > 5)
            .ToList();
    }

    private List<WikiNode> DetectLowSalience(IReadOnlyList<WikiNode> nodes)
    {
        return nodes
            .Where(n => n.Salience < LowSalienceThreshold)
            .ToList();
    }
}

/// <summary>
/// Report of memory pollution analysis.
/// </summary>
public record PollutionReport
{
    public DateTimeOffset AnalyzedAt { get; init; }
    public int TotalNodes { get; init; }
    public double PollutionScore { get; init; }
    public List<string> StaleNodes { get; init; } = new();
    public List<string> OrphanedNodes { get; init; } = new();
    public Dictionary<string, int> OverrepresentedTypes { get; init; } = new();
    public List<string> RetrievalLoopNodes { get; init; } = new();
    public List<string> LowSalienceNodes { get; init; } = new();
    public List<PollutionWarning> Warnings { get; init; } = new();
}

/// <summary>
/// A pollution warning.
/// </summary>
public record PollutionWarning
{
    public PollutionSeverity Severity { get; init; }
    public string Category { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public enum PollutionSeverity
{
    Low,
    Medium,
    High,
    Critical
}
