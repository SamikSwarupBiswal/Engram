namespace Engram.Store.Providers;

/// <summary>
/// Provider interface for file system watching.
/// Implementations monitor directories for file changes and raise events.
/// </summary>
public interface IFileCaptureProvider : IDisposable
{
    /// <summary>Whether the provider is currently watching.</summary>
    bool IsWatching { get; }

    /// <summary>Start watching the configured directories.</summary>
    void Start();

    /// <summary>Stop watching. Safe to call multiple times.</summary>
    void Stop();

    /// <summary>Raised when a file change is detected (after debouncing).</summary>
    event EventHandler<FileChangeEventArgs>? FileChanged;
}

/// <summary>
/// Event args for file change events.
/// </summary>
public class FileChangeEventArgs : EventArgs
{
    public string FilePath { get; init; } = string.Empty;
    public FileChangeType ChangeType { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

public enum FileChangeType
{
    Created,
    Changed,
    Renamed,
    Deleted
}
