using Engram.Store.Wiki;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Metabolism;

/// <summary>
/// Prevents prompt entropy explosion by managing what gets injected into the context window.
/// 
/// The hidden risk: timeline, wiki, OCR, chats, projects, events, contradictions
/// all compete for context window space. Without budget management,
/// Engram becomes noisy and incoherent.
/// 
/// Rules:
/// - Only inject: highest salience, temporally relevant, contradiction-relevant, goal-relevant, task-relevant
/// - Compress context when budget is exceeded
/// - Weight by recency, salience, and relevance
/// </summary>
public class RetrievalBudgetManager
{
    private readonly ILogger<RetrievalBudgetManager>? _logger;

    /// <summary>Maximum tokens for retrieved context.</summary>
    public int MaxContextTokens { get; set; } = 2000;

    /// <summary>Maximum number of wiki nodes to include.</summary>
    public int MaxNodes { get; set; } = 10;

    /// <summary>Maximum number of facts per node.</summary>
    public int MaxFactsPerNode { get; set; } = 3;

    /// <summary>Weight for salience in scoring (0.0-1.0).</summary>
    public double SalienceWeight { get; set; } = 0.4;

    /// <summary>Weight for recency in scoring (0.0-1.0).</summary>
    public double RecencyWeight { get; set; } = 0.3;

    /// <summary>Weight for relevance in scoring (0.0-1.0).</summary>
    public double RelevanceWeight { get; set; } = 0.3;

    public RetrievalBudgetManager(ILogger<RetrievalBudgetManager>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Select the most relevant nodes within the context budget.
    /// </summary>
    public List<WeightedNode> SelectNodes(
        IEnumerable<WikiNode> candidates,
        string query,
        DateTimeOffset now)
    {
        var scored = new List<WeightedNode>();

        foreach (var node in candidates)
        {
            var score = ComputeScore(node, query, now);
            scored.Add(new WeightedNode
            {
                Node = node,
                Score = score,
                SalienceScore = node.Salience,
                RecencyScore = ComputeRecencyScore(node.LastTouchedAt, now),
                RelevanceScore = ComputeRelevanceScore(node, query)
            });
        }

        // Sort by score descending
        var sorted = scored.OrderByDescending(w => w.Score).ToList();

        // Apply budget constraints
        var selected = ApplyBudgetConstraints(sorted);

        _logger?.LogDebug(
            "Selected {Selected} nodes from {Total} candidates (budget: {MaxTokens} tokens)",
            selected.Count, scored.Count, MaxContextTokens);

        return selected;
    }

    /// <summary>
    /// Compress selected nodes to fit within token budget.
    /// </summary>
    public string CompressContext(List<WeightedNode> weightedNodes)
    {
        var context = new System.Text.StringBuilder();
        int estimatedTokens = 0;

        foreach (var weighted in weightedNodes)
        {
            var nodeText = FormatNode(weighted.Node);
            var nodeTokens = EstimateTokens(nodeText);

            if (estimatedTokens + nodeTokens > MaxContextTokens)
            {
                // Try to fit a compressed version
                var compressed = CompressNode(weighted.Node);
                var compressedTokens = EstimateTokens(compressed);

                if (estimatedTokens + compressedTokens <= MaxContextTokens)
                {
                    context.AppendLine(compressed);
                    estimatedTokens += compressedTokens;
                }
                else
                {
                    break; // Budget exceeded
                }
            }
            else
            {
                context.AppendLine(nodeText);
                estimatedTokens += nodeTokens;
            }
        }

        return context.ToString();
    }

    /// <summary>
    /// Compute a weighted score for a node.
    /// </summary>
    private double ComputeScore(WikiNode node, string query, DateTimeOffset now)
    {
        var salience = node.Salience;
        var recency = ComputeRecencyScore(node.LastTouchedAt, now);
        var relevance = ComputeRelevanceScore(node, query);

        return (salience * SalienceWeight) +
               (recency * RecencyWeight) +
               (relevance * RelevanceWeight);
    }

    /// <summary>
    /// Compute recency score (0.0-1.0) based on time since last touch.
    /// </summary>
    private static double ComputeRecencyScore(DateTimeOffset lastTouchedAt, DateTimeOffset now)
    {
        var daysSince = (now - lastTouchedAt).TotalDays;
        if (daysSince < 0) daysSince = 0;

        // Exponential decay: 1.0 for fresh, ~0.37 after 1 day, ~0.02 after 3 days
        return Math.Exp(-0.5 * daysSince);
    }

    /// <summary>
    /// Compute relevance score (0.0-1.0) based on query match.
    /// </summary>
    private static double ComputeRelevanceScore(WikiNode node, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return 0.5; // Neutral if no query

        var queryLower = query.ToLowerInvariant();
        var titleLower = (node.Title ?? "").ToLowerInvariant();
        var summaryLower = (node.Summary ?? "").ToLowerInvariant();

        double score = 0;

        // Title match (highest weight)
        if (titleLower.Contains(queryLower)) score += 0.5;
        else if (queryLower.Contains(titleLower)) score += 0.3;

        // Summary match
        if (summaryLower.Contains(queryLower)) score += 0.3;

        // Fact match
        var factMatch = node.Facts.Any(f =>
            (f.Text ?? "").ToLowerInvariant().Contains(queryLower));
        if (factMatch) score += 0.2;

        return Math.Min(1.0, score);
    }

    /// <summary>
    /// Apply budget constraints to the sorted node list.
    /// </summary>
    private List<WeightedNode> ApplyBudgetConstraints(List<WeightedNode> sorted)
    {
        var selected = new List<WeightedNode>();
        int estimatedTokens = 0;

        foreach (var weighted in sorted)
        {
            if (selected.Count >= MaxNodes) break;

            var nodeText = FormatNode(weighted.Node);
            var nodeTokens = EstimateTokens(nodeText);

            if (estimatedTokens + nodeTokens <= MaxContextTokens)
            {
                selected.Add(weighted);
                estimatedTokens += nodeTokens;
            }
            else
            {
                // Try compressed version
                var compressed = CompressNode(weighted.Node);
                var compressedTokens = EstimateTokens(compressed);

                if (estimatedTokens + compressedTokens <= MaxContextTokens)
                {
                    selected.Add(weighted);
                    estimatedTokens += compressedTokens;
                }
            }
        }

        return selected;
    }

    /// <summary>
    /// Format a node for context injection.
    /// </summary>
    private string FormatNode(WikiNode node)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[{node.NodeType}] {node.Title}: {node.Summary}");

        var facts = node.Facts.Take(MaxFactsPerNode);
        foreach (var fact in facts)
        {
            sb.AppendLine($"  • {fact.Text}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Compress a node to fit within budget.
    /// </summary>
    private string CompressNode(WikiNode node)
    {
        var title = node.Title.Length > 30 ? node.Title[..30] + "..." : node.Title;
        var summary = (node.Summary ?? "").Length > 50 ? node.Summary[..50] + "..." : (node.Summary ?? "");

        return $"[{node.NodeType}] {title}: {summary}";
    }

    /// <summary>
    /// Estimate token count (rough: ~4 chars per token).
    /// </summary>
    private static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return text.Length / 4;
    }
}

/// <summary>
/// A wiki node with its computed weight scores.
/// </summary>
public class WeightedNode
{
    public WikiNode Node { get; set; } = null!;
    public double Score { get; set; }
    public double SalienceScore { get; set; }
    public double RecencyScore { get; set; }
    public double RelevanceScore { get; set; }
}
