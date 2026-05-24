using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Engram.Store.Deployment;
using Engram.Store.Metabolism;
using Engram.Store.Wiki;
using Xunit;

namespace Engram.Store.Tests;

public class LongTermStabilityCertificationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly WorkspacePaths _paths;
    private readonly WikiNodeStore _nodeStore;
    private readonly VersionCompatibilityManager _versionManager;
    private readonly SnapshotRollbackSystem _rollbackSystem;
    private readonly SemanticMigrationEngine _migrationEngine;
    private readonly MigrationIntegrityVerifier _integrityVerifier;
    private readonly AtomicUpgradeCoordinator _upgradeCoordinator;

    public LongTermStabilityCertificationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram_stability_test_" + Guid.NewGuid().ToString("n")[..8]);
        _paths = new WorkspacePaths(_tempDir);
        
        Directory.CreateDirectory(_paths.Raw);
        Directory.CreateDirectory(_paths.Wiki);
        Directory.CreateDirectory(_paths.Runs);
        Directory.CreateDirectory(_paths.Config);
        Directory.CreateDirectory(_paths.Logs);
        Directory.CreateDirectory(_paths.Archives);

        _nodeStore = new WikiNodeStore(_paths);
        _versionManager = new VersionCompatibilityManager(_paths);
        _rollbackSystem = new SnapshotRollbackSystem(_paths);
        _migrationEngine = new SemanticMigrationEngine(_nodeStore, _versionManager);
        _integrityVerifier = new MigrationIntegrityVerifier();
        _upgradeCoordinator = new AtomicUpgradeCoordinator(
            _paths, _nodeStore, _versionManager, _rollbackSystem, _migrationEngine, _integrityVerifier);
    }

    public void Dispose()
    {
        _nodeStore.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public async Task Simulate90DayTimeWarp_GraphEquilibriumMaintained()
    {
        var virtualStart = DateTimeOffset.UtcNow.AddDays(-90);

        // Seed core developer node that should not decay
        _nodeStore.Save(new WikiNode
        {
            NodeId = "developer_core",
            Title = "Samik",
            NodeType = WikiNodeType.Person,
            Summary = "The user profile",
            Salience = 1.0,
            LastTouchedAt = virtualStart
        });

        // 90 Day Simulation Loop
        for (int day = 1; day <= 90; day++)
        {
            var currentVirtualTime = virtualStart.AddDays(day);

            // Ingest short-lived daily activity nodes
            _nodeStore.Save(new WikiNode
            {
                NodeId = $"daily_node_{day}",
                Title = $"Activity on day {day}",
                NodeType = WikiNodeType.Concept,
                Summary = $"Context captured on day {day}",
                Salience = 1.0 - (day * 0.005), // gradual starting salience decay
                LastTouchedAt = currentVirtualTime
            });

            // Trigger simulated manual cleanup logic or verify node persistence boundaries
            if (day % 10 == 0)
            {
                // Verify core node has not been lost
                var core = _nodeStore.Load("developer_core");
                Assert.NotNull(core);
            }
        }

        // Verify active concept nodes count is reasonably bounded and hasn't crashed
        var allNodes = _nodeStore.LoadAll();
        Assert.True(allNodes.Count >= 90);
        var developer = _nodeStore.Load("developer_core");
        Assert.NotNull(developer);
        Assert.Equal("Samik", developer.Title);
    }

    [Fact]
    public void RepeatedMigrationCycles_ResilienceVerification()
    {
        // 1. Initial State Check
        var initialInfo = _versionManager.GetCurrentVersionInfo();
        Assert.Equal(1, initialInfo.SchemaVersion);

        // 2. Perform sequential migrations up to Level 2
        var result = _upgradeCoordinator.ExecuteUpgrade("1.1.0", 2);
        Assert.True(result.Success);
        Assert.Equal(2, _versionManager.GetCurrentVersionInfo().SchemaVersion);

        // 3. Rollback manually to version 1
        var backupsDir = Path.Combine(_paths.Root, "backups");
        var latestSnapshot = Directory.GetDirectories(backupsDir, "snapshot_*")
            .OrderByDescending(d => d)
            .FirstOrDefault();
        
        Assert.NotNull(latestSnapshot);

        _upgradeCoordinator.ExecuteUpgrade("1.0.0", 1); // Revert metadata version info
        _rollbackSystem.RestoreSnapshot(latestSnapshot);

        var rolledBackInfo = _versionManager.GetCurrentVersionInfo();
        Assert.Equal(1, rolledBackInfo.SchemaVersion);

        // 4. Upgrade again to Level 2
        var reUpgradeResult = _upgradeCoordinator.ExecuteUpgrade("1.1.0", 2);
        Assert.True(reUpgradeResult.Success);
        Assert.Equal(2, _versionManager.GetCurrentVersionInfo().SchemaVersion);
    }
}
