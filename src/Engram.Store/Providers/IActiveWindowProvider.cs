namespace Engram.Store.Providers;

/// <summary>
/// Provider interface for active window tracking.
/// Returns information about the currently focused application.
/// </summary>
public interface IActiveWindowProvider : IDisposable
{
    /// <summary>Get info about the currently active/focused window.</summary>
    ActiveWindowInfo? GetActiveWindowInfo();
}

/// <summary>
/// Information about the currently active window.
/// </summary>
public class ActiveWindowInfo
{
    /// <summary>Process name (e.g., "chrome", "code", "explorer").</summary>
    public string ProcessName { get; init; } = string.Empty;

    /// <summary>Window title text.</summary>
    public string WindowTitle { get; init; } = string.Empty;

    /// <summary>Full path to the executable.</summary>
    public string? ExecutablePath { get; init; }

    /// <summary>Process ID.</summary>
    public int ProcessId { get; init; }

    /// <summary>When this info was captured.</summary>
    public DateTimeOffset CapturedAt { get; init; }
}
