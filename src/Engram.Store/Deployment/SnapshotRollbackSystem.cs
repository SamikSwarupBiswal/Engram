using System;
using System.IO;
using System.Linq;

namespace Engram.Store.Deployment;

public class SnapshotRollbackSystem
{
    private readonly WorkspacePaths _paths;
    private readonly string _backupsDirectory;

    public SnapshotRollbackSystem(WorkspacePaths paths)
    {
        _paths = paths;
        _backupsDirectory = Path.Combine(paths.Root, "backups");
        Directory.CreateDirectory(_backupsDirectory);
    }

    public string CreateSnapshot()
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
        var snapshotDir = Path.Combine(_backupsDirectory, $"snapshot_{timestamp}");
        Directory.CreateDirectory(snapshotDir);

        try
        {
            // Copy Raw, Wiki, Runs, Config, Logs, Archives
            var foldersToBackup = new[] { _paths.Raw, _paths.Wiki, _paths.Runs, _paths.Config, _paths.Logs, _paths.Archives };
            foreach (var folder in foldersToBackup)
            {
                if (Directory.Exists(folder))
                {
                    var destFolder = Path.Combine(snapshotDir, Path.GetFileName(folder));
                    CopyDirectory(folder, destFolder);
                }
            }
            return snapshotDir;
        }
        catch (Exception ex)
        {
            // Clean up partial snapshot
            if (Directory.Exists(snapshotDir))
            {
                Directory.Delete(snapshotDir, recursive: true);
            }
            throw new InvalidOperationException("Failed to create snapshot.", ex);
        }
    }

    public void RestoreSnapshot(string snapshotPath)
    {
        if (!Directory.Exists(snapshotPath))
        {
            throw new DirectoryNotFoundException($"Snapshot directory '{snapshotPath}' not found.");
        }

        try
        {
            // Delete current operational directories
            var foldersToRestore = new[] { _paths.Raw, _paths.Wiki, _paths.Runs, _paths.Config, _paths.Logs, _paths.Archives };
            foreach (var folder in foldersToRestore)
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }

            // Restore from snapshot
            foreach (var folder in foldersToRestore)
            {
                var folderName = Path.GetFileName(folder);
                var srcFolder = Path.Combine(snapshotPath, folderName);
                if (Directory.Exists(srcFolder))
                {
                    CopyDirectory(srcFolder, folder);
                }
                else
                {
                    // Re-create empty if not in snapshot
                    Directory.CreateDirectory(folder);
                }
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to restore snapshot.", ex);
        }
    }

    public void PruneOldSnapshots(int keepCount = 5)
    {
        try
        {
            if (!Directory.Exists(_backupsDirectory)) return;

            var snapshots = Directory.GetDirectories(_backupsDirectory, "snapshot_*")
                .Select(d => new DirectoryInfo(d))
                .OrderByDescending(d => d.CreationTimeUtc)
                .Skip(keepCount)
                .ToList();

            foreach (var snap in snapshots)
            {
                snap.Delete(recursive: true);
            }
        }
        catch
        {
            // Fail silently on background pruning
        }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists) return;

        Directory.CreateDirectory(destinationDir);

        foreach (var file in dir.GetFiles())
        {
            var targetFilePath = Path.Combine(destinationDir, file.Name);
            file.CopyTo(targetFilePath, overwrite: true);
        }

        foreach (var subDir in dir.GetDirectories())
        {
            var newDestinationDir = Path.Combine(destinationDir, subDir.Name);
            CopyDirectory(subDir.FullName, newDestinationDir);
        }
    }
}
