using Engram.Store.Salience;
using Engram.Store.Wiki;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Metabolism;

/// <summary>
/// Compresses the semantic graph to prevent memory explosion.
/// 
/// As Engram accumulates data, the knowledge graph grows without bound.
/// Without compression:
/// - Retrieval quality degrades (too many nodes to search)
/// - Salience scoring becomes less meaningful (too many low-salience nodes)
/// - Metabolism cycles slow down (too many nodes to process)
/// - Interventions become noisy (too many tensions to consider)
/// 
/// Compression strategies:
/// 1. Pruning — remove nodes that are stale, low-salience, and unreferenced
/// 2. Merging — combine similar nodes into richer nodes
/// 3. Abstraction — convert specific observations into general patterns
/// 4. Archival — move old nodes to cold storage
/// 
/// This is NOT deletion. It's semantic compaction.
/// </summary>
public class SemanticCompressor
{
    private readonly WikiNodeStore _nodeStore;
    private readonly ILogger<SemanticCompressor>? _logger;

    /// <summary>Minimum salience to keep a node (below this = archive candidate).</summary>
    public double MinSalienceForRetention { get; set; } = 0.05;

    /// <summary>Minimum facts to keep a node (below this = merge candidate).</summary>
    public int MinFactsForRetention { get; set; } = 1;

    /// <summary>Maximum age in days for unreferenced nodes.</summary>
    public int MaxUnreferencedAgeDays { get; set; } = 60;

    public SemanticCompressor(
        WikiNodeStore nodeStore,
        ILogger<SemanticCompressor>? logger = null)
    {
        _nodeStore = nodeStore;
    }

    /// <summary>
    /// Analyze the graph and produce compression recommendations.
    /// Does NOT modify the graph — only reports what should be done.
    /// </summary>
    public CompressionReport AnalyzeForCompression()
    {
        var nodes = _nodeStore.LoadAll();

        var pruneCandidates = FindPruneCandidates(nodes);
        var mergeCandidates = FindMergeCandidates(nodes);
        var archiveCandidates = FindArchiveCandidates(nodes);
        var abstractionCandidates = FindAbstractionCandidates(nodes);

        var totalActionable = pruneCandidates.Count + archiveCandidates.Count;

        _logger?.LogInformation(
            "Compression analysis: {Total} nodes, {Prune} prune, {Merge} merge, {Archive} archive, {Abstract} abstract",
            nodes.Count, pruneCandidates.Count, mergeCandidates.Count, archiveCandidates.Count, abstractionCandidates.Count);

        return new CompressionReport
        {
            AnalyzedAt = DateTimeOffset.UtcNow,
            TotalNodes = nodes.Count,
            PruneCandidates = pruneCandidates.Select(n => n.Title).ToList(),
            MergeCandidates = mergeCandidates.Select(p => new MergeCandidatePair
            {
                SourceNode = p.Item1.Title,
                TargetNode = p.Item2.Title,
                Similarity = ComputeSimilarity(p.Item1, p.Item2)
            }).ToList(),
            ArchiveCandidates = archiveCandidates.Select(n => n.Title).ToList(),
            AbstractionCandidates = abstractionCandidates.Select(n => n.Title).ToList(),
            EstimatedReduction = totalActionable,
            EstimatedReductionPercent = nodes.Count > 0 ? (double)totalActionable / nodes.Count : 0
        };
    }

    /// <summary>
    /// Execute pruning — remove nodes that are stale, low-salience, and unreferenced.
    /// Returns the number of nodes pruned.
    /// </summary>
    public int ExecutePruning()
    {
        var nodes = _nodeStore.LoadAll();
        var candidates = FindPruneCandidates(nodes);

        int pruned = 0;
        foreach (var node in candidates)
        {
            // Check no other node references this one
            var isReferenced = nodes.Any(n =>
                n.NodeId != node.NodeId &&
                n.Links.Any(r => r.Contains(node.NodeId)));

            if (!isReferenced)
            {
                _nodeStore.Delete(node.NodeId);
                pruned++;
            }
        }

        _logger?.LogInformation("Pruned {Count} nodes from graph", pruned);
        return pruned;
    }

