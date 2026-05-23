using System;
using System.Collections.Concurrent;

namespace Engram.Store.Governance;

/// <summary>
/// Manages Time-To-Live (TTL) for safety overrides and directory whitelists.
/// </summary>
public class OverrideExpiryManager
{
    private readonly TrustCalibrationEngine _trustEngine;
    private readonly ConstitutionalStateMachine _stateMachine;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _temporaryOverrides = new();
    private readonly object _lock = new();

    public OverrideExpiryManager(
        TrustCalibrationEngine trustEngine,
        ConstitutionalStateMachine stateMachine)
    {
        _trustEngine = trustEngine ?? throw new ArgumentNullException(nameof(trustEngine));
        _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
    }

    /// <summary>
    /// Add or renew a temporary safety override (e.g. "bypass_safety") with a TTL.
    /// </summary>
    public void RegisterOverride(string key, TimeSpan ttl)
    {
        _temporaryOverrides[key] = DateTimeOffset.UtcNow.Add(ttl);
    }

    /// <summary>
    /// Check if a specific temporary safety override is active.
    /// </summary>
    public bool IsOverrideActive(string key)
    {
        if (_temporaryOverrides.TryGetValue(key, out var expiry))
        {
            if (DateTimeOffset.UtcNow < expiry)
            {
                return true;
            }
            // Remove if expired
            _temporaryOverrides.TryRemove(key, out _);
        }
        return false;
    }

    /// <summary>
    /// Scans and prunes expired overrides and triggers permission decay.
    /// </summary>
    public void CheckAndExpireOverrides()
    {
        lock (_lock)
        {
            // 1. Decay permissions inside TrustCalibrationEngine
            _trustEngine.DecayPermissions();

            // 2. Clean up temporary overrides
            var now = DateTimeOffset.UtcNow;
            foreach (var kvp in _temporaryOverrides)
            {
                if (now >= kvp.Value)
                {
                    _temporaryOverrides.TryRemove(kvp.Key, out _);
                }
            }
        }
    }
}
