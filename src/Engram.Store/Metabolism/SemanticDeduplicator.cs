using System.Text.RegularExpressions;
using Engram.Store.Wiki;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Metabolism;

/// <summary>
/// Prevents wiki rot by detecting and merging duplicate entities.
/// 
/// Without semantic dedup, the wiki WILL rot because:
/// - "Engram", "Engram project", "semantic OS", "desktop AI" all create separate nodes
/// - Memory graph fragmentation destroys continuity
/// - Retrieval becomes noisy and incoherent
/// 
/// This is the merge quality layer that matches extraction quality.
/// </summary>
public class SemanticDeduplicator
{
    private readonly WikiNodeStore _nodeStore;
    private readonly ILogger<SemanticDeduplicator>? _logger;

    /// <summary>Similarity threshold above which nodes are considered duplicates (0.0-1.0).</summary>
    public double SimilarityThreshold { get; set; } = 0.7;

    public SemanticDeduplicator(WikiNodeStore nodeStore, ILogger<SemanticDeduplicator>? logger = null)
    {
        _nodeStore = nodeStore;
        _logger = logger;
    }

    /// <summary>
    /// Find and merge duplicate nodes in the wiki.
    /// Returns the number of merges performed.
    /// </summary>
    public DeduplicationResult Deduplicate()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new DeduplicationResult();

        var nodes = _nodeStore.LoadAll();
        result.NodesAnalyzed = nodes.Count;

        // Group by type for more accurate comparison
        var groups = nodes.GroupBy(n => n.NodeType);

        foreach (var group in groups)
        {
            var typedNodes = group.ToList();
            var merges = FindAndMergeDuplicates(typedNodes);
            result.MergesPerformed += merges;
        }

        result.Duration = sw.Elapsed;
        result.Success = true;

        _logger?.LogInformation(
            "Deduplication complete: {Nodes} nodes analyzed, {Merges} merges in {Ms}ms",
            result.NodesAnalyzed, result.MergesPerformed, sw.ElapsedMilliseconds);

