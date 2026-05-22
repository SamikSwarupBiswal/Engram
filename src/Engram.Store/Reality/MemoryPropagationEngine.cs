using System;
using System.Collections.Generic;
using Engram.Store.Wiki;

namespace Engram.Store.Reality;

/// <summary>
/// Memory Propagation Engine manages geometric salience/attention propagation along graph edges.
/// Attenuates influence based on distance, edge weights, and type-specific rules.
/// Uses AttentionStormGuard to prevent loops.
/// </summary>
public class MemoryPropagationEngine
{
    private readonly WikiNodeStore _nodeStore;
    private readonly GlobalAttentionOrchestrator _orchestrator;
    private readonly AttentionStormGuard _stormGuard;

    public MemoryPropagationEngine(
        WikiNodeStore nodeStore, 
        GlobalAttentionOrchestrator orchestrator, 
        AttentionStormGuard stormGuard)
    {
        _nodeStore = nodeStore ?? throw new ArgumentNullException(nameof(nodeStore));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _stormGuard = stormGuard ?? throw new ArgumentNullException(nameof(stormGuard));
    }

    /// <summary>
    /// Propagates attention from a starting node to connected nodes recursively (up to MaxDepth).
    /// </summary>
    public void Propagate(string startNodeId, double initialSalience)
    {
        if (string.IsNullOrEmpty(startNodeId) || initialSalience <= 0.0) return;

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<PropagationState>();
        
        queue.Enqueue(new PropagationState 
        { 
            NodeId = startNodeId, 
            SalienceValue = initialSalience, 
            Depth = 0 
        });

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            
            if (visited.Contains(current.NodeId)) continue;
            visited.Add(current.NodeId);

            // Fetch the current node to look up edges
            var node = _nodeStore.Load(current.NodeId);
            if (node == null) continue;

            // Only record attention in orchestrator for target nodes (starting node is already updated)
            if (!current.NodeId.Equals(startNodeId, StringComparison.OrdinalIgnoreCase))
            {
                _orchestrator.RecordAttention(current.NodeId, current.SalienceValue);
                
                // Update and save the node's salience field directly on disk to keep it in sync
                node.Salience = _orchestrator.GetAttention(current.NodeId);
                node.LastTouchedAt = DateTimeOffset.UtcNow;
                _nodeStore.Save(node);
            }

            // Propagate along edges
            foreach (var edge in node.Edges)
            {
                if (string.IsNullOrEmpty(edge.TargetNodeId)) continue;

                // 1. Guard check
                if (!_stormGuard.AllowPropagation(current.NodeId, edge.TargetNodeId, current.Depth)) continue;

                // 2. Determine type-specific modifier
                double typeModifier = GetTypeModifier(edge.PropagationType);

                // 3. Geometric attenuation
                double propagatedValue = current.SalienceValue * edge.PropagationWeight * typeModifier;

                // 4. Caps and thresholds
                if (propagatedValue > edge.MaxInfluence)
                {
                    propagatedValue = edge.MaxInfluence;
                }

                if (propagatedValue < edge.EvidenceThreshold)
                {
                    continue; // Skip because it is below the node's threshold
                }

                // 5. Enqueue and record
                queue.Enqueue(new PropagationState
                {
                    NodeId = edge.TargetNodeId,
                    SalienceValue = propagatedValue,
                    Depth = current.Depth + 1
                });

                // Update edge timestamp
                edge.LastPropagatedAt = DateTimeOffset.UtcNow;

                // Record the propagation step in the storm guard
                _stormGuard.RecordPropagation(current.NodeId, edge.TargetNodeId, propagatedValue);
            }

            // Persist the updated edge metadata
            _nodeStore.Save(node);
        }
    }

    private static double GetTypeModifier(string propagationType)
    {
        return (propagationType?.ToLowerInvariant()) switch
        {
            "operational" => 1.0,     // fast decay, normal propagation
            "identity" => 0.8,        // slow decay, slightly attenuated propagation
            "emotional" => 0.2,       // highly throttled/attenuated propagation
            _ => 0.5                  // fallback default modifier
        };
    }

    private class PropagationState
    {
        public required string NodeId { get; set; }
        public double SalienceValue { get; set; }
        public int Depth { get; set; }
    }
}
