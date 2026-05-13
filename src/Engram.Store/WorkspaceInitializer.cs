namespace Engram.Store;

/// <summary>
/// Initializes the .engram workspace directory structure.
/// Idempotent: safe to run multiple times.
/// </summary>
public class WorkspaceInitializer
{
    /// <summary>
    /// Creates the .engram workspace and all required subdirectories.
    /// Safe to call multiple times — existing directories are not modified.
    /// </summary>
    public void Initialize(WorkspacePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        foreach (var path in paths.GetAllRequiredPaths())
        {
            Directory.CreateDirectory(path);
        }
    }

    /// <summary>
    /// Returns true if the workspace exists and all required directories are present.
    /// </summary>
    public bool IsInitialized(WorkspacePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        // All directories must exist, including root
        return paths.GetAllRequiredPaths().All(Directory.Exists);
    }
}