    /// <summary>
    /// Execute archival — move old, low-activity nodes to archive.
    /// Returns the number of nodes archived.
    /// </summary>
    public int ExecuteArchival(ArchiveManager archiveManager)
    {
        var nodes = _nodeStore.LoadAll();
        var candidates = FindArchiveCandidates(nodes);

        int archived = 0;
        foreach (var node in candidates)
        {
            try
            {
                archiveManager.ArchiveNode(node);
                archived++;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to archive node {NodeId}", node.NodeId);
            }
        }

        _logger?.LogInformation("Archived {Count} nodes", archived);
        return archived;
    }

    private List<WikiNode> FindPruneCandidates(IReadOnlyList<WikiNode> nodes)
    {
        // Prune: stale + low salience + few facts + no relations
        return nodes.Where(n =>
            !SemanticCompactor.IsProtectedNode(n) &&
            n.Salience < MinSalienceForRetention &&
            n.Facts.Count <= MinFactsForRetention &&
            n.Links.Count == 0 &&
            (DateTimeOffset.UtcNow - n.LastTouchedAt).TotalDays > MaxUnreferencedAgeDays)
            .ToList();
    }

    private List<(WikiNode, WikiNode)> FindMergeCandidates(IReadOnlyList<WikiNode> nodes)
    {
        var candidates = new List<(WikiNode, WikiNode)>();
        var nodeList = nodes.ToList();

        // Find pairs with high title similarity
        for (int i = 0; i < nodeList.Count; i++)
        {
            for (int j = i + 1; j < nodeList.Count; j++)
            {
                if (SemanticCompactor.IsProtectedNode(nodeList[i]) || SemanticCompactor.IsProtectedNode(nodeList[j])) continue;

                var similarity = ComputeSimilarity(nodeList[i], nodeList[j]);
                if (similarity > 0.7 && nodeList[i].NodeType == nodeList[j].NodeType)
                {
                    candidates.Add((nodeList[i], nodeList[j]));
                }
            }
        }

        return candidates;
    }

    private List<WikiNode> FindArchiveCandidates(IReadOnlyList<WikiNode> nodes)
    {
        // Archive: old activity + low salience (but not as extreme as prune candidates)
        return nodes.Where(n =>
            !SemanticCompactor.IsProtectedNode(n) &&
            n.Salience < 0.2 &&
            (DateTimeOffset.UtcNow - n.LastTouchedAt).TotalDays > 21 &&
            n.NodeType != WikiNodeType.Goal) // Don't archive goals
            .ToList();
    }

    private List<WikiNode> FindAbstractionCandidates(IReadOnlyList<WikiNode> nodes)
    {
        // Abstraction candidates: nodes with many specific facts that could be summarized
        return nodes.Where(n =>
            n.Facts.Count > 10 &&
            n.NodeType == WikiNodeType.Concept)
            .ToList();
    }

    private static double ComputeSimilarity(WikiNode a, WikiNode b)
    {
        // Title similarity using word overlap
        var wordsA = a.Title.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var wordsB = b.Title.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        if (wordsA.Count == 0 && wordsB.Count == 0) return 1.0;
        if (wordsA.Count == 0 || wordsB.Count == 0) return 0;

        var intersection = wordsA.Intersect(wordsB).Count();
        var union = wordsA.Union(wordsB).Count();

        return (double)intersection / union;
    }
}

/// <summary>
/// Report of compression analysis.
/// </summary>
public record CompressionReport
{
    public DateTimeOffset AnalyzedAt { get; init; }
    public int TotalNodes { get; init; }
    public List<string> PruneCandidates { get; init; } = new();
    public List<MergeCandidatePair> MergeCandidates { get; init; } = new();
    public List<string> ArchiveCandidates { get; init; } = new();
    public List<string> AbstractionCandidates { get; init; } = new();
    public int EstimatedReduction { get; init; }
    public double EstimatedReductionPercent { get; init; }
}

/// <summary>
/// A pair of nodes that could be merged.
/// </summary>
public record MergeCandidatePair
{
    public string SourceNode { get; init; } = string.Empty;
    public string TargetNode { get; init; } = string.Empty;
    public double Similarity { get; init; }
}
