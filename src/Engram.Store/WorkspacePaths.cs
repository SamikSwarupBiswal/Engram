using Engram.Store.Validation;

namespace Engram.Store;

/// <summary>
/// Represents the .engram workspace directory structure.
/// All paths are derived from a configurable root.
/// </summary>
public class WorkspacePaths
{
    public string Root { get; }
    public string Raw => Path.Combine(Root, "raw");
    public string Wiki => Path.Combine(Root, "wiki");
    public string Runs => Path.Combine(Root, "runs");
    public string Config => Path.Combine(Root, "config");
    public string Logs => Path.Combine(Root, "logs");
    public string Archives => Path.Combine(Root, "archives");

    public WorkspacePaths(string root)
    {
        // Validate before assigning
        if (root is null)
            throw new ArgumentNullException(nameof(root));

        InputValidator.ValidateRootPath(root);

        Root = root;
    }

    public string[] GetAllRequiredPaths() => new[]
    {
        Root, Raw, Wiki, Runs, Config, Logs, Archives
    };
}
