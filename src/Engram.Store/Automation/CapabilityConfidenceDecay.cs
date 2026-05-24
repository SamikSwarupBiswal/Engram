using System;
using System.Collections.Concurrent;

namespace Engram.Store.Automation;

public class CapabilityConfidenceDecay
{
    private readonly ConcurrentDictionary<string, double> _confidenceStore = new(StringComparer.OrdinalIgnoreCase);

    public double GetAppCapabilityConfidence(string appName, string appVersion)
    {
        var key = $"{appName}_{appVersion}";
        return _confidenceStore.GetOrAdd(key, 1.0);
    }

    public void RecordOperationResult(string appName, string appVersion, bool success)
    {
        var key = $"{appName}_{appVersion}";
        _confidenceStore.AddOrUpdate(key,
            success ? 1.0 : 0.8, // Initial value
            (k, oldConfidence) => 
            {
                if (success)
                {
                    // Slowly recover confidence
                    return Math.Min(1.0, oldConfidence + 0.05);
                }
                else
                {
                    // Decay rapidly on failure
                    return Math.Max(0.1, oldConfidence - 0.2);
                }
            });
    }

    public void MarkVersionChanged(string appName)
    {
        // Decay all versions for this app as it was mutated
        foreach (var key in _confidenceStore.Keys)
        {
            if (key.StartsWith(appName + "_", StringComparison.OrdinalIgnoreCase))
            {
                _confidenceStore[key] = 0.5; // Mutated environment penalty
            }
        }
    }
}
