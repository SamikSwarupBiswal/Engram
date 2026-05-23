using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Engram.Store.Inference;
using Engram.Store.Wiki;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Metabolism;

/// <summary>
/// Compacts the semantic graph by merging overlapping/redundant entities.
/// Uses programmatic heuristics for deterministic merging, and refines
/// summaries/titles via the local LLM when available.
/// </summary>
public class SemanticCompactor
{
    private readonly WikiNodeStore _nodeStore;
    private readonly LocalInferenceEngine? _inferenceEngine;
    private readonly ILogger<SemanticCompactor>? _logger;

    public SemanticCompactor(
        WikiNodeStore nodeStore,
        LocalInferenceEngine? inferenceEngine = null,
        ILogger<SemanticCompactor>? logger = null)
    {
        _nodeStore = nodeStore;
        _inferenceEngine = inferenceEngine;
        _logger = logger;
    }

    /// <summary>
    /// Scan the graph and execute compaction for pairs matching the similarity threshold.
    /// </summary>
    public async Task<int> CompactGraphAsync(double similarityThreshold, CancellationToken ct = default)
    {
        var nodes = _nodeStore.LoadAll();
        var pairsToMerge = FindMergePairs(nodes, similarityThreshold);

        int mergeCount = 0;
        foreach (var (nodeA, nodeB) in pairsToMerge)
        {
            if (ct.IsCancellationRequested) break;

            // Re-load nodes to ensure they weren't deleted in a previous step
            var freshA = _nodeStore.Load(nodeA.NodeId);
            var freshB = _nodeStore.Load(nodeB.NodeId);

            if (freshA == null || freshB == null) continue;

            _logger?.LogInformation("Merging nodes: {NodeA} and {NodeB}", freshA.Title, freshB.Title);

            // Execute merge
            var mergedNode = await MergeNodesAsync(freshA, freshB, ct);
            _nodeStore.Save(mergedNode);

            // Redirect references from other nodes
            RedirectReferences(nodes, freshA.NodeId, freshB.NodeId, mergedNode.NodeId);

            // Delete original nodes
            _nodeStore.Delete(freshA.NodeId);
            _nodeStore.Delete(freshB.NodeId);

            mergeCount++;
        }

        return mergeCount;
    }

    /// <summary>
    /// Find pairs of nodes that qualify for merging based on title Jaccard similarity.
    /// </summary>
    public List<(WikiNode, WikiNode)> FindMergePairs(IReadOnlyList<WikiNode> nodes, double threshold)
    {
        var pairs = new List<(WikiNode, WikiNode)>();
        var list = nodes.ToList();

        for (int i = 0; i < list.Count; i++)
        {
            for (int j = i + 1; j < list.Count; j++)
            {
                if (list[i].NodeType != list[j].NodeType) continue;

                var similarity = ComputeJaccardSimilarity(list[i].Title, list[j].Title);
                if (similarity >= threshold)
                {
                    pairs.Add((list[i], list[j]));
                }
            }
        }

        return pairs;
    }

    private double ComputeJaccardSimilarity(string titleA, string titleB)
    {
        var wordsA = titleA.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var wordsB = titleB.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        if (wordsA.Count == 0 && wordsB.Count == 0) return 1.0;
        if (wordsA.Count == 0 || wordsB.Count == 0) return 0.0;

        var intersection = wordsA.Intersect(wordsB).Count();
        var union = wordsA.Union(wordsB).Count();

        return (double)intersection / union;
    }

