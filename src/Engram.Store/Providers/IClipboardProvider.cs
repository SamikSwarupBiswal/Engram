namespace Engram.Store.Providers;

/// <summary>
/// Provider interface for clipboard monitoring.
/// Platform-specific implementations handle actual clipboard access.
/// </summary>
public interface IClipboardProvider : IDisposable
{
    /// <summary>Whether clipboard monitoring is active.</summary>
    bool IsMonitoring { get; }

    /// <summary>Start monitoring clipboard changes.</summary>
    void Start();

    /// <summary>Stop monitoring. Safe to call multiple times.</summary>
    void Stop();

    /// <summary>Get current clipboard content. Returns null if empty or inaccessible.</summary>
    ClipboardContent? GetCurrentContent();

    /// <summary>Raised when clipboard content changes.</summary>
    event EventHandler<ClipboardChangedEventArgs>? ClipboardChanged;
}

public class ClipboardContent
{
    public string Text { get; init; } = string.Empty;
    public string ContentHash { get; init; } = string.Empty;
    public DateTimeOffset CapturedAt { get; init; }
}

public class ClipboardChangedEventArgs : EventArgs
{
    public ClipboardContent Content { get; init; } = new();
    public ActiveWindowInfo? ActiveWindow { get; init; }
}
