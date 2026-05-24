using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Engram.Store.Deployment;
using Engram.Store.Wiki;
using Xunit;

namespace Engram.Store.Tests;

public class MigrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly WorkspacePaths _paths;
    private readonly WikiNodeStore _nodeStore;
    private readonly VersionCompatibilityManager _versionManager;
    private readonly SemanticMigrationEngine _migrationEngine;
    private readonly MigrationIntegrityVerifier _integrityVerifier;
    private readonly MigrationSimulationHarness _simulationHarness;

    public MigrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram_migration_test_" + Guid.NewGuid().ToString("n")[..8]);
        _paths = new WorkspacePaths(_tempDir);
        
        Directory.CreateDirectory(_paths.Raw);
        Directory.CreateDirectory(_paths.Wiki);
        Directory.CreateDirectory(_paths.Config);

        _nodeStore = new WikiNodeStore(_paths);
        _versionManager = new VersionCompatibilityManager(_paths);
        _migrationEngine = new SemanticMigrationEngine(_nodeStore, _versionManager);
        _integrityVerifier = new MigrationIntegrityVerifier();
        _simulationHarness = new MigrationSimulationHarness(_paths);
    }

    public void Dispose()
    {
        _nodeStore.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void SemanticMigrationEngine_GetsPendingMigrations()
    {
        var pending = _migrationEngine.GetPendingMigrations();
        // Since initial version is 1, and default migrations registered are TargetVersion 1 and 2
        // Only target version 2 should be pending
        Assert.Single(pending);
        Assert.Equal(2, pending[0].TargetVersion);
    }

    [Fact]
    public void SemanticMigrationEngine_ExecutesSequentialMigrations()
    {
        // Set DB version to 0
        var info = _versionManager.GetCurrentVersionInfo();
        info.SchemaVersion = 0;
        _versionManager.SaveVersionInfo(info);

        var pending = _migrationEngine.GetPendingMigrations();
        Assert.Equal(2, pending.Count); // Migration 1 and 2 should be pending

        var errors = new List<string>();
        var confidence = _migrationEngine.ExecuteMigrations(out errors);

        Assert.Equal(MigrationConfidence.Safe, confidence);
        Assert.Empty(errors);

        var finalInfo = _versionManager.GetCurrentVersionInfo();
        Assert.Equal(2, finalInfo.SchemaVersion);
    }

    [Fact]
    public void MigrationIntegrityVerifier_ReportsDuplicates_AndBrokenLinks()
    {
        // 1. Save node referencing missing link target
        _nodeStore.Save(new WikiNode
        {
            NodeId = "node_a",
            Title = "Node A",
            NodeType = WikiNodeType.Concept,
            Links = new List<string> { "missing_target" },
            Claims = new List<SemanticClaim>
            {
                new() { Property = "status", Value = "active" },
                new() { Property = "status", Value = "active" } // Duplicate claim
            }
        });

        var report = _integrityVerifier.Verify(_nodeStore);

        Assert.True(report.IsValid); // IsValid is false only if broken link target count > 10
        Assert.Equal(1, report.BrokenLinksCount);
        Assert.Equal(1, report.DuplicateClaimsCount);
        Assert.Contains("missing_target", report.ValidationErrors[0]);
    }

    [Fact]
    public void MigrationIntegrityVerifier_FailsOnEmptyTitle()
    {
        var nodeFilePath = Path.Combine(_paths.Wiki, "node_invalid.md");
        File.WriteAllText(nodeFilePath, @"---
node_id: node_invalid
title: """" """"
node_type: Concept
---
# Invalid Title Node
");

        var report = _integrityVerifier.Verify(_nodeStore);
        Assert.False(report.IsValid);
        Assert.Contains("invalid title", report.ValidationErrors[0]);
    }

    [Fact]
    public void MigrationSimulationHarness_HandlesLegacyFormat()
    {
        // Seed legacy node format file using simulation harness
        _simulationHarness.InjectLegacyNodeFormat("legacy_node", "Legacy Node Title");

        // Verify we can load it through WikiNodeStore (which should tolerate or default fields)
        var loadedNode = _nodeStore.Load("legacy_node");
        Assert.NotNull(loadedNode);
        Assert.Equal("Legacy Node Title", loadedNode.Title);
    }

    [Fact]
    public void MigrationSimulationHarness_HandlesCorruptJsonGracefully()
    {
        // Seed corrupted JSON format file using simulation harness
        _simulationHarness.InjectCorruptNodeJson("corrupt_node");

        // Attempting to load this should fail and return null rather than crashing the thread pool
        var loadedNode = _nodeStore.Load("corrupt_node");
        Assert.Null(loadedNode);
    }

    [Fact]
    public void MigrationSimulationHarness_SimulatesInterruptedWrite()
    {
        // Seed truncated/interrupted write node using simulation harness
        _simulationHarness.SimulateInterruptedWrite("interrupted_node");

        // Attempting to load this should fail and return null safely
        var loadedNode = _nodeStore.Load("interrupted_node");
        Assert.Null(loadedNode);
    }
}