    private async Task<WikiNode> MergeNodesAsync(WikiNode nodeA, WikiNode nodeB, CancellationToken ct)
    {
        // 1. Determine baseline programmatic merge properties
        var mergedId = $"{nodeA.NodeType.ToString().ToLower()}_{Guid.NewGuid().ToString("n")[..8]}";
        var mergedNode = new WikiNode
        {
            NodeId = mergedId,
            NodeType = nodeA.NodeType,
            Title = nodeA.Title.Length >= nodeB.Title.Length ? nodeA.Title : nodeB.Title,
            Summary = $"{nodeA.Summary} | {nodeB.Summary}".Trim(' ', '|'),
            Facts = MergeFacts(nodeA.Facts, nodeB.Facts),
            OpenQuestions = nodeA.OpenQuestions.Union(nodeB.OpenQuestions).Distinct().ToList(),
            Links = nodeA.Links.Union(nodeB.Links).Distinct().ToList(),
            Edges = MergeEdges(nodeA.Edges, nodeB.Edges),
            Claims = MergeClaims(nodeA.Claims, nodeB.Claims),
            Salience = Math.Max(nodeA.Salience, nodeB.Salience),
            Confidence = Math.Max(nodeA.Confidence, nodeB.Confidence),
            LastTouchedAt = nodeA.LastTouchedAt > nodeB.LastTouchedAt ? nodeA.LastTouchedAt : nodeB.LastTouchedAt,
            CreatedAt = nodeA.CreatedAt < nodeB.CreatedAt ? nodeA.CreatedAt : nodeB.CreatedAt
        };

        // Remove self-links if any were created
        mergedNode.Links.Remove(nodeA.NodeId);
        mergedNode.Links.Remove(nodeB.NodeId);

        // 2. Refine summary and title using LLM if available
        if (_inferenceEngine != null && _inferenceEngine.IsReady)
        {
            try
            {
                var messages = new[]
                {
                    new ChatMessage { Role = "system", Content = "You are a database compaction worker. Merge two Entity Graph nodes into one clean, consolidated node profile." },
                    new ChatMessage { Role = "user", Content = $"Merge Node A and Node B:\n\nNode A:\nTitle: {nodeA.Title}\nSummary: {nodeA.Summary}\n\nNode B:\nTitle: {nodeB.Title}\nSummary: {nodeB.Summary}\n\nReturn the consolidated result in this format:\nTitle: [Unified Title]\nSummary: [Single concise unified summary]" }
                };

                var result = await _inferenceEngine.ChatCompletionAsync(messages, 512, ct);
                if (result.Success && !string.IsNullOrWhiteSpace(result.Content))
                {
                    ParseLlmMergeResponse(result.Content, mergedNode);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "LLM refinement of merged node failed. Falling back to heuristic merge.");
            }
        }

        return mergedNode;
    }

    private void ParseLlmMergeResponse(string content, WikiNode target)
    {
        var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (line.StartsWith("Title:", StringComparison.OrdinalIgnoreCase))
            {
                target.Title = line["Title:".Length..].Trim();
            }
            else if (line.StartsWith("Summary:", StringComparison.OrdinalIgnoreCase))
            {
                target.Summary = line["Summary:".Length..].Trim();
            }
        }
    }

    private List<WikiFact> MergeFacts(List<WikiFact> factsA, List<WikiFact> factsB)
    {
        var merged = new List<WikiFact>(factsA);
        foreach (var factB in factsB)
        {
            // Simple deduplication based on word overlap of fact texts
            var exists = merged.Any(f => ComputeJaccardSimilarity(f.Text, factB.Text) > 0.8);
            if (!exists)
            {
                merged.Add(factB);
            }
            else
            {
                // Update source references for the existing fact
                var existing = merged.First(f => ComputeJaccardSimilarity(f.Text, factB.Text) > 0.8);
                existing.Sources = existing.Sources.Union(factB.Sources).ToList();
            }
        }
        return merged;
    }

    private List<WikiEdge> MergeEdges(List<WikiEdge> edgesA, List<WikiEdge> edgesB)
    {
        var merged = new Dictionary<string, WikiEdge>();
        foreach (var edge in edgesA.Concat(edgesB))
        {
            if (merged.TryGetValue(edge.TargetNodeId, out var existing))
            {
                existing.PropagationWeight = Math.Max(existing.PropagationWeight, edge.PropagationWeight);
            }
            else
            {
                merged[edge.TargetNodeId] = edge;
            }
        }
        return merged.Values.ToList();
    }

    private List<SemanticClaim> MergeClaims(List<SemanticClaim> claimsA, List<SemanticClaim> claimsB)
    {
        var merged = new Dictionary<string, SemanticClaim>();
        foreach (var claim in claimsA.Concat(claimsB))
        {
            var key = $"{claim.Property}:{claim.Value}";
            if (merged.TryGetValue(key, out var existing))
            {
                existing.Confidence = Math.Max(existing.Confidence, claim.Confidence);
            }
            else
            {
                merged[key] = claim;
            }
        }
        return merged.Values.ToList();
    }

    private void RedirectReferences(IReadOnlyList<WikiNode> allNodes, string oldIdA, string oldIdB, string newId)
    {
        foreach (var node in allNodes)
        {
            bool updated = false;

            // 1. Redirect links
            for (int i = 0; i < node.Links.Count; i++)
            {
                if (node.Links[i] == oldIdA || node.Links[i] == oldIdB)
                {
                    node.Links[i] = newId;
                    updated = true;
                }
            }

            // 2. Redirect edges
            foreach (var edge in node.Edges)
            {
                if (edge.TargetNodeId == oldIdA || edge.TargetNodeId == oldIdB)
                {
                    edge.TargetNodeId = newId;
                    updated = true;
                }
            }

            if (updated)
            {
                node.Links = node.Links.Distinct().ToList();
                _nodeStore.Save(node);
            }
        }
    }
}