        return result;
    }

    /// <summary>
    /// Find and merge duplicates within a group of same-type nodes.
    /// </summary>
    private int FindAndMergeDuplicates(List<WikiNode> nodes)
    {
        int merges = 0;
        var merged = new HashSet<string>();

        for (int i = 0; i < nodes.Count; i++)
        {
            if (merged.Contains(nodes[i].NodeId)) continue;

            for (int j = i + 1; j < nodes.Count; j++)
            {
                if (merged.Contains(nodes[j].NodeId)) continue;

                var similarity = ComputeSimilarity(nodes[i], nodes[j]);
                if (similarity >= SimilarityThreshold)
                {
                    // Merge j into i (keep the one with higher salience)
                    var (keeper, absorbed) = nodes[i].Salience >= nodes[j].Salience
                        ? (nodes[i], nodes[j])
                        : (nodes[j], nodes[i]);

                    MergeNodes(keeper, absorbed);
                    _nodeStore.Save(keeper);
                    _nodeStore.Delete(absorbed.NodeId);
                    merged.Add(absorbed.NodeId);
                    merges++;

                    _logger?.LogInformation(
                        "Merged duplicate: '{Absorbed}' → '{Keeper}' (similarity: {Similarity:F2})",
                        absorbed.Title, keeper.Title, similarity);
                }
            }
        }

        return merges;
    }

    /// <summary>
    /// Compute similarity between two wiki nodes.
    /// Uses multiple signals: title, summary, facts, links.
    /// </summary>
    private static double ComputeSimilarity(WikiNode a, WikiNode b)
    {
        double score = 0;
        double weight = 0;

        // Title similarity (highest weight)
        var titleSim = ComputeTextSimilarity(a.Title, b.Title);
        score += titleSim * 3.0;
        weight += 3.0;

        // Summary similarity
        var summarySim = ComputeTextSimilarity(a.Summary, b.Summary);
        score += summarySim * 2.0;
        weight += 2.0;

        // Fact overlap
        var factSim = ComputeFactSimilarity(a.Facts, b.Facts);
        score += factSim * 1.5;
        weight += 1.5;

        // Link overlap
        var linkSim = ComputeLinkSimilarity(a.Links, b.Links);
        score += linkSim * 1.0;
        weight += 1.0;

        return weight > 0 ? score / weight : 0;
    }

    /// <summary>
    /// Compute text similarity using word overlap (Jaccard-like).
    /// </summary>
    private static double ComputeTextSimilarity(string text1, string text2)
    {
        if (string.IsNullOrWhiteSpace(text1) || string.IsNullOrWhiteSpace(text2))
            return 0;

        var words1 = Tokenize(text1);
        var words2 = Tokenize(text2);

        if (words1.Count == 0 || words2.Count == 0) return 0;

        var intersection = words1.Intersect(words2).Count();
        var union = words1.Union(words2).Count();

        return union > 0 ? (double)intersection / union : 0;
    }

    /// <summary>
    /// Compute fact similarity by comparing fact texts.
    /// </summary>
    private static double ComputeFactSimilarity(List<WikiFact> facts1, List<WikiFact> facts2)
    {
        if (facts1.Count == 0 || facts2.Count == 0) return 0;

        double maxSim = 0;
        foreach (var f1 in facts1)
        {
            foreach (var f2 in facts2)
            {
                var sim = ComputeTextSimilarity(f1.Text, f2.Text);
                maxSim = Math.Max(maxSim, sim);
            }
        }

        return maxSim;
    }

    /// <summary>
    /// Compute link overlap between two link lists.
    /// </summary>
    private static double ComputeLinkSimilarity(List<string> links1, List<string> links2)
    {
        if (links1.Count == 0 || links2.Count == 0) return 0;

        var set1 = links1.Select(NormalizeNodeId).ToHashSet();
        var set2 = links2.Select(NormalizeNodeId).ToHashSet();

        var intersection = set1.Intersect(set2).Count();
        var union = set1.Union(set2).Count();

        return union > 0 ? (double)intersection / union : 0;
    }

    /// <summary>
    /// Merge absorbed node into keeper.
    /// </summary>
    private static void MergeNodes(WikiNode keeper, WikiNode absorbed)
    {
        // Merge facts (avoid duplicates)
        foreach (var fact in absorbed.Facts)
        {
            var existing = keeper.Facts.FirstOrDefault(f =>
                ComputeTextSimilarity(f.Text, fact.Text) > 0.8);

            if (existing != null)
            {
                // Merge sources
                foreach (var source in fact.Sources)
                {
                    if (!existing.Sources.Any(s => s.EventId == source.EventId))
                        existing.Sources.Add(source);
                }
            }
            else
            {
                keeper.Facts.Add(fact);
            }
        }

        // Merge links (avoid duplicates)
        foreach (var link in absorbed.Links)
        {
            if (!keeper.Links.Contains(link))
                keeper.Links.Add(link);
        }

        // Merge open questions (avoid duplicates)
        foreach (var question in absorbed.OpenQuestions)
        {
            if (!keeper.OpenQuestions.Contains(question))
                keeper.OpenQuestions.Add(question);
        }

        // Keep the higher salience
        keeper.Salience = Math.Max(keeper.Salience, absorbed.Salience);

        // Update timestamp
        keeper.LastTouchedAt = DateTimeOffset.UtcNow;

        // Update summary if absorbed has a better one
        if (string.IsNullOrWhiteSpace(keeper.Summary) && !string.IsNullOrWhiteSpace(absorbed.Summary))
            keeper.Summary = absorbed.Summary;
    }

    /// <summary>
    /// Tokenize text into normalized words.
    /// </summary>
    private static HashSet<string> Tokenize(string text)
    {
        return text.ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\n', '\r', ',', '.', ';', ':', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 2)
            .ToHashSet();
    }

    /// <summary>
    /// Normalize a node ID for comparison.
    /// </summary>
    private static string NormalizeNodeId(string nodeId)
    {
        return nodeId.ToLowerInvariant().Replace("_", "").Replace("-", "");
    }
}

/// <summary>
/// Result of a deduplication run.
/// </summary>
public class DeduplicationResult
{
    public bool Success { get; set; }
    public int NodesAnalyzed { get; set; }
    public int MergesPerformed { get; set; }
    public TimeSpan Duration { get; set; }
}
