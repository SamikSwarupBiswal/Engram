using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Engram.Store.Reality;

/// <summary>
/// Intercepts salience propagation events to enforce cooldowns, prevent runaway self-reinforcing loops,
/// and shield the graph against infinite activation cascades (attention storms).
/// </summary>
public class AttentionStormGuard
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastPropagatedTime = new();
    private readonly ConcurrentDictionary<string, double> _accumulatedSalienceChange = new();
    
    // Cooldown duration to prevent immediate repeat propagation
    public TimeSpan RefractoryCooldown { get; set; } = TimeSpan.FromSeconds(5);
    
    // Maximum propagation depth for any single wave
    public int MaxPropagationDepth { get; set; } = 4;
    
    // Max salience increment allowed on any single node within a short sliding window
    public double MaxIncrementalCap { get; set; } = 0.9;

    /// <summary>
    /// Checks if a propagation step from source to target is allowed under current guard rules.
    /// </summary>
    public bool AllowPropagation(string sourceNodeId, string targetNodeId, int currentDepth)
    {
        if (string.IsNullOrEmpty(sourceNodeId) || string.IsNullOrEmpty(targetNodeId)) return false;
        
        // 1. Prevent circular loop (self-propagation)
        if (sourceNodeId.Equals(targetNodeId, StringComparison.OrdinalIgnoreCase)) return false;

        // 2. Enforce maximum recursion depth
        if (currentDepth > MaxPropagationDepth) return false;

        // 3. Enforce refractory cooldown on the source node (throttles rapid sequential triggers)
        var now = DateTimeOffset.UtcNow;
        if (_lastPropagatedTime.TryGetValue(sourceNodeId, out var lastTime))
        {
            if (now - lastTime < RefractoryCooldown)
            {
                return false; // In refractory period
            }
        }

        return true;
    }

    /// <summary>
    /// Records that propagation has occurred, updating the refractory timers.
    /// </summary>
    public void RecordPropagation(string sourceNodeId, string targetNodeId, double amount)
    {
        var now = DateTimeOffset.UtcNow;
        _lastPropagatedTime[sourceNodeId] = now;
        
        // Track accumulated change on target
        _accumulatedSalienceChange.AddOrUpdate(targetNodeId, 
            id => amount,
            (id, current) => {
                // Decay the accumulated change if time has elapsed
                if (_lastPropagatedTime.TryGetValue(id, out var lastTargetTime) && (now - lastTargetTime).TotalSeconds > 10)
                {
                    return amount;
                }
                return Math.Min(MaxIncrementalCap, current + amount);
            });
    }

    /// <summary>
    /// Resets all guard state.
    /// </summary>
    public void Reset()
    {
        _lastPropagatedTime.Clear();
        _accumulatedSalienceChange.Clear();
    }
}
