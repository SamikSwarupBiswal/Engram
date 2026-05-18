using Microsoft.Extensions.Logging;

namespace Engram.Store.Perception;

/// <summary>
/// Detects UI state changes between screen frames.
/// Compares consecutive frames to find:
/// - New windows opened/closed
/// - Text content changes
/// - Notifications appeared
/// - Active window switches
/// </summary>
public class UiStateDetector
{
    private readonly ILogger<UiStateDetector>? _logger;
    private ScreenFrame? _previousFrame;
    private string _lastWindowTitle = string.Empty;
    private string _lastWindowProcess = string.Empty;

    public UiStateDetector(ILogger<UiStateDetector>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Analyze a new frame against the previous one.
    /// Returns detected state changes.
    /// </summary>
    public List<UiStateChange> DetectChanges(ScreenFrame current)
    {
        var changes = new List<UiStateChange>();

        if (_previousFrame == null)
        {
            _previousFrame = current;
            _lastWindowTitle = current.ActiveWindowTitle;
            _lastWindowProcess = current.ActiveWindowProcess;
            return changes;
        }

        // Detect process change FIRST (before title update)
        if (current.ActiveWindowProcess != _lastWindowProcess && !string.IsNullOrEmpty(current.ActiveWindowProcess))
        {
            changes.Add(new UiStateChange
            {
                Type = "app_switch",
                Description = $"Active app: {current.ActiveWindowProcess}",
                NewValue = current.ActiveWindowProcess
            });
        }

        // Detect active window change
        if (current.ActiveWindowTitle != _lastWindowTitle)
        {
            changes.Add(new UiStateChange
            {
                Type = "window_switch",
                Description = $"Switched from '{_lastWindowTitle}' to '{current.ActiveWindowTitle}'",
                OldValue = _lastWindowTitle,
                NewValue = current.ActiveWindowTitle
            });
        }

        // Update state AFTER detection
        _lastWindowTitle = current.ActiveWindowTitle;
        _lastWindowProcess = current.ActiveWindowProcess;

        // Detect text content changes (if OCR available)
        if (!string.IsNullOrEmpty(current.ExtractedText) &&
            !string.IsNullOrEmpty(_previousFrame.ExtractedText) &&
            current.ExtractedText != _previousFrame.ExtractedText)
        {
            var diff = ComputeTextDiff(_previousFrame.ExtractedText, current.ExtractedText);
            if (diff.Length > 10) // Significant change
            {
                changes.Add(new UiStateChange
                {
                    Type = "text_change",
                    Description = $"Content changed in {current.ActiveWindowTitle}",
                    OldValue = _previousFrame.ExtractedText.Length > 100 ? _previousFrame.ExtractedText[..100] + "..." : _previousFrame.ExtractedText,
                    NewValue = current.ExtractedText.Length > 100 ? current.ExtractedText[..100] + "..." : current.ExtractedText
                });
            }
        }

        _previousFrame = current;
        return changes;
    }

    /// <summary>
    /// Check if a frame contains a notification-like popup.
    /// Heuristic: small window with specific title patterns.
    /// </summary>
    public bool IsNotification(ScreenFrame frame)
    {
        var title = frame.ActiveWindowTitle.ToLowerInvariant();
        var notificationPatterns = new[] { "notification", "alert", "toast", "reminder", "message from" };
        return notificationPatterns.Any(p => title.Contains(p));
    }

    /// <summary>
    /// Check if the user is idle (same window for extended period).
    /// </summary>
    public bool IsUserIdle(TimeSpan threshold)
    {
        if (_previousFrame == null) return false;
        return (DateTimeOffset.UtcNow - _previousFrame.Timestamp) > threshold;
    }

    private static string ComputeTextDiff(string oldText, string newText)
    {
        // Simple diff: find new content
        if (newText.Length > oldText.Length)
            return newText[oldText.Length..];
        if (oldText.Length > newText.Length)
            return oldText[newText.Length..];
        return string.Empty;
    }

    public void Reset()
    {
        _previousFrame = null;
        _lastWindowTitle = string.Empty;
        _lastWindowProcess = string.Empty;
    }
}
