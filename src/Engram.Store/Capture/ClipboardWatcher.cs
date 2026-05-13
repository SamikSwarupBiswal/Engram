using System.Security.Cryptography;
using System.Text;
using Engram.Store.Providers;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Capture;

/// <summary>
/// Clipboard monitoring via polling.
/// Features: content hash change detection, excluded app enforcement, rate limiting.
/// Uses IClipboardProvider for platform-specific clipboard access.
/// </summary>
public class ClipboardWatcher : IClipboardProvider
{
    private readonly IClipboardProvider _provider;
    private readonly ExclusionList _exclusionList;
    private readonly IActiveWindowProvider _activeWindowProvider;
    private readonly RateLimiter _rateLimiter;
    private readonly ILogger<ClipboardWatcher>? _logger;
    private readonly TimeSpan _pollInterval;
    private Timer? _pollTimer;
    private string _lastContentHash = string.Empty;
    private bool _disposed;
    private bool _isMonitoring;

    public bool IsMonitoring => _isMonitoring;
    public event EventHandler<ClipboardChangedEventArgs>? ClipboardChanged;

    public ClipboardWatcher(
        IClipboardProvider provider,
        ExclusionList exclusionList,
        IActiveWindowProvider activeWindowProvider,
        RateLimiter rateLimiter,
        TimeSpan? pollInterval = null,
        ILogger<ClipboardWatcher>? logger = null)
    {
        _provider = provider;
        _exclusionList = exclusionList;
        _activeWindowProvider = activeWindowProvider;
        _rateLimiter = rateLimiter;
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(500);
        _logger = logger;
    }

    public void Start()
    {
        if (_isMonitoring) return;

        _pollTimer = new Timer(PollClipboard, null, TimeSpan.Zero, _pollInterval);
        _isMonitoring = true;
        _logger?.LogInformation("Clipboard monitoring started (interval={Interval}ms)", _pollInterval.TotalMilliseconds);
    }

    public void Stop()
    {
        if (!_isMonitoring) return;

        _pollTimer?.Dispose();
        _pollTimer = null;
        _isMonitoring = false;
        _logger?.LogInformation("Clipboard monitoring stopped");
    }

    public ClipboardContent? GetCurrentContent()
    {
        return _provider.GetCurrentContent();
    }

    private void PollClipboard(object? state)
    {
        try
        {
            // Check if active window is excluded
            var activeWindow = _activeWindowProvider.GetActiveWindowInfo();
            if (activeWindow != null && _exclusionList.IsExcluded(activeWindow.ProcessName))
            {
                _logger?.LogDebug("Clipboard skipped: active window {Process} is excluded", activeWindow.ProcessName);
                return;
            }

            var content = _provider.GetCurrentContent();
            if (content == null || string.IsNullOrEmpty(content.Text))
                return;

            // Content hash change detection
            var hash = ComputeContentHash(content.Text);
            if (hash == _lastContentHash)
                return; // No change

            _lastContentHash = hash;

            // Rate limit
            if (!_rateLimiter.TryAcquire())
            {
                _logger?.LogDebug("Clipboard event rate limited");
                return;
            }

            _logger?.LogInformation("Clipboard content changed (hash={Hash})", hash[..16]);

            ClipboardChanged?.Invoke(this, new ClipboardChangedEventArgs
            {
                Content = new ClipboardContent
                {
                    Text = content.Text,
                    ContentHash = hash,
                    CapturedAt = DateTimeOffset.UtcNow
                },
                ActiveWindow = activeWindow
            });
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error polling clipboard");
        }
    }

    private static string ComputeContentHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Stop();
            _provider.Dispose();
            _disposed = true;
        }
    }
}
