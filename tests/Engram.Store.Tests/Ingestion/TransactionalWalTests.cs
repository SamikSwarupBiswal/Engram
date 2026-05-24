using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using Engram.Store.Ingestion;

namespace Engram.Store.Tests.Ingestion;

public class TransactionalWalTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void Reconcile_RollsBackUncommittedTransaction_NewFileDeleted()
    {
        var rawDir = _workspace.Paths.Raw;
        Directory.CreateDirectory(rawDir);

        var txId = Guid.NewGuid();
        var targetFile = Path.Combine(rawDir, "new_file.json");

        // Simulate creating a new file in transaction but crashing before commit
        File.WriteAllText(targetFile, "{ \"data\": \"new\" }");

        using (var wal = new WriteAheadLog(rawDir))
        {
            var ops = new List<WalTransactionOperation>
            {
                new()
                {
                    FilePath = targetFile,
                    Hash = "new_hash",
                    PreviousContent = null, // Created new
                    NewContent = "{ \"data\": \"new\" }"
                }
            };
            wal.LogTransactionStart(txId, ops);
            // Crashing here - no LogTransactionCommit!
        }

        // Run reconciler
        var reconciler = new CausalReconciler(_workspace.Paths, new ContentHasher());
        var rolledBackCount = reconciler.Reconcile();

        // Assert target file was deleted as part of rollback
        Assert.Equal(1, rolledBackCount);
        Assert.False(File.Exists(targetFile));
    }

    [Fact]
    public void Reconcile_RollsBackUncommittedTransaction_UpdatedFileRestored()
    {
        var rawDir = _workspace.Paths.Raw;
        Directory.CreateDirectory(rawDir);

        var txId = Guid.NewGuid();
        var targetFile = Path.Combine(rawDir, "existing_file.json");
        var originalContent = "{ \"data\": \"original\" }";
        var updatedContent = "{ \"data\": \"updated\" }";

        // Seed original file
        File.WriteAllText(targetFile, originalContent);

        // Simulate update in transaction but crashing before commit
        File.WriteAllText(targetFile, updatedContent);

        using (var wal = new WriteAheadLog(rawDir))
        {
            var ops = new List<WalTransactionOperation>
            {
                new()
                {
                    FilePath = targetFile,
                    Hash = "updated_hash",
                    PreviousContent = originalContent, // Backup
                    NewContent = updatedContent
                }
            };
            wal.LogTransactionStart(txId, ops);
            // Crashing here - no LogTransactionCommit!
        }

        // Run reconciler
        var reconciler = new CausalReconciler(_workspace.Paths, new ContentHasher());
        var rolledBackCount = reconciler.Reconcile();

        // Assert file content was restored to original
        Assert.Equal(1, rolledBackCount);
        Assert.True(File.Exists(targetFile));
        Assert.Equal(originalContent, File.ReadAllText(targetFile));
    }

    [Fact]
    public void Reconcile_DoesNotRollBackCommittedTransaction()
    {
        var rawDir = _workspace.Paths.Raw;
        Directory.CreateDirectory(rawDir);

        var txId = Guid.NewGuid();
        var targetFile = Path.Combine(rawDir, "file.json");
        var originalContent = "{ \"data\": \"original\" }";
        var updatedContent = "{ \"data\": \"updated\" }";

        File.WriteAllText(targetFile, originalContent);
        File.WriteAllText(targetFile, updatedContent);

        using (var wal = new WriteAheadLog(rawDir))
        {
            var ops = new List<WalTransactionOperation>
            {
                new()
                {
                    FilePath = targetFile,
                    Hash = "updated_hash",
                    PreviousContent = originalContent,
                    NewContent = updatedContent
                }
            };
            wal.LogTransactionStart(txId, ops);
            wal.LogTransactionCommit(txId); // Committed successfully!
        }

        // Run reconciler
        var reconciler = new CausalReconciler(_workspace.Paths, new ContentHasher());
        var rolledBackCount = reconciler.Reconcile();

        // Assert no rollback occurred, content remains updated
        Assert.Equal(0, rolledBackCount);
        Assert.True(File.Exists(targetFile));
        Assert.Equal(updatedContent, File.ReadAllText(targetFile));
    }
}
