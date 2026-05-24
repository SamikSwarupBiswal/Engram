using System;
using System.IO;
using System.Linq;
using Engram.Store.Deployment;
using Engram.Store.Wiki;
using Xunit;

namespace Engram.Store.Tests;

public class UpgradeEcologyTests : IDisposable
{
    private readonly string _tempDir;
    private readonly WorkspacePaths _paths;
    private readonly WikiNodeStore _nodeStore;
    private readonly VersionCompatibilityManager _versionManager;
    private readonly SnapshotRollbackSystem _rollbackSystem;
    private readonly SemanticMigrationEngine _migrationEngine;
    private readonly MigrationIntegrityVerifier _integrityVerifier;
    private readonly AtomicUpgradeCoordinator _upgradeCoordinator;

    public UpgradeEcologyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram_upgrade_test_" + Guid.NewGuid().ToString("n")[..8]);
        _paths = new WorkspacePaths(_tempDir);
        
        // Ensure directories exist
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
    public void VersionCompatibilityManager_Initializes_Correctly()
    {
        var info = _versionManager.GetCurrentVersionInfo();
        Assert.NotNull(info);
        Assert.Equal("1.0.0", info.SystemVersion);
        Assert.Equal(1, info.SchemaVersion);
        Assert.Equal(1, info.CapabilityVersion);
    }

    [Fact]
    public void VersionCompatibilityManager_SavesAndLoads_Correctly()
    {
        var info = new VersionInfo
        {
            SystemVersion = "1.2.0",
            SchemaVersion = 3,
            CapabilityVersion = 2,
            LastUpdatedAt = DateTimeOffset.UtcNow
        };

        _versionManager.SaveVersionInfo(info);
        var loaded = _versionManager.GetCurrentVersionInfo();

        Assert.Equal("1.2.0", loaded.SystemVersion);
        Assert.Equal(3, loaded.SchemaVersion);
        Assert.Equal(2, loaded.CapabilityVersion);
    }

    [Fact]
    public void VersionCompatibilityManager_ChecksCompatibility_OlderAndNewer()
    {
        // 1. Same version
        bool compat = _versionManager.CheckCompatibility(1, out var reason);
        Assert.True(compat);
        Assert.Contains("compatible", reason);

        // 2. System newer than database (migration required)
        compat = _versionManager.CheckCompatibility(2, out reason);
        Assert.True(compat);
        Assert.Contains("Migration is required", reason);

        // 3. Database newer than system (not compatible)
        var dbInfo = _versionManager.GetCurrentVersionInfo();
        dbInfo.SchemaVersion = 3;
        _versionManager.SaveVersionInfo(dbInfo);

        compat = _versionManager.CheckCompatibility(2, out reason);
        Assert.False(compat);
        Assert.Contains("newer than the system", reason);
    }

    [Fact]
    public void SnapshotRollbackSystem_CreatesAndRestores_StateCorrectly()
    {
        // 1. Seed some files
        var testFile = Path.Combine(_paths.Config, "test_setting.json");
        File.WriteAllText(testFile, "{\"key\": \"val\"}");

        _nodeStore.Save(new WikiNode
        {
            NodeId = "n1",
            Title = "Original Node",
            NodeType = WikiNodeType.Concept,
            Summary = "Original Summary",
            Salience = 0.9
        });

        // 2. Create snapshot
        var snapshotPath = _rollbackSystem.CreateSnapshot();
        Assert.True(Directory.Exists(snapshotPath));

        // 3. Mutate current state
        File.WriteAllText(testFile, "{\"key\": \"mutated\"}");
        _nodeStore.Save(new WikiNode
        {
            NodeId = "n1",
            Title = "Mutated Node",
            NodeType = WikiNodeType.Concept,
            Summary = "Mutated Summary",
            Salience = 0.1
        });
        _nodeStore.Save(new WikiNode
        {
            NodeId = "n2",
            Title = "New Node",
            NodeType = WikiNodeType.Concept,
            Summary = "New Summary",
            Salience = 0.5
        });

        // Verify current state is indeed mutated
        var node1 = _nodeStore.Load("n1");
        Assert.Equal("Mutated Node", node1.Title);
        Assert.NotNull(_nodeStore.Load("n2"));

        // 4. Restore from snapshot
        _rollbackSystem.RestoreSnapshot(snapshotPath);

        // Verify state is completely reverted
        var configJson = File.ReadAllText(testFile);
        Assert.Equal("{\"key\": \"val\"}", configJson);

        var restoredNode1 = _nodeStore.Load("n1");
        Assert.Equal("Original Node", restoredNode1.Title);
        Assert.Null(_nodeStore.Load("n2"));
    }

    [Fact]
    public void SnapshotRollbackSystem_PrunesOldSnapshots()
    {
        var backupsDir = Path.Combine(_paths.Root, "backups");
        Directory.CreateDirectory(backupsDir);

        // Manually create 6 snapshot directories
        for (int i = 0; i < 6; i++)
        {
            var snapPath = Path.Combine(backupsDir, $"snapshot_20260524_12000{i}");
            Directory.CreateDirectory(snapPath);
        }

        var snapshotsBefore = Directory.GetDirectories(backupsDir, "snapshot_*");
        Assert.Equal(6, snapshotsBefore.Length);

        // Prune to keep 3
        _rollbackSystem.PruneOldSnapshots(3);

        var snapshotsAfter = Directory.GetDirectories(backupsDir, "snapshot_*");
        Assert.Equal(3, snapshotsAfter.Length);
    }

    [Fact]
    public void AtomicUpgradeCoordinator_Preflight_Succeeds()
    {
        var preflight = _upgradeCoordinator.RunPreflight("1.1.0", 2);
        Assert.True(preflight.Success);
        Assert.Equal("1.1.0", preflight.TargetSystemVersion);
        Assert.Equal(2, preflight.TargetSchemaVersion);
        Assert.True(preflight.BehavioralTrustDiff.Count > 0);
    }

    [Fact]
    public void AtomicUpgradeCoordinator_UpgradeExecution_Works()
    {
        _nodeStore.Save(new WikiNode
        {
            NodeId = "orig_node",
            Title = "Test Node",
            NodeType = WikiNodeType.Concept,
            Summary = "Test Summary"
        });

        var result = _upgradeCoordinator.ExecuteUpgrade("1.1.0", 2);
        
        Assert.True(result.Success);
        Assert.Equal("1.1.0", _versionManager.GetCurrentVersionInfo().SystemVersion);
        Assert.Equal(2, _versionManager.GetCurrentVersionInfo().SchemaVersion);
        
        // Assert the node still exists post-migration
        var node = _nodeStore.Load("orig_node");
        Assert.NotNull(node);
        Assert.Equal("Test Node", node.Title);
    }
}
