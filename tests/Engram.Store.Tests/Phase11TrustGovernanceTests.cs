using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Engram.Store.Wiki;
using Engram.Store.Salience;
using Engram.Store.Governance;

namespace Engram.Store.Tests;

public class Phase11TrustGovernanceTests
{
    [Fact]
    public void Explainability_NarrativeGeneration_CleansTechnicalFactors()
    {
        // Arrange
        var trace = new ReasonTrace
        {
            TriggerType = TraceTriggerType.SalienceShift,
            TargetEntityId = "node_123",
            Description = "Salience drift detected",
            CausalFactors = new List<string>
            {
                "salience propagated from node_abc",
                "edge weight was high",
                "decay rate was low",
                "Confidence is low",
                "attention storm active"
            },
            SystemComponent = "TestComponent"
        };

        // Act
        var narrative = DecisionNarrator.Narrate(trace);

        // Assert
        Assert.Contains("Priority tracking updated because:", narrative);
        Assert.Contains("activity related from node_abc", narrative);
        Assert.Contains("relevance was high", narrative);
        Assert.Contains("relevance fade was low", narrative);
        Assert.Contains("confidence score is low", narrative);
        Assert.Contains("sudden activity spike active", narrative);
    }

    [Fact]
    public void MemorySovereignty_ForgetAndReconciliation_PerformsPurgeAndLeavesEnvelope()
    {
        // Arrange
        using var workspace = new TempWorkspace();
        var store = new WikiNodeStore(workspace.Paths);
        var sovereignty = new MemorySovereigntySystem(store, workspace.Paths);

        var targetNode = new WikiNode
        {
            NodeId = "target_node",
            Title = "Secret Project Alpha",
            NodeType = WikiNodeType.Project,
            Salience = 1.0,
            Confidence = 0.9
        };

        var otherNode = new WikiNode
        {
            NodeId = "other_node",
            Title = "Related Node",
            NodeType = WikiNodeType.Concept,
            Links = new List<string> { "target_node" },
            Edges = new List<WikiEdge>
            {
                new() { TargetNodeId = "target_node", RelationType = "references" }
            },
            Claims = new List<SemanticClaim>
            {
                new() { ClaimId = "claim_1", Property = "uses", Value = "target_node", Context = "target_node" }
            },
            Salience = 1.5
        };

        store.Save(targetNode);
        store.Save(otherNode);

        // Act
        sovereignty.Forget("target_node");

        // Assert
        // 1. Target node file is completely deleted
        Assert.Null(store.Load("target_node"));

        // 2. References in other node are cleaned up
        var updatedOther = store.Load("other_node");
        Assert.NotNull(updatedOther);
        Assert.Empty(updatedOther.Links);
        Assert.Empty(updatedOther.Edges);
        Assert.Empty(updatedOther.Claims);

        // 3. Historical deletion envelope is created and preserved
        Assert.True(sovereignty.IsDeleted("target_node"));
        var envelope = sovereignty.GetEnvelope("target_node");
        Assert.NotNull(envelope);
        Assert.Equal("target_node", envelope.OriginalNodeId);
        Assert.Equal("Secret Project Alpha", envelope.OriginalTitle);
        Assert.Equal(WikiNodeType.Project, envelope.OriginalType);
        Assert.Equal("Referenced entity removed by user.", envelope.PlaceholderText);

        // 4. Reconciliation: orphan node salience decays
        // otherNode becomes an orphan because links, edges, and claims pointing to target_node were removed.
        Assert.True(updatedOther.Salience < 1.5);
    }

