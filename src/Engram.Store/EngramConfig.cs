namespace Engram.Store;

/// <summary>
/// Configuration model for the Engram workspace.
/// </summary>
public class EngramConfig
{
    public string Version { get; set; } = "1.0.0";
    public bool ClipboardCaptureEnabled { get; set; } = false;
    public bool ActiveWindowCaptureEnabled { get; set; } = false;
    public bool FileWatcherEnabled { get; set; } = false;
    public List<string> ExcludedApps { get; set; } = new();
    public List<string> WatchedPaths { get; set; } = new();
}
