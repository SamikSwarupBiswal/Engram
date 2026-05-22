using System;
using System.Collections.Generic;
using System.Linq;
using Engram.Store.Wiki;
using Engram.Store.Salience;

namespace Engram.Store.Governance;

/// <summary>
/// Central coordinator orchestrating all Explainability, Sovereignty, Trust, Boundary, Restraint,
/// Observability, disputation, and Constitutional Safety systems of Phase 11.
/// </summary>
public class GovernanceCoordinator
{
    public GovernanceConfig Config { get; private set; }
    public ReasonTraceEngine Traces { get; }
    public MemorySovereigntySystem Sovereignty { get; }
    public TrustCalibrationEngine Trust { get; }
    public CognitiveBoundarySystem Boundaries { get; }
    public AmbientCognitionRestraint Restraint { get; }
    public LongitudinalTrustModel LongitudinalTrust { get; }
    public TransparencyObservabilityService Observability { get; }
    public RealityCorrectionLayer RealityCorrection { get; }
    public ConstitutionalStateMachine SafetyStateMachine { get; }
    public ConstitutionalAuditLog SafetyAudit { get; }
    public GovernanceIsolationBoundary SafetyBoundary { get; }

    public GovernanceCoordinator(WikiNodeStore nodeStore, WorkspacePaths paths, DriftAlertStore? driftStore = null)
    {
        Config = new GovernanceConfig();
        
        Traces = new ReasonTraceEngine(paths);
        Sovereignty = new MemorySovereigntySystem(nodeStore, paths, driftStore);
        Trust = new TrustCalibrationEngine(paths);
        Boundaries = new CognitiveBoundarySystem(Config);
        Restraint = new AmbientCognitionRestraint(Config);
        LongitudinalTrust = new LongitudinalTrustModel(paths);
        Observability = new TransparencyObservabilityService(paths);
        RealityCorrection = new RealityCorrectionLayer(nodeStore, paths);
        SafetyAudit = new ConstitutionalAuditLog(paths);
        SafetyStateMachine = new ConstitutionalStateMachine(paths, SafetyAudit);
        SafetyBoundary = new GovernanceIsolationBoundary(SafetyStateMachine);

        Observability.LogActivity("System Boot", "Governance Coordinator initialized and running.", impact: "Medium");
    }

    /// <summary>
    /// Update central configuration settings.
    /// </summary>
    public void UpdateConfig(GovernanceConfig config)
    {
        if (config == null) return;
        Config = config;
        Observability.LogActivity("Config Update", "Governance parameters and privacy zones updated.");
    }

    /// <summary>
    /// Execute a forget command with structural cleanup and deletion envelopes.
    /// </summary>
    public void ForgetNode(string nodeId)
    {
        SafetyBoundary.VerifyExecutionSafety($"Forget Node: {nodeId}");
        
        Observability.LogActivity("Memory Forget", $"Node '{nodeId}' targeted for structural forgetting.", relatedNodeId: nodeId, impact: "High");
        Sovereignty.Forget(nodeId);
        
        Traces.AddTrace(
            TraceTriggerType.ExecutionDecision,
            nodeId,
            "Permanent structural node deletion performed.",
            new() { "User dispute or automatic policy expiration", "Incoming edge cleanup", "Deletion envelope registered" },
            "MemorySovereigntySystem"
        );
    }

    /// <summary>
    /// Disputes a claim on a wiki node, updating grounding truth.
    /// </summary>
    public void DisputeClaim(string nodeId, string claimId, string correctedValue)
    {
        SafetyBoundary.VerifyExecutionSafety($"Dispute Claim on node {nodeId}");

        Observability.LogActivity("Grounding Dispute", $"Claim {claimId} on node '{nodeId}' disputed.", relatedNodeId: nodeId, impact: "Medium");
        RealityCorrection.DisputeClaim(nodeId, claimId, correctedValue);

        Traces.AddTrace(
            TraceTriggerType.Escalation,
            nodeId,
            $"Claim disputed by user. Subsystem confidence degraded.",
            new() { "Explicit dispute", "Corrected value preserved as template", "Propagation pathways adjusted" },
            "RealityCorrectionLayer"
        );
    }

    /// <summary>
    /// Pre-execution safety check. Ensures permission exists, trust score is appropriate, and boundary is not breached.
    /// </summary>
    public bool CheckActionSafety(string actionDetail, string category, string targetResourceId, bool isReversible)
    {
        // 1. Isolation boundary check (throws if Frozen)
        SafetyBoundary.VerifyExecutionSafety(actionDetail);

        // 2. Privacy zone boundaries check
        if (Boundaries.IsExcluded(targetResourceId, actionDetail))
        {
            var violation = new ConstitutionalViolation
            {
                Severity = ConstitutionalSeverity.C3,
                ViolatingSubsystem = "CognitiveBoundarySystem",
                Details = $"Privacy zone violation triggered for resource '{targetResourceId}' during action '{actionDetail}'.",
                TriggerAction = actionDetail,
                CausalChain = new() { "Target matches privacy zone rule", "Execution block initiated" }
            };
            SafetyStateMachine.HandleViolation(violation);
            return false;
        }

        // 3. Permission decay check
        bool hasPerm = Trust.CheckPermission(category, targetResourceId);
        if (!hasPerm)
        {
            // If no permission, raise violation
            var violation = new ConstitutionalViolation
            {
                Severity = isReversible ? ConstitutionalSeverity.C2 : ConstitutionalSeverity.C4, // Unauthorized high-risk
                ViolatingSubsystem = "TrustCalibrationEngine",
                Details = $"Attempted unauthorized action '{actionDetail}' in category '{category}' without valid permission envelope.",
                TriggerAction = actionDetail,
                CausalChain = new() { "No active permission grant found", "Expired decay window or unapproved context" }
            };
            SafetyStateMachine.HandleViolation(violation);
            return false;
        }

        // 4. Double check trust index
        double trustIndex = Trust.GetTrustScore(category);
        if (trustIndex < 0.3)
        {
            var violation = new ConstitutionalViolation
            {
                Severity = ConstitutionalSeverity.C3,
                ViolatingSubsystem = "TrustCalibrationEngine",
                Details = $"Trust score for domain '{category}' ({trustIndex:F2}) below operation floor.",
                TriggerAction = actionDetail,
                CausalChain = new() { "Cumulative overrides degraded score", "Operational constraints enforced" }
            };
            SafetyStateMachine.HandleViolation(violation);
            return false;
        }

        return true;
    }
}
