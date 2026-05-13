using Microsoft.Extensions.Logging;

namespace Engram.Store;

/// <summary>
/// Initializes the .engram workspace directory structure.
/// Idempotent: safe to run multiple times.
/// Cleans up orphaned .tmp files on startup.
/// </summary>
public class WorkspaceInitializer
{
    private readonly ILogger<WorkspaceInitializer>? _logger;

    public WorkspaceInitializer(ILogger<WorkspaceInitializer>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Creates the .engram workspace and all required subdirectories.
    /// Cleans up orphaned .tmp files from crashed writes.
    /// </summary>
    public void Initialize(WorkspacePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        _logger?.LogInformation("Initializing workspace at {Root}", paths.Root);

        foreach (var path in paths.GetAllRequiredPaths())
        {
            Directory.CreateDirectory(path);
        }

        CleanupOrphanedTempFiles(paths);

        _logger?.LogInformation("Workspace initialized successfully");
    }

    /// <summary>
    /// Returns true if the workspace exists and all required directories are present.
    /// </summary>
    public bool IsInitialized(WorkspacePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return paths.GetAllRequiredPaths().All(Directory.Exists);
    }

    /// <summary>
    /// Scans .engram/raw/ for orphaned .tmp files from crashed writes.
    /// Deletes .tmp files older than 1 hour.
    /// </summary>
    public int CleanupOrphanedTempFiles(WorkspacePaths paths)
    {
        if (!Directory.Exists(paths.Raw))
            return 0;

        var cleaned = 0;
        var cutoff = DateTime.UtcNow.AddHours(-1);

        try
        {
            foreach (var tmpFile in Directory.EnumerateFiles(paths.Raw, "*.tmp", SearchOption.AllDirectories))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(tmpFile) < cutoff)
                    {
                        File.Delete(tmpFile);
                        cleaned++;
                        _logger?.LogInformation("Cleaned orphaned .tmp file: {Path}", tmpFile);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to clean orphaned .tmp file: {Path}", tmpFile);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to scan for orphaned .tmp files");
        }

        if (cleaned > 0)
            _logger?.LogInformation("Cleaned {Count} orphaned .tmp file(s)", cleaned);

        return cleaned;
    }
}