    [Fact]
    public void RealityCorrection_DisputeClaim_InvertsGroundingAndRollsBackDerivativeMetrics()
    {
        // Arrange
        using var workspace = new TempWorkspace();
        var store = new WikiNodeStore(workspace.Paths);
        var correctionLayer = new RealityCorrectionLayer(store, workspace.Paths);

        var disputedNode = new WikiNode
        {
            NodeId = "node_a",
            Title = "Subject Node",
            Confidence = 0.9,
            Claims = new List<SemanticClaim>
            {
                new() { ClaimId = "claim_abc", Property = "status", Value = "inactive", Confidence = 0.8, Source = "inferred" }
            }
        };

        var derivedNode = new WikiNode
        {
            NodeId = "node_b",
            Title = "Derived Node",
            Links = new List<string> { "node_a" },
            Edges = new List<WikiEdge>
            {
                new() { TargetNodeId = "node_a", PropagationWeight = 0.8 }
            },
            Claims = new List<SemanticClaim>
            {
                new() { ClaimId = "claim_xyz", Property = "status", Value = "inactive", Confidence = 0.7, Source = "inferred_inactivity" }
            }
        };

        store.Save(disputedNode);
        store.Save(derivedNode);

        // Act
        correctionLayer.DisputeClaim("node_a", "claim_abc", "active");

        // Assert
        var updatedDisputed = store.Load("node_a");
        Assert.NotNull(updatedDisputed);

        // 1. Initial claim confidence downgraded to 0
        var oldClaim = updatedDisputed.Claims.First(c => c.ClaimId == "claim_abc");
        Assert.Equal(0.0, oldClaim.Confidence);

        // 2. Node confidence degraded due to friction
        Assert.True(updatedDisputed.Confidence < 0.9);

        // 3. User statement injected as new grounding truth
        var userClaim = updatedDisputed.Claims.First(c => c.Source == "user_statement");
        Assert.Equal(1.0, userClaim.Confidence);
        Assert.Equal("status", userClaim.Property);
        Assert.Equal("active", userClaim.Value);

        // 4. Narrative Rollback check: derived node propagation weight decayed and derivative claim confidence downgraded
        var updatedDerived = store.Load("node_b");
        Assert.NotNull(updatedDerived);
        
        var edge = updatedDerived.Edges.First(e => e.TargetNodeId == "node_a");
        Assert.True(edge.PropagationWeight < 0.8);

        var derivedClaim = updatedDerived.Claims.First(c => c.ClaimId == "claim_xyz");
        Assert.Equal(0.1, derivedClaim.Confidence);

        // 5. Counterfactual correction is stored
        var corrections = correctionLayer.GetCorrections();
        Assert.Single(corrections);
        Assert.Equal("node_a", corrections[0].NodeId);
        Assert.Equal("status", corrections[0].Property);
        Assert.Equal("inactive", corrections[0].DisputedValue);
        Assert.Equal("active", corrections[0].CorrectedValue);
    }

    [Fact]
    public void SafetyConstitution_StateMachineAndIsolationBoundary_EnforcesFreezeAndVerifyIntegrity()
    {
        // Arrange
        using var workspace = new TempWorkspace();
        var auditLog = new ConstitutionalAuditLog(workspace.Paths);
        var stateMachine = new ConstitutionalStateMachine(workspace.Paths, auditLog);
        var boundary = new GovernanceIsolationBoundary(stateMachine);

        // Act & Assert 1: Normal operational state does not throw
        Assert.Equal(ConstitutionalState.Operational, stateMachine.CurrentState);
        boundary.VerifyExecutionSafety("action 1");
        boundary.VerifyWriteSafety("action 1");
        boundary.VerifyMemorySafety("action 1");

        // Act & Assert 2: C1 shifts to Restrained
        stateMachine.HandleViolation(new ConstitutionalViolation
        {
            Severity = ConstitutionalSeverity.C1,
            ViolatingSubsystem = "ComponentA",
            Details = "C1 minor issue"
        });
        Assert.Equal(ConstitutionalState.Restrained, stateMachine.CurrentState);
        boundary.VerifyExecutionSafety("action 2");
        boundary.VerifyWriteSafety("action 2");
        boundary.VerifyMemorySafety("action 2");

        // Act & Assert 3: C2 shifts to Degraded
        stateMachine.HandleViolation(new ConstitutionalViolation
        {
            Severity = ConstitutionalSeverity.C2,
            ViolatingSubsystem = "ComponentB",
            Details = "C2 drift issue"
        });
        Assert.Equal(ConstitutionalState.Degraded, stateMachine.CurrentState);
        boundary.VerifyExecutionSafety("action 3");
        boundary.VerifyWriteSafety("action 3");
        boundary.VerifyMemorySafety("action 3");

        // Act & Assert 4: C3 shifts to IntegrityUncertain
        stateMachine.HandleViolation(new ConstitutionalViolation
        {
            Severity = ConstitutionalSeverity.C3,
            ViolatingSubsystem = "ComponentC",
            Details = "C3 privacy issue"
        });
        Assert.Equal(ConstitutionalState.IntegrityUncertain, stateMachine.CurrentState);
        boundary.VerifyExecutionSafety("action 4");
        Assert.Throws<InvalidOperationException>(() => boundary.VerifyWriteSafety("action 4"));
        boundary.VerifyMemorySafety("action 4");

        // Act & Assert 5: C4 shifts to Quarantine
        stateMachine.HandleViolation(new ConstitutionalViolation
        {
            Severity = ConstitutionalSeverity.C4,
            ViolatingSubsystem = "ComponentD",
            Details = "C4 containment action"
        });
        Assert.Equal(ConstitutionalState.Quarantine, stateMachine.CurrentState);
        boundary.VerifyExecutionSafety("action 5");
        Assert.Throws<InvalidOperationException>(() => boundary.VerifyWriteSafety("action 5"));
        Assert.Throws<InvalidOperationException>(() => boundary.VerifyMemorySafety("action 5"));

        // Act & Assert 5b: C5 shifts to Frozen and blocks operations
        stateMachine.HandleViolation(new ConstitutionalViolation
        {
            Severity = ConstitutionalSeverity.C5,
            ViolatingSubsystem = "ComponentE",
            Details = "C5 destructive action"
        });
        Assert.Equal(ConstitutionalState.Frozen, stateMachine.CurrentState);
        
        var ex = Assert.Throws<InvalidOperationException>(() => boundary.VerifyExecutionSafety("action 6"));
        Assert.Contains("FROZEN due to constitutional safety breach", ex.Message);

        // Act & Assert 6: Recover returns state back to Operational
        stateMachine.Recover("Human audit resolved the safety breach manually.");
        Assert.Equal(ConstitutionalState.Operational, stateMachine.CurrentState);
        boundary.VerifyExecutionSafety("action 7");

        // Act & Assert 7: Verify blockchain-like tamper-evident audit log integrity
        Assert.True(auditLog.VerifyIntegrity());
        var entries = auditLog.GetEntries();
        Assert.Equal(6, entries.Count); // C1, C2, C3, C4, C5, Recover
    }

