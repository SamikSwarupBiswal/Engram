using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Engram.Store.Wiki;

namespace Engram.Store.Governance;

/// <summary>
/// Preservation model representing user-disputed counterfactual claims.
/// Keeps track of what Engram thought vs what was the true human state.
/// </summary>
public class CounterfactualCorrection
{
    public string DisputeId { get; set; } = Guid.NewGuid().ToString("n");
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string NodeId { get; set; } = string.Empty;
    public string Property { get; set; } = string.Empty;
    public string DisputedValue { get; set; } = string.Empty;
    public string CorrectedValue { get; set; } = string.Empty;
    public double InferredConfidence { get; set; }
}

/// <summary>
/// Reality Correction Layer: processes user disputes, rolls back false interpretations,
/// and stores counterfactual corrections.
/// </summary>
public class RealityCorrectionLayer
{
    private readonly WikiNodeStore _nodeStore;
    private readonly string _disputesFilePath;
    private readonly List<CounterfactualCorrection> _corrections = new();
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public RealityCorrectionLayer(WikiNodeStore nodeStore, WorkspacePaths paths)
    {
        _nodeStore = nodeStore;
        var dir = Path.Combine(paths.Config, "governance");
        Directory.CreateDirectory(dir);
        _disputesFilePath = Path.Combine(dir, "disputes.json");
        LoadCorrections();
    }

    /// <summary>
    /// Processes a user dispute against a specific node claim.
    /// Downgrades inference confidence, injects corrected claim, and recalculates narrative.
    /// </summary>
    public void DisputeClaim(string nodeId, string claimId, string correctedValue)
    {
        var node = _nodeStore.Load(nodeId);
        if (node == null) return;

        var claim = node.Claims.FirstOrDefault(c => string.Equals(c.ClaimId, claimId, StringComparison.OrdinalIgnoreCase));
        if (claim == null) return;

        // 1. Store counterfactual record for alignment calibration
        var correction = new CounterfactualCorrection
        {
            NodeId = nodeId,
            Property = claim.Property,
            DisputedValue = claim.Value,
            CorrectedValue = correctedValue,
            InferredConfidence = claim.Confidence
        };

        lock (_lock)
        {
            _corrections.Add(correction);
            SaveCorrections();
        }

        // 2. Downgrade prior claim confidence to 0
        claim.Confidence = 0.0;

        // 3. Inject explicit user claim to serve as future grounding truth
        var userClaim = new SemanticClaim
        {
            ClaimId = "uc_" + Guid.NewGuid().ToString("n")[..8],
            Property = claim.Property,
            Value = correctedValue,
            Confidence = 1.0,
            Source = "user_statement",
            Timestamp = DateTimeOffset.UtcNow
        };
        node.Claims.Add(userClaim);
        
        // Downgrade global node confidence due to dispute friction
        node.Confidence = Math.Max(0.1, node.Confidence - 0.2);

        _nodeStore.Save(node);

        // 4. Trigger Narrative Rollback System to re-stabilize surrounding nodes
        RollbackNarrative(nodeId, claim.Property);
    }

    public IReadOnlyList<CounterfactualCorrection> GetCorrections()
    {
        lock (_lock) { return _corrections.ToList(); }
    }

    private void RollbackNarrative(string nodeId, string property)
    {
        // Re-stabilize: Scan neighboring nodes linked to this node
        var allNodes = _nodeStore.LoadAll();
        foreach (var otherNode in allNodes)
        {
            if (otherNode.Links.Contains(nodeId))
            {
                // Downgrade salience propagation from the disputed node
                var edge = otherNode.Edges.FirstOrDefault(e => string.Equals(e.TargetNodeId, nodeId, StringComparison.OrdinalIgnoreCase));
                if (edge != null)
                {
                    edge.PropagationWeight = Math.Max(0.05, edge.PropagationWeight * 0.5); // Decay propagation path
                }

                // If neighboring nodes had claims matching the disputed property/value derived from this, revert them
                var derivativeClaims = otherNode.Claims.Where(c => 
                    string.Equals(c.Property, property, StringComparison.OrdinalIgnoreCase) && 
                    string.Equals(c.Source, "inferred_inactivity", StringComparison.OrdinalIgnoreCase)).ToList();

                foreach (var dc in derivativeClaims)
                {
                    dc.Confidence = 0.1; // Substantially downgrade derivative claim confidence
                }

                _nodeStore.Save(otherNode);
            }
        }
    }

    private void LoadCorrections()
    {
        lock (_lock)
        {
            if (!File.Exists(_disputesFilePath)) return;
            try
            {
                var json = File.ReadAllText(_disputesFilePath);
                var loaded = JsonSerializer.Deserialize<List<CounterfactualCorrection>>(json, JsonOptions);
                if (loaded != null)
                {
                    _corrections.Clear();
                    _corrections.AddRange(loaded);
                }
            }
            catch { }
        }
    }

    private void SaveCorrections()
    {
        lock (_lock)
        {
            try
            {
                var tmpPath = _disputesFilePath + ".tmp";
                File.WriteAllText(tmpPath, JsonSerializer.Serialize(_corrections, JsonOptions));
                File.Move(tmpPath, _disputesFilePath, overwrite: true);
            }
            catch { }
        }
    }
}
