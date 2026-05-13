using Engram.Store.Providers;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Capture;

/// <summary>
/// Active window tracking via polling.
/// Wraps IActiveWindowProvider with periodic polling and caching.
/// </summary>
public class ActiveWindowTracker : IDisposable
{
    private readonly IActiveWindowProvider _provider;
    private readonly TimeSpan _pollInterval;
    private readonly ILogger<ActiveWindowTracker>? _logger;
    private Timer? _pollTimer;
    private ActiveWindowInfo? _currentWindow;
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>
    /// The most recently detected active window info.
    /// </summary>
    public ActiveWindowInfo? CurrentWindow
    {
        get { lock (_lock) return _currentWindow; }
    }

    /// <summary>Raised when the active window changes.</summary>
    public event EventHandler<ActiveWindowInfo>? WindowChanged;

    public ActiveWindowTracker(
        IActiveWindowProvider provider,
        TimeSpan? pollInterval = null,
        ILogger<ActiveWindowTracker>? logger = null)
    {
        _provider = provider;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(1);
        _logger = logger;
    }

    public void Start()
    {
        _pollTimer = new Timer(PollWindow, null, TimeSpan.Zero, _pollInterval);
        _logger?.LogInformation("Active window tracking started (interval={Interval}ms)", _pollInterval.TotalMilliseconds);
    }

    public void Stop()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;
        _logger?.LogInformation("Active window tracking stopped");
    }

    private void PollWindow(object? state)
    {
        try
        {
            var info = _provider.GetActiveWindowInfo();
            if (info == null) return;

            var previous = _currentWindow;
            lock (_lock)
            {
                _currentWindow = info;
            }

            // Raise event if window changed
            if (previous == null ||
                previous.ProcessName != info.ProcessName ||
                previous.WindowTitle != info.WindowTitle)
            {
                _logger?.LogDebug("Active window changed: {Process} - {Title}", info.ProcessName, info.WindowTitle);
                WindowChanged?.Invoke(this, info);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error polling active window");
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Stop();
            _disposed = true;
        }
    }
}