    [Fact]
    public void TrustCalibration_ReversibilityWeightingAndAdaptation_PerformsCalculationsCorrectly()
    {
        // Arrange
        using var workspace = new TempWorkspace();
        var trustEngine = new TrustCalibrationEngine(workspace.Paths);

        // Act & Assert 1: Reversibility weighted trust accrual
        // Reversible action builds trust faster
        trustEngine.RecordActionOutcome("file_editing", isSuccess: true, isReversible: true);
        double reversibleScore = trustEngine.GetTrustScore("file_editing");
        Assert.Equal(0.55, reversibleScore, 5); // 0.5 + 0.05

        // Irreversible action builds trust slower
        trustEngine.RecordActionOutcome("file_deletion", isSuccess: true, isReversible: false);
        double irreversibleScore = trustEngine.GetTrustScore("file_deletion");
        Assert.Equal(0.51, irreversibleScore, 5); // 0.5 + 0.01

        // Override penalty drops irreversible trust more heavily
        trustEngine.RecordActionOutcome("file_editing", isSuccess: false, isReversible: true);
        double revOverrideScore = trustEngine.GetTrustScore("file_editing");
        Assert.Equal(0.35, revOverrideScore, 5); // 0.55 - 0.20

        trustEngine.RecordActionOutcome("file_deletion", isSuccess: false, isReversible: false);
        double irrOverrideScore = trustEngine.GetTrustScore("file_deletion");
        Assert.Equal(0.01, irrOverrideScore, 5); // 0.51 - 0.50

        // Act & Assert 2: Comfort Adaptation checks
        // Under high user override/dismissal friction, ceiling decays
        trustEngine.AdaptToComfortSignals(recentOverrides: 4, recentDismissals: 2); // 6 total
        Assert.True(trustEngine.AutonomyCeiling < 1.0);
        Assert.True(trustEngine.InterventionFrequencyMultiplier < 1.0);

        // Ceiling recovers slowly when friction is absent
        double currentCeiling = trustEngine.AutonomyCeiling;
        trustEngine.AdaptToComfortSignals(recentOverrides: 0, recentDismissals: 0);
        Assert.True(trustEngine.AutonomyCeiling > currentCeiling);
    }

