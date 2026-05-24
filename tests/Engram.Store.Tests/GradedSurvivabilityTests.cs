using System;
using System.IO;
using System.Text.Json;
using Engram.Store.Wiki;
using Engram.Store.Governance;
using Engram.Store.Inference;
using Xunit;

namespace Engram.Store.Tests;

public class GradedSurvivabilityTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public GradedSurvivabilityTests()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
    }

    public void Dispose()
    {
        _workspace.Dispose();
    }

    [Fact]
    public void SaveNode_ThrowsInvalidOperationException_WhenIntegrityUncertainOrHigher()
    {
        // Arrange
        using var store = new WikiNodeStore(_workspace.Paths);
        var auditLog = new ConstitutionalAuditLog(_workspace.Paths);
        var stateMachine = new ConstitutionalStateMachine(_workspace.Paths, auditLog);
        var boundary = new GovernanceIsolationBoundary(stateMachine);
        store.SetBoundary(boundary);

        // Move to IntegrityUncertain
        stateMachine.HandleViolation(new ConstitutionalViolation
        {
            Severity = ConstitutionalSeverity.C3,
            ViolatingSubsystem = "Telemetry",
            Details = "High anomaly rate triggers suspension."
        });

        Assert.Equal(ConstitutionalState.IntegrityUncertain, stateMachine.CurrentState);

        var node = new WikiNode
        {
            NodeId = "integrity_test",
            Title = "Integrity Test",
            NodeType = WikiNodeType.Concept,
            Summary = "Testing write suspension",
            Salience = 0.8,
            Confidence = 0.9
        };

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => store.Save(node));
        Assert.Contains("Writes are suspended", ex.Message);

        // Verify deferred mutation file exists and contains details
        var deferredDir = Path.Combine(_workspace.Paths.Wiki, "..", "deferred_mutations");
        Assert.True(Directory.Exists(deferredDir));
        var files = Directory.GetFiles(deferredDir, "*.json");
        Assert.Single(files);

        var jsonContent = File.ReadAllText(files[0]);
        var mutation = JsonSerializer.Deserialize<DeferredMutation>(jsonContent);
        Assert.NotNull(mutation);
        Assert.Equal("Save", mutation.OperationType);
        Assert.Equal("integrity_test", mutation.TargetNodeId);
        Assert.Contains("Testing write suspension", mutation.TargetContent);
        Assert.Contains("Writes are suspended", mutation.CausalReason);
    }

    [Fact]
    public void DeleteNode_ThrowsInvalidOperationException_WhenIntegrityUncertainOrHigher()
    {
        // Arrange
        using var store = new WikiNodeStore(_workspace.Paths);
        var auditLog = new ConstitutionalAuditLog(_workspace.Paths);
        var stateMachine = new ConstitutionalStateMachine(_workspace.Paths, auditLog);
        var boundary = new GovernanceIsolationBoundary(stateMachine);
        store.SetBoundary(boundary);

        // Save node first under Operational
        var node = new WikiNode
        {
            NodeId = "delete_test",
            Title = "Delete Test",
            NodeType = WikiNodeType.Concept,
            Summary = "Testing delete suspension"
        };
        store.Save(node);

        var nodeFile = Path.Combine(_workspace.Paths.Wiki, "delete_test.md");
        Assert.True(File.Exists(nodeFile));

        // Move to Quarantine
        stateMachine.HandleViolation(new ConstitutionalViolation
        {
            Severity = ConstitutionalSeverity.C4,
            ViolatingSubsystem = "Security",
            Details = "Unauthorized task runs trigger quarantine."
        });

        Assert.Equal(ConstitutionalState.Quarantine, stateMachine.CurrentState);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => store.Delete("delete_test"));
        Assert.Contains("Writes are suspended", ex.Message);

        // Verify file is NOT deleted
        Assert.True(File.Exists(nodeFile));

        // Verify deferred mutation exists
        var deferredDir = Path.Combine(_workspace.Paths.Wiki, "..", "deferred_mutations");
        var files = Directory.GetFiles(deferredDir, "*.json");
        Assert.Single(files);

        var jsonContent = File.ReadAllText(files[0]);
        var mutation = JsonSerializer.Deserialize<DeferredMutation>(jsonContent);
        Assert.NotNull(mutation);
        Assert.Equal("Delete", mutation.OperationType);
        Assert.Equal("delete_test", mutation.TargetNodeId);
        Assert.Contains("Writes are suspended", mutation.CausalReason);
    }

    [Fact]
    public void SaveNode_PopulatesProvenance_WhenDefault()
    {
        // Arrange
        using var store = new WikiNodeStore(_workspace.Paths);
        var auditLog = new ConstitutionalAuditLog(_workspace.Paths);
        var stateMachine = new ConstitutionalStateMachine(_workspace.Paths, auditLog);
        var boundary = new GovernanceIsolationBoundary(stateMachine);
        store.SetBoundary(boundary);

        var node = new WikiNode
        {
            NodeId = "prov_test",
            Title = "Provenance Test",
            NodeType = WikiNodeType.Concept,
            Summary = "Testing provenance marking"
        };

        // Act
        store.Save(node);

        // Assert
        var loaded = store.Load("prov_test");
        Assert.NotNull(loaded);
        Assert.Equal("SafetyConstitutionBounded", loaded.ProvenanceApprovalSource);
        Assert.Equal(1.0, loaded.ProvenanceEnvironmentalReliability);
        Assert.Equal(node.Confidence, loaded.ProvenanceConfidence);

        // Verify it is serialized in markdown file
        var nodeFile = Path.Combine(_workspace.Paths.Wiki, "prov_test.md");
        var text = File.ReadAllText(nodeFile);
        Assert.Contains("provenance_approval_source: SafetyConstitutionBounded", text);
        Assert.Contains("provenance_environmental_reliability: 1.00", text);
    }
}
