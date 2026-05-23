using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Engram.Store.Ingestion;
using Engram.Store.Security;
using Engram.Store.Metabolism;

namespace Engram.Store.Tests;

public class CausalReconcilerTests
{
    [Fact]
    public void CausalReconciler_HealsUncommittedWrites()
    {
        // Arrange
        using var workspace = new TempWorkspace();
        var hasher = new ContentHasher();
        var reconciler = new CausalReconciler(workspace.Paths, hasher);

        // Create standard folders
        Directory.CreateDirectory(workspace.Paths.Raw);

        var rawEvent = TestEvents.Create(text: "Uncommitted Event Content");
        var hash = hasher.ComputeHash(rawEvent);
        var dateFolder = rawEvent.CapturedAt.ToString("yyyy-MM-dd");
        var dateDir = Path.Combine(workspace.Paths.Raw, dateFolder);
        var targetFile = Path.Combine(dateDir, $"{rawEvent.EventId}.json");
        var tmpFile = targetFile + ".tmp";

        // Setup 1: WAL write intent but no commit
        using (var wal = new WriteAheadLog(workspace.Paths.Raw))
        {
            wal.LogWrite(rawEvent.EventId, hash, targetFile);
        }

        // Write the .tmp file (simulating crashed rename step)
        Directory.CreateDirectory(dateDir);
        var json = JsonSerializer.Serialize(rawEvent, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        File.WriteAllText(tmpFile, json);

        // Act: Run reconciler
        int healed = reconciler.Reconcile();

        // Assert
        Assert.Equal(1, healed);
        Assert.True(File.Exists(targetFile));
        Assert.False(File.Exists(tmpFile));

        // Check that index has the entry
        using var hashIndex = new HashIndex(workspace.Paths.Raw);
        Assert.True(hashIndex.TryGet(hash, out var indexedPath));
        Assert.Equal(targetFile, indexedPath);
    }

    [Fact]
    public void CausalReconciler_DeletesCorruptedUncommittedWrites()
    {
        // Arrange
        using var workspace = new TempWorkspace();
        var hasher = new ContentHasher();
        var reconciler = new CausalReconciler(workspace.Paths, hasher);

        Directory.CreateDirectory(workspace.Paths.Raw);

        var rawEvent = TestEvents.Create(text: "Corrupted Content");
        var dateFolder = rawEvent.CapturedAt.ToString("yyyy-MM-dd");
        var dateDir = Path.Combine(workspace.Paths.Raw, dateFolder);
        var targetFile = Path.Combine(dateDir, $"{rawEvent.EventId}.json");
        var tmpFile = targetFile + ".tmp";

        // WAL entry
        using (var wal = new WriteAheadLog(workspace.Paths.Raw))
        {
            wal.LogWrite(rawEvent.EventId, "INVALID_HASH_VAL", targetFile);
        }

        Directory.CreateDirectory(dateDir);
        File.WriteAllText(tmpFile, "CORRUPTED_JSON_OR_HASH");

        // Act
        int healed = reconciler.Reconcile();

        // Assert
        Assert.Equal(0, healed);
        Assert.False(File.Exists(targetFile));
        Assert.False(File.Exists(tmpFile)); // Corrupted tmp file cleaned up
    }

    [Fact]
    public void BackupManager_CreatesZIPBackups_AndEnforces7DayPruning()
    {
        // Arrange
        using var workspace = new TempWorkspace();
        var manager = new BackupManager(workspace.Paths);

        // Create some sample data in config/ and wiki/ to backup
        Directory.CreateDirectory(workspace.Paths.Config);
        Directory.CreateDirectory(workspace.Paths.Wiki);

        File.WriteAllText(Path.Combine(workspace.Paths.Config, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(workspace.Paths.Wiki, "node_1.md"), "# Welcome to Engram");

        // Act 1: create backups
        var path = manager.CreateBackup();
        Assert.True(File.Exists(path));
        Assert.Contains(".zip", path);

        // Act 2: generate 10 backups to trigger pruning limit (>7)
        for (int i = 0; i < 10; i++)
        {
            // Pause slightly so file times differ (creation times)
            System.Threading.Thread.Sleep(10);
            manager.CreateBackup();
        }

        // Assert: only 7 ZIP files should remain in backups folder
        var backupsDir = Path.Combine(workspace.Paths.Root, "backups");
        var remainingZipFiles = Directory.GetFiles(backupsDir, "engram_backup_*.zip");
        Assert.Equal(7, remainingZipFiles.Length);
    }

    [Fact]
    public async Task HomeostasisController_HandlesCognitiveTriage_DebtTracking_AndRecovery()
    {
        // Arrange
        var controller = new HomeostasisController();

        // 1. Invariant test: Absolute priority layers must always run
        Assert.True(controller.CanExecuteTask("Constitutional safeguards"));
        Assert.True(controller.CanExecuteTask("Human override systems"));

        // 2. Optimal state check
        Assert.Equal(HomeostasisState.Optimal, controller.CurrentState);
        Assert.Equal("System running at full cognitive fidelity.", controller.GetSemanticStateMessage());
        Assert.True(controller.CanExecuteTask("Background reflection"));

        // 3. Congestion state check
        controller.CpuLoad = 0.6; // triggers Congested state
        controller.Tick(elapsedSeconds: 1.0);
        Assert.Equal(HomeostasisState.Congested, controller.CurrentState);
        Assert.Equal("Prioritizing active tasks to maintain responsiveness.", controller.GetSemanticStateMessage());

        // Under congestion, background reflection should fail and go to debt queue
        Assert.False(controller.CanExecuteTask("Background reflection"));
        Assert.Equal(1, controller.CognitiveDebtCount);

        // 4. Critical state check
        controller.MemoryPressure = 0.9; // triggers Critical state
        controller.Tick(elapsedSeconds: 1.0);
        Assert.Equal(HomeostasisState.Critical, controller.CurrentState);
        Assert.Equal("Background cognition temporarily minimized while core safeguards remain active.", controller.GetSemanticStateMessage());

        // Verify floor detection (stuck in non-optimal state)
        await Task.Delay(5100);
        controller.Tick(elapsedSeconds: 1.0);
        Assert.True(controller.FloorDetected);

        // 5. Exponential recovery dynamics check
        controller.CpuLoad = 0.1;
        controller.MemoryPressure = 0.1;

        // Tick once - recovery factor increases slowly, stays in Critical/degraded state initially
        controller.Tick(elapsedSeconds: 1.0);
        Assert.True(controller.RecoveryFactor > 0.2);
        Assert.True(controller.RecoveryFactor < 0.95);
        Assert.Equal(HomeostasisState.Critical, controller.CurrentState);

        // Tick again with a large elapsed time to finalize recovery
        controller.Tick(elapsedSeconds: 10.0);
        Assert.Equal(1.0, controller.RecoveryFactor);
        Assert.Equal(HomeostasisState.Optimal, controller.CurrentState);

        // Exposing optimal allows pulling debt from queue
        var deferredTask = controller.DequeueCognitiveDebt();
        Assert.Equal("Background reflection", deferredTask);
        Assert.Equal(0, controller.CognitiveDebtCount);
    }
}
