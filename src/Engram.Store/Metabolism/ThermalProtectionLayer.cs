using System;
using System.Collections.Generic;
using System.Linq;

namespace Engram.Store.Metabolism;

public class ThermalProtectionLayer
{
    private readonly List<double> _inferenceLatencyHistoryMs = new();
    private readonly object _lock = new();
    private const double BaselineMsPerToken = 45.0; // Expected baseline for typical performance
    private const int MaxHistory = 50;

    public bool IsThrottlingDetected { get; private set; }
    public double CurrentMsPerToken { get; private set; } = BaselineMsPerToken;

    public void RecordInferenceStats(int tokenCount, double durationMs)
    {
        if (tokenCount <= 0 || durationMs <= 0) return;

        double msPerToken = durationMs / tokenCount;

        lock (_lock)
        {
            CurrentMsPerToken = msPerToken;
            _inferenceLatencyHistoryMs.Add(msPerToken);
            if (_inferenceLatencyHistoryMs.Count > MaxHistory)
            {
                _inferenceLatencyHistoryMs.RemoveAt(0);
            }

            // Behavioral Thermal Inference:
            // If ms per token is consistently double the baseline, infer thermal throttling or high load.
            var recentInferences = _inferenceLatencyHistoryMs.TakeLast(5).ToList();
            if (recentInferences.Count >= 3)
            {
                var averageRecent = recentInferences.Average();
                IsThrottlingDetected = averageRecent > (BaselineMsPerToken * 2.2);
            }
            else
            {
                IsThrottlingDetected = msPerToken > (BaselineMsPerToken * 2.5);
            }
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _inferenceLatencyHistoryMs.Clear();
            IsThrottlingDetected = false;
            CurrentMsPerToken = BaselineMsPerToken;
        }
    }
}
