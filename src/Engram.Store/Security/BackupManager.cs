using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Security;

/// <summary>
/// Handles daily metadata ZIP backups of `.engram/` configuration and wiki files,
/// and enforces a 7-day retention limit.
/// </summary>
public class BackupManager
{
    private readonly WorkspacePaths _paths;
    private readonly string _backupsDir;
    private readonly ILogger<BackupManager>? _logger;
    private readonly object _lock = new();

    public BackupManager(WorkspacePaths paths, ILogger<BackupManager>? logger = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _backupsDir = Path.Combine(paths.Root, "backups");
        _logger = logger;
    }

    /// <summary>
    /// Performs a metadata backup (Wiki and Config folders) in a ZIP file
    /// and prunes backups keeping only the 7 most recent.
    /// </summary>
    public string CreateBackup()
    {
        lock (_lock)
        {
            Directory.CreateDirectory(_backupsDir);

            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
            var uniqueId = Guid.NewGuid().ToString("n")[..6];
            var backupZipPath = Path.Combine(_backupsDir, $"engram_backup_{timestamp}_{uniqueId}.zip");
            var tempDir = Path.Combine(Path.GetTempPath(), $"engram_backup_temp_{Guid.NewGuid():N}");

            try
            {
                // 1. Prepare temp directory structure
                var tempConfig = Path.Combine(tempDir, "config");
                var tempWiki = Path.Combine(tempDir, "wiki");

                Directory.CreateDirectory(tempConfig);
                Directory.CreateDirectory(tempWiki);

                // 2. Copy Config files recursively
                if (Directory.Exists(_paths.Config))
                {
                    CopyDirectory(_paths.Config, tempConfig);
                }

                // 3. Copy Wiki files recursively
                if (Directory.Exists(_paths.Wiki))
                {
                    CopyDirectory(_paths.Wiki, tempWiki);
                }

                // 4. Compress to ZIP
                ZipFile.CreateFromDirectory(tempDir, backupZipPath);
                _logger?.LogInformation("Backup created successfully: {Path}", backupZipPath);

                // 5. Prune old backups
                PruneOldBackups();

                return backupZipPath;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to create metadata backup.");
                throw;
            }
            finally
            {
                // Clean up temporary workspace
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, recursive: true);
                    }
                }
                catch { }
            }
        }
    }

    private void PruneOldBackups()
    {
        if (!Directory.Exists(_backupsDir)) return;

        var zipFiles = Directory.GetFiles(_backupsDir, "engram_backup_*.zip")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.CreationTimeUtc)
            .ToList();

        if (zipFiles.Count > 7)
        {
            var filesToDelete = zipFiles.Skip(7);
            foreach (var file in filesToDelete)
            {
                try
                {
                    _logger?.LogInformation("Pruning old backup: {Name}", file.Name);
                    file.Delete();
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to prune old backup: {Path}", file.FullName);
                }
            }
        }
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var targetFile = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, targetFile, overwrite: true);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var targetSubDir = Path.Combine(targetDir, Path.GetFileName(subDir));
            CopyDirectory(subDir, targetSubDir);
        }
    }
}
