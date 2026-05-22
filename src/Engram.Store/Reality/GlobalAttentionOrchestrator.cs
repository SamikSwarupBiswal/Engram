using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Engram.Store.Wiki;

namespace Engram.Store.Reality;

/// <summary>
/// Tracks attention (salience) scores dynamically with exponential time-decay.
/// Categorizes nodes into active, stale, or requiring intervention states.
/// </summary>
public class GlobalAttentionOrchestrator
{
    private readonly WikiNodeStore _nodeStore;
    private readonly ConcurrentDictionary<string, double> _attentionScores = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastUpdated = new();
    
    // Half-life in seconds (default is 600 seconds = 10 minutes)
    public double AttentionHalfLifeSeconds { get; set; } = 600.0;
    
    // Active threshold: >= 0.7
    public double ActiveThreshold { get; set; } = 0.7;
    
    // Stale threshold: <= 0.2
    public double StaleThreshold { get; set; } = 0.2;

    public GlobalAttentionOrchestrator(WikiNodeStore nodeStore)
    {
        _nodeStore = nodeStore ?? throw new ArgumentNullException(nameof(nodeStore));
    }

    /// <summary>
    /// Records attention (activation) on a node.
    /// Increases attention score up to a maximum of 1.0.
    /// </summary>
    public void RecordAttention(string nodeId, double score)
    {
        if (string.IsNullOrEmpty(nodeId)) return;

        var now = DateTimeOffset.UtcNow;
        _attentionScores.AddOrUpdate(nodeId, 
            id => {
                _lastUpdated[id] = now;
                return Math.Min(1.0, Math.Max(0.0, score));
            },
            (id, current) => {
                // Apply decay before adding new score
                var decayed = GetDecayedScore(id, current, now);
                _lastUpdated[id] = now;
                return Math.Min(1.0, Math.Max(0.0, decayed + score));
            });
    }

    /// <summary>
    /// Gets the current decayed attention score for a node.
    /// </summary>
    public double GetAttention(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId)) return 0.0;
        if (!_attentionScores.TryGetValue(nodeId, out var score))
        {
            // Fallback to node's saved salience if available
            var node = _nodeStore.Load(nodeId);
            return node?.Salience ?? 0.0;
        }

        return GetDecayedScore(nodeId, score, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Force-sets the attention score for a node (e.g. from loaded salience).
    /// </summary>
    public void SetAttention(string nodeId, double score)
    {
        if (string.IsNullOrEmpty(nodeId)) return;
        var now = DateTimeOffset.UtcNow;
        _attentionScores[nodeId] = Math.Min(1.0, Math.Max(0.0, score));
        _lastUpdated[nodeId] = now;
    }

    /// <summary>
    /// Decays all tracked attention scores.
    /// </summary>
    public void DecayAll()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var key in _attentionScores.Keys)
        {
            _attentionScores.AddOrUpdate(key, 
                id => 0.0,
                (id, current) => GetDecayedScore(id, current, now));
            _lastUpdated[key] = now;
        }
    }

    /// <summary>
    /// Returns the lists of nodes categorized by their attention state.
    /// </summary>
    public AttentionStateSummary GetAttentionSummary()
    {
        var now = DateTimeOffset.UtcNow;
        var allNodes = _nodeStore.LoadAll();
        
        var active = new List<string>();
        var stale = new List<string>();
        var requiresIntervention = new List<string>();

        foreach (var node in allNodes)
        {
            var score = GetAttention(node.NodeId);
            
            if (score >= ActiveThreshold)
            {
                active.Add(node.NodeId);
            }
            else if (score <= StaleThreshold)
            {
                stale.Add(node.NodeId);
                
                // Intervention logic: if it's an important entity (e.g., Project or Goal) 
                // but its attention has decayed and it has low confidence or open questions
                if ((node.NodeType == WikiNodeType.Goal || node.NodeType == WikiNodeType.Project) && 
                    (node.Confidence < 0.6 || node.OpenQuestions.Any()))
                {
                    requiresIntervention.Add(node.NodeId);
                }
            }
        }

        return new AttentionStateSummary
        {
            ActiveNodeIds = active,
            StaleNodeIds = stale,
            RequiresInterventionNodeIds = requiresIntervention
        };
    }

    private double GetDecayedScore(string nodeId, double currentScore, DateTimeOffset now)
    {
        if (!_lastUpdated.TryGetValue(nodeId, out var lastTime))
        {
            return currentScore;
        }

        var elapsedSeconds = (now - lastTime).TotalSeconds;
        if (elapsedSeconds <= 0) return currentScore;

        // Exponential decay: score * 0.5 ^ (elapsed / half-life)
        double lambda = Math.Log(2) / AttentionHalfLifeSeconds;
        double decayed = currentScore * Math.Exp(-lambda * elapsedSeconds);

        return Math.Max(0.0, decayed);
    }
}

public class AttentionStateSummary
{
    public List<string> ActiveNodeIds { get; set; } = new();
    public List<string> StaleNodeIds { get; set; } = new();
    public List<string> RequiresInterventionNodeIds { get; set; } = new();
}
