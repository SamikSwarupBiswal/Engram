using System;
using System.Collections.Generic;

namespace Engram.Store.Governance;

public class ExplainabilityNarrativeEngine
{
    public string GenerateActionExplanation(ReasonTrace trace)
    {
        return DecisionNarrator.Narrate(trace);
    }

    public string ExplainStateTransition(ConstitutionalState fromState, ConstitutionalState toState, string reason)
    {
        return toState switch
        {
            ConstitutionalState.Operational => $"System returned to fully functional state: {reason}",
            ConstitutionalState.Restrained => $"Engram has limited its background alert frequency to match your active focus session.",
            ConstitutionalState.Degraded => $"Some operations have been paused to save system resources or resolve minor data conflicts.",
            ConstitutionalState.Quarantine => $"Write access is restricted to ensure privacy boundary containment.",
            ConstitutionalState.Frozen => $"All operations are suspended to protect your system following a critical security anomaly: '{reason}'. Please supply an override note to resume.",
            _ => $"System state shifted from {fromState} to {toState} due to: {reason}."
        };
    }

    public string ExplainActiveDegradations(IReadOnlyDictionary<string, string> degradations)
    {
        if (degradations == null || degradations.Count == 0)
        {
            return "All cognitive systems are running normally at full fidelity.";
        }

        var explanations = new List<string>();
        foreach (var entry in degradations)
        {
            var cleanExplanation = entry.Key switch
            {
                "SafeModeActive" => "System running in read-only Safe Mode to preserve database integrity.",
                "ThermalThrottling" => "Metabolic Cadence throttled due to detected processor heat.",
                "BatterySaver" => "Inference rate restricted to preserve laptop battery life.",
                "HeavyWorkload" => "Background cognition deferred to yield CPU capacity to foreground apps.",
                "WakeStabilizing" => "Engram is stabilizing after sleep mode wake.",
                _ => $"{entry.Key}: {entry.Value}"
            };
            explanations.Add(cleanExplanation);
        }

        return string.Join(" · ", explanations);
    }
}
