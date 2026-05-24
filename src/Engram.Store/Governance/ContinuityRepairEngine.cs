using System;
using System.Collections.Generic;
using System.Linq;
using Engram.Store.Wiki;

namespace Engram.Store.Governance;

public class RepairResult
{
    public int BrokenEdgesFixed { get; set; }
    public int OrphanedSalienceDecayed { get; set; }
    public int DuplicateClaimsRemoved { get; set; }
    public int InvalidEdgesRemoved { get; set; }
}

public class ContinuityRepairEngine
{
    private readonly WikiNodeStore _nodeStore;

    public ContinuityRepairEngine(WikiNodeStore nodeStore)
    {
        _nodeStore = nodeStore;
    }

    public RepairResult RunRepair()
    {
        var result = new RepairResult();
        var allNodes = _nodeStore.LoadAll().ToList();
        var nodeIds = allNodes.Select(n => n.NodeId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var node in allNodes)
        {
            bool modified = false;

            // 1. Remove edges that point to non-existent target nodes
            var validEdges = new List<WikiEdge>();
            foreach (var edge in node.Edges)
            {
                if (nodeIds.Contains(edge.TargetNodeId))
                {
                    validEdges.Add(edge);
                }
                else
                {
                    result.BrokenEdgesFixed++;
                    modified = true;
                }
            }
            if (modified)
            {
                node.Edges = validEdges;
            }

            // 2. Remove broken elements from Links list
            var originalLinksCount = node.Links.Count;
            node.Links = node.Links.Where(l => nodeIds.Contains(l)).ToList();
            if (node.Links.Count != originalLinksCount)
            {
                result.BrokenEdgesFixed += (originalLinksCount - node.Links.Count);
                modified = true;
            }

            // 3. Remove duplicate claims
            var uniqueClaims = new List<SemanticClaim>();
            var claimKeys = new HashSet<string>();
            foreach (var claim in node.Claims)
            {
                var key = $"{claim.Property}:{claim.Value}";
                if (claimKeys.Add(key))
                {
                    uniqueClaims.Add(claim);
                }
                else
                {
                    result.DuplicateClaimsRemoved++;
                    modified = true;
                }
            }
            if (modified)
            {
                node.Claims = uniqueClaims;
            }

            // 4. Decay salience of orphaned concept nodes
            if (node.NodeType == WikiNodeType.Concept && !node.Links.Any() && !node.Edges.Any())
            {
                var targets = allNodes.SelectMany(n => n.Links.Concat(n.Edges.Select(e => e.TargetNodeId))).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (!targets.Contains(node.NodeId))
                {
                    // Orphaned concept: decay salience aggressively
                    var oldSalience = node.Salience;
                    node.Salience = Math.Max(0.05, node.Salience * 0.4);
                    if (Math.Abs(oldSalience - node.Salience) > 0.01)
                    {
                        result.OrphanedSalienceDecayed++;
                        modified = true;
                    }
                }
            }

            if (modified)
            {
                _nodeStore.Save(node);
            }
        }

        return result;
    }
}
