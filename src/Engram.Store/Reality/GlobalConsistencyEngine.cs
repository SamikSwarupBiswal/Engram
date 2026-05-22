using System;
using System.Collections.Generic;
using System.Linq;
using Engram.Store.Wiki;

namespace Engram.Store.Reality;

/// <summary>
/// Reasons over the Claim Ecology (competing claims, temporal weights, source quality weights).
/// Detects semantic tension and requests escalation when execution bounds are breached.
/// </summary>
public class GlobalConsistencyEngine
{
    public double ClaimDecayHalfLifeSeconds { get; set; } = 86400.0; // 24 hours default
    public double TensionEscalationThreshold { get; set; } = 0.5;
    public double ConfidenceCollapseThreshold { get; set; } = 0.3;

    /// <summary>
    /// Analyzes the claims of a node to find dominant values, calculate property tension,
    /// and generate escalation requests if tension violates confidence or execution bounds.
    /// </summary>
    public ConsistencyAnalysis AnalyzeNode(WikiNode node, bool affectsExecution = false)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));

        var analysis = new ConsistencyAnalysis
        {
            NodeId = node.NodeId
        };

        var now = DateTimeOffset.UtcNow;
        var activeClaims = node.Claims
            .Where(c => c.Expires == null || c.Expires > now)
            .ToList();

        if (!activeClaims.Any())
        {
            return analysis;
        }

        // Group claims by property (case-insensitive)
        var propertyGroups = activeClaims.GroupBy(c => c.Property, StringComparer.OrdinalIgnoreCase);
        double totalConfidenceSum = 0.0;
        int propertyCount = 0;

        foreach (var group in propertyGroups)
        {
            string propertyName = group.Key;
            propertyCount++;

            // Calculate total weight for each value of this property
            var valueWeights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            foreach (var claim in group)
            {
                double sourceWeight = GetSourceWeight(claim.Source);
                double temporalDecay = GetTemporalDecay(claim.Timestamp, now);
                double calculatedWeight = claim.Confidence * sourceWeight * temporalDecay;

                string claimValue = claim.Value ?? string.Empty;
                if (valueWeights.TryGetValue(claimValue, out double existing))
                {
                    valueWeights[claimValue] = existing + calculatedWeight;
                }
                else
                {
                    valueWeights[claimValue] = calculatedWeight;
                }
            }

            // Order value weights descending
            var sortedValues = valueWeights
                .OrderByDescending(kvp => kvp.Value)
                .ToList();

            if (!sortedValues.Any()) continue;

            var dominant = sortedValues[0];
            analysis.DominantValues[propertyName] = dominant.Key;
            totalConfidenceSum += dominant.Value;

            double tension = 0.0;
            if (sortedValues.Count > 1)
            {
                double w1 = dominant.Value;
                double w2 = sortedValues[1].Value;

                tension = w1 > 0.0 ? Math.Min(1.0, w2 / w1) : (w2 > 0.0 ? 1.0 : 0.0);
            }

            analysis.PropertyTensions[propertyName] = tension;

            // Determine if escalation is required
            bool isCriticalNode = node.NodeType == WikiNodeType.Workflow || node.NodeType == WikiNodeType.Goal;
            bool executionImpacted = affectsExecution || isCriticalNode;
            bool hasTension = tension >= TensionEscalationThreshold;
            bool confidenceCollapsed = dominant.Value < ConfidenceCollapseThreshold;

            if (hasTension && (executionImpacted || confidenceCollapsed))
            {
                string reason = executionImpacted
                    ? $"Tension on '{propertyName}' ({tension:F2}) impacts executing node '{node.NodeId}'."
                    : $"Confidence collapse on '{propertyName}' (dominant weight {dominant.Value:F2} is below threshold {ConfidenceCollapseThreshold:F2}).";

                analysis.Escalations.Add(new EscalationRequest
                {
                    Property = propertyName,
                    Reason = reason,
                    Tension = tension,
                    MaxConfidence = dominant.Value
                });
            }
        }

        if (propertyCount > 0)
        {
            analysis.AverageConfidence = totalConfidenceSum / propertyCount;
        }

        return analysis;
    }

    private double GetSourceWeight(string source)
    {
        return (source?.ToLowerInvariant()) switch
        {
            "user_statement" => 1.0,
            "workflow_activity" => 0.8,
            "inferred_inactivity" => 0.2,
            _ => 0.5 // Fallback/default weight
        };
    }

    private double GetTemporalDecay(DateTimeOffset timestamp, DateTimeOffset now)
    {
        double elapsedSeconds = (now - timestamp).TotalSeconds;
        if (elapsedSeconds <= 0.0) return 1.0;

        double lambda = Math.Log(2) / ClaimDecayHalfLifeSeconds;
        return Math.Exp(-lambda * elapsedSeconds);
    }
}

public class ConsistencyAnalysis
{
    public string NodeId { get; set; } = string.Empty;
    public Dictionary<string, string> DominantValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> PropertyTensions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<EscalationRequest> Escalations { get; set; } = new();
    public double AverageConfidence { get; set; } = 1.0;
}

public class EscalationRequest
{
    public string Property { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public double Tension { get; set; }
    public double MaxConfidence { get; set; }
}