    [Fact]
    public void CognitiveBoundarySystem_PrivacyZonesAndExclusions_RestrictsMatches()
    {
        // Arrange
        var config = new GovernanceConfig();
        config.PrivacyZones.Add(new PrivacyZoneRule
        {
            RuleName = "financial exclusion",
            ExcludedPathPattern = "C:/Users/Samik/Documents/Tax_Returns",
            ExcludedAppProcess = "turbotax"
        });

        var boundaries = new CognitiveBoundarySystem(config);

        // Act & Assert 1: Privacy exclusions
        Assert.True(boundaries.IsExcluded("C:/Users/Samik/Documents/Tax_Returns/2025.pdf", "explorer"));
        Assert.True(boundaries.IsExcluded("C:/projects/app.cs", "turbotax"));
        Assert.False(boundaries.IsExcluded("C:/projects/app.cs", "vscode"));

        // Act & Assert 2: Sensitive topic trigger check
        Assert.True(boundaries.IsSensitive("Consulting a medical doctor for heart symptoms"));
        Assert.False(boundaries.IsSensitive("Writing program scripts for compiler"));

        // Act & Assert 3: Narrative intrusion guard
        var originalNarrative = "User is in a burnout panic spiral and procrastinating.";
        // High confidence lets narrative pass
        Assert.Equal(originalNarrative, boundaries.RestrainNarrative(originalNarrative, 0.95));
        // Low confidence rewrites creepiness
        var cleanNarrative = boundaries.RestrainNarrative(originalNarrative, 0.5);
        Assert.Contains("temporary operational context shift", cleanNarrative);
        Assert.DoesNotContain("burnout", cleanNarrative);
        Assert.DoesNotContain("panic", cleanNarrative);
        Assert.DoesNotContain("spiral", cleanNarrative);
    }

    [Fact]
    public void AmbientCognitionRestraint_DailyBudgetsFlowStateAndVerbosity_EnforcesRestraints()
    {
        // Arrange
        var config = new GovernanceConfig { MaxDailyInterventions = 2 };
        var restraint = new AmbientCognitionRestraint(config);

        // Act & Assert 1: Daily budget depletion
        Assert.True(restraint.CheckDailyBudget());
        restraint.RecordIntervention();
        Assert.True(restraint.CheckDailyBudget());
        restraint.RecordIntervention();
        Assert.False(restraint.CheckDailyBudget()); // depletion hit

        // Act & Assert 2: Flow state/activity respect model suppression
        Assert.True(restraint.ShouldSuppressDueToActivity("deep_work", windowSwitchRatePerMin: 1.0, isTyping: false));
        Assert.True(restraint.ShouldSuppressDueToActivity("browsing", windowSwitchRatePerMin: 12.0, isTyping: false));
        Assert.True(restraint.ShouldSuppressDueToActivity("browsing", windowSwitchRatePerMin: 1.0, isTyping: true));
        Assert.False(restraint.ShouldSuppressDueToActivity("browsing", windowSwitchRatePerMin: 1.0, isTyping: false));

        // Act & Assert 3: Silence confidence gates
        Assert.True(restraint.CheckConfidenceGate(0.55, isUrgent: true));
        Assert.False(restraint.CheckConfidenceGate(0.55, isUrgent: false)); // default threshold is 0.7
        Assert.True(restraint.CheckConfidenceGate(0.75, isUrgent: false));

        // Act & Assert 4: Verbosity estimation
        Assert.Equal("Concise", restraint.EstimateVerbosity(taskSwitchRate: 10.0, appLoadFactor: 8.0));
        Assert.Equal("Standard", restraint.EstimateVerbosity(taskSwitchRate: 5.0, appLoadFactor: 4.0));
        Assert.Equal("Detailed", restraint.EstimateVerbosity(taskSwitchRate: 1.0, appLoadFactor: 1.0));
    }

    [Fact]
    public void LongitudinalTrust_AnnoyanceFrictionAndForgivenessDecay_AccumulatesAndDecays()
    {
        // Arrange
        using var workspace = new TempWorkspace();
        var model = new LongitudinalTrustModel(workspace.Paths);

        // Act & Assert 1: Annoyance score accumulation
        Assert.Equal(0.0, model.AnnoyanceScore, 5);
        Assert.Equal(1.0, model.HistoricalTrustIndex, 5);

        model.RecordAnnoyance(intensity: 2.0);
        Assert.Equal(2.0, model.AnnoyanceScore, 5);
        Assert.Equal(0.9, model.HistoricalTrustIndex, 5); // 1.0 - (2 * 0.05)

        // Act & Assert 2: Forgiveness decay on quiet coexistence
        model.ApplyForgivenessDecay(TimeSpan.FromHours(2.0));
        Assert.Equal(1.0, model.AnnoyanceScore, 5); // 2.0 - (2 * 0.5)
        
        // Since annoyance is now 1.0 (which is < 2.0), trust repair is active
        Assert.Equal(0.94, model.HistoricalTrustIndex, 5); // 0.9 + (2 * 0.02)
    }
}
