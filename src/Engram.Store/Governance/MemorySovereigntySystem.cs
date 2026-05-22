using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Engram.Store.Wiki;
using Engram.Store.Salience;

namespace Engram.Store.Governance;

/// <summary>
/// A structural absence marker preserved in timelines/histories to maintain chronological coherence
/// without retaining any deleted semantic content.
/// </summary>
public class HistoricalDeletionEnvelope
{
    public string OriginalNodeId { get; set; } = string.Empty;
    public string OriginalTitle { get; set; } = string.Empty;
    public WikiNodeType OriginalType { get; set; }
    public DateTimeOffset DeletedAt { get; set; } = DateTimeOffset.UtcNow;
    public string PlaceholderText { get; set; } = "Referenced entity removed by user.";
}

/// <summary>
/// System managing memory retention, deletion envelopes, disputes, and structural forgetting.
/// </summary>
public class MemorySovereigntySystem
{
    private readonly WikiNodeStore _nodeStore;
    private readonly string _envelopesFilePath;
    private readonly List<HistoricalDeletionEnvelope> _envelopes = new();
    private readonly object _lock = new();
    private readonly DriftAlertStore? _driftStore;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public MemorySovereigntySystem(WikiNodeStore nodeStore, WorkspacePaths paths, DriftAlertStore? driftStore = null)
    {
        _nodeStore = nodeStore;
        _driftStore = driftStore;
        var dir = Path.Combine(paths.Config, "governance");
        Directory.CreateDirectory(dir);
        _envelopesFilePath = Path.Combine(dir, "deletion_envelopes.json");
        LoadEnvelopes();
    }

    public void Forget(string nodeId)
    {
        var node = _nodeStore.Load(nodeId);
        if (node == null) return;

        // 1. Delete actual node file
        _nodeStore.Delete(nodeId);

        // 2. Create historical deletion envelope
        var envelope = new HistoricalDeletionEnvelope
        {
            OriginalNodeId = nodeId,
            OriginalTitle = node.Title,
            OriginalType = node.NodeType
        };

        lock (_lock)
        {
            _envelopes.Add(envelope);
            SaveEnvelopes();
        }

        // 3. Clean up the graph (edges, links, claims, drift alerts)
        var allNodes = _nodeStore.LoadAll();
        foreach (var otherNode in allNodes)
        {
            bool modified = false;

            // Remove edges pointing to deleted node
            var remainingEdges = otherNode.Edges.Where(e => !string.Equals(e.TargetNodeId, nodeId, StringComparison.OrdinalIgnoreCase)).ToList();
            if (remainingEdges.Count != otherNode.Edges.Count)
            {
                otherNode.Edges = remainingEdges;
                modified = true;
            }

            // Remove links pointing to deleted node
            if (otherNode.Links.Contains(nodeId))
            {
                otherNode.Links.Remove(nodeId);
                modified = true;
            }

            // Remove semantic claims related to this node
            var remainingClaims = otherNode.Claims.Where(c => 
                !string.Equals(c.Value, nodeId, StringComparison.OrdinalIgnoreCase) && 
                !string.Equals(c.Context, nodeId, StringComparison.OrdinalIgnoreCase)).ToList();
            if (remainingClaims.Count != otherNode.Claims.Count)
            {
                otherNode.Claims = remainingClaims;
                modified = true;
            }

            if (modified)
            {
                _nodeStore.Save(otherNode);
            }
        }

        // 4. Run propagation reconciliation pass
        RunReconciliationPass(nodeId);
    }

    public bool IsDeleted(string nodeId)
    {
        lock (_lock)
        {
            return _envelopes.Any(e => string.Equals(e.OriginalNodeId, nodeId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public HistoricalDeletionEnvelope? GetEnvelope(string nodeId)
    {
        lock (_lock)
        {
            return _envelopes.FirstOrDefault(e => string.Equals(e.OriginalNodeId, nodeId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public IReadOnlyList<HistoricalDeletionEnvelope> GetAllEnvelopes()
    {
        lock (_lock)
        {
            return _envelopes.ToList();
        }
    }

    public void EnforceRetention(GovernanceConfig config)
    {
        var allNodes = _nodeStore.LoadAll();
        var now = DateTimeOffset.UtcNow;

        foreach (var node in allNodes)
        {
            // Determine domain type based on node type
            string domain = node.NodeType switch
            {
                WikiNodeType.Workflow => "workflows",
                WikiNodeType.BrowserTab => "browsing",
                WikiNodeType.TimelineSession => "browsing",
                WikiNodeType.Decision => "personal_reflections",
                _ => string.Empty
            };

            if (string.IsNullOrEmpty(domain)) continue;

            var policy = config.RetentionPolicies.FirstOrDefault(p => string.Equals(p.Domain, domain, StringComparison.OrdinalIgnoreCase));
            if (policy != null && policy.AutoExpire)
            {
                var age = now - node.LastTouchedAt;
                if (age > policy.RetentionWindow)
                {
                    Forget(node.NodeId);
                }
            }
        }
    }

    private void RunReconciliationPass(string deletedNodeId)
    {
        // 1. Orphan Detection & Rebalancing
        var allNodes = _nodeStore.LoadAll();
        foreach (var node in allNodes)
        {
            // If a node is an orphan (has no edges or links), decay salience or normalize it
            if (!node.Edges.Any() && !node.Links.Any() && node.NodeType != WikiNodeType.Person)
            {
                node.Salience = Math.Max(0.1, node.Salience * 0.5); // Decay orphan salience faster
                _nodeStore.Save(node);
            }
        }

        // 2. Global Salience Normalization
        double totalSalience = allNodes.Sum(n => n.Salience);
        if (totalSalience > 0 && allNodes.Any())
        {
            // Keep overall salience balanced to prevent runaway propagation in the remaining nodes
            double mean = totalSalience / allNodes.Count;
            if (mean > 2.0)
            {
                foreach (var node in allNodes)
                {
                    node.Salience = node.Salience / mean;
                    _nodeStore.Save(node);
                }
            }
        }
    }

    private void LoadEnvelopes()
    {
        lock (_lock)
        {
            if (!File.Exists(_envelopesFilePath)) return;
            try
            {
                var json = File.ReadAllText(_envelopesFilePath);
                var loaded = JsonSerializer.Deserialize<List<HistoricalDeletionEnvelope>>(json, JsonOptions);
                if (loaded != null)
                {
                    _envelopes.Clear();
                    _envelopes.AddRange(loaded);
                }
            }
            catch
            {
                // Fallback
            }
        }
    }

    private void SaveEnvelopes()
    {
        lock (_lock)
        {
            try
            {
                var tmpPath = _envelopesFilePath + ".tmp";
                var json = JsonSerializer.Serialize(_envelopes, JsonOptions);
                File.WriteAllText(tmpPath, json);
                File.Move(tmpPath, _envelopesFilePath, overwrite: true);
            }
            catch
            {
                // Fallback
            }
        }
    }
}
