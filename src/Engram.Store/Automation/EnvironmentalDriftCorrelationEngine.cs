using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Engram.Store.Automation;

public enum DriftType
{
    LocalProcedural,
    UserTurbulence,
    Environmental,
    Semantic,
    ResourcePressure,
    NetworkInstability,
    GovernanceCollapse
}

public class DriftObservation
{
    public string StepId { get; set; } = string.Empty;
    public string AppScope { get; set; } = string.Empty;
    public DriftType Type { get; set; }
    public double DriftValue { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

public class EnvironmentalDriftCorrelationEngine
{
    private readonly List<DriftObservation> _observations = new();
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<string, double> _isolationZoneConfidence = new(StringComparer.OrdinalIgnoreCase);

    public double SystemicThreshold { get; set; } = 0.6;

    public void RecordObservation(string stepId, string appScope, DriftType type, double driftValue)
    {
        lock (_lock)
        {
            _observations.Add(new DriftObservation
            {
                StepId = stepId,
                AppScope = appScope ?? "Default",
                Type = type,
                DriftValue = driftValue,
                Timestamp = DateTimeOffset.UtcNow
            });

            DegradeConfidenceForScope(appScope ?? "Default", type, driftValue);
        }
    }

    private void DegradeConfidenceForScope(string scope, DriftType type, double value)
    {
        double current = _isolationZoneConfidence.GetOrAdd(scope, 1.0);
        double degradationFactor = type switch
        {
            DriftType.Environmental => 0.3 * value,
            DriftType.Semantic => 0.25 * value,
            DriftType.ResourcePressure => 0.2 * value,
            DriftType.NetworkInstability => 0.15 * value,
            DriftType.UserTurbulence => 0.1 * value,
            _ => 0.05 * value
        };

        double updated = Math.Max(0.0, current - degradationFactor);
        _isolationZoneConfidence[scope] = updated;
    }

    public double GetScopeConfidence(string appScope)
    {
        return _isolationZoneConfidence.TryGetValue(appScope ?? "Default", out var val) ? val : 1.0;
    }

    public void ResetScopeConfidence(string appScope)
    {
        _isolationZoneConfidence[appScope ?? "Default"] = 1.0;
    }

    public double CalculateSystemicDriftIndex(string appScope)
    {
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow;
            var recent = _observations
                .Where(o => (now - o.Timestamp).TotalMinutes <= 5 && o.AppScope.Equals(appScope, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (recent.Count == 0) return 0.0;

            double totalDriftWeight = 0;
            foreach (var obs in recent)
            {
                double weight = obs.Type switch
                {
                    DriftType.Environmental => 1.0,
                    DriftType.Semantic => 0.8,
                    DriftType.ResourcePressure => 0.6,
                    DriftType.NetworkInstability => 0.5,
                    DriftType.UserTurbulence => 0.2,
                    _ => 0.3
                };
                totalDriftWeight += obs.DriftValue * weight;
            }

            return Math.Min(1.0, totalDriftWeight / Math.Max(3, recent.Count));
        }
    }

    public bool ShouldRecalibrate(string appScope)
    {
        return CalculateSystemicDriftIndex(appScope) >= SystemicThreshold;
    }
}
