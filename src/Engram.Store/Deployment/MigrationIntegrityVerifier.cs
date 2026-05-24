using System;
using System.Collections.Generic;
using System.Linq;
using Engram.Store.Wiki;

namespace Engram.Store.Deployment;

public class MigrationIntegrityReport
{
    public bool IsValid { get; set; } = true;
    public int ScannedNodesCount { get; set; }
    public int OrphanedNodesCount { get; set; }
    public int BrokenLinksCount { get; set; }
    public int DuplicateClaimsCount { get; set; }
    public List<string> ValidationErrors { get; set; } = new();
}

public class MigrationIntegrityVerifier
{
    public MigrationIntegrityReport Verify(WikiNodeStore store)
    {
        var report = new MigrationIntegrityReport();
        var allNodes = store.LoadAll().ToList();
        report.ScannedNodesCount = allNodes.Count;

        var nodeIds = allNodes.Select(n => n.NodeId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var node in allNodes)
        {
            // 1. Broken link checking
            foreach (var link in node.Links)
            {
                if (!nodeIds.Contains(link))
                {
                    report.BrokenLinksCount++;
                    report.ValidationErrors.Add($"Node '{node.NodeId}' references missing link target '{link}'.");
                }
            }

            // 2. Broken edge target checking
            foreach (var edge in node.Edges)
            {
                if (!nodeIds.Contains(edge.TargetNodeId))
                {
                    report.BrokenLinksCount++;
                    report.ValidationErrors.Add($"Node '{node.NodeId}' contains edge targeting missing target '{edge.TargetNodeId}'.");
                }
            }

            // 3. Duplicate claims checking
            var claimKeys = new HashSet<string>();
            foreach (var claim in node.Claims)
            {
                var key = $"{claim.Property}:{claim.Value}";
                if (!claimKeys.Add(key))
                {
                    report.DuplicateClaimsCount++;
                }
            }

            // 4. Verification of basic structures
            if (string.IsNullOrWhiteSpace(node.Title))
            {
                report.ValidationErrors.Add($"Node '{node.NodeId}' has empty or invalid title.");
            }
        }

        // 5. Orphaned non-concept node reporting
        var targets = allNodes.SelectMany(n => n.Links.Concat(n.Edges.Select(e => e.TargetNodeId))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var node in allNodes)
        {
            if (node.NodeType != WikiNodeType.Person && node.NodeType != WikiNodeType.Project && !targets.Contains(node.NodeId) && !node.Links.Any() && !node.Edges.Any())
            {
                report.OrphanedNodesCount++;
            }
        }

        // If there are critical broken connections, mark invalid
        if (report.BrokenLinksCount > 10 || report.ValidationErrors.Any(e => e.Contains("invalid title")))
        {
            report.IsValid = false;
        }

        return report;
    }
}
