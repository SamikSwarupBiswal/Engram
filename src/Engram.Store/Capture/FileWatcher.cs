using Engram.Store.Providers;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Capture;

/// <summary>
/// Production-grade file system watcher.
/// Features: debouncing, rate limiting, self-filtering, error recovery.
/// </summary>
public class FileWatcher : IFileCaptureProvider
{
    private readonly List<string> _watchPaths;
    private readonly string _selfPath;  // .engram path to filter out
    private readonly RateLimiter _rateLimiter;
    private readonly Debouncer<string> _debouncer;
    private readonly ILogger<FileWatcher>? _logger;
    private readonly List<FileSystemWatcher> _watchers = new();
    private bool _disposed;
    private bool _isWatching;

    public bool IsWatching => _isWatching;
    public event EventHandler<FileChangeEventArgs>? FileChanged;

    public FileWatcher(
        IEnumerable<string> watchPaths,
        string selfPath,
        RateLimiter rateLimiter,
        TimeSpan? debounceDelay = null,
        ILogger<FileWatcher>? logger = null)
    {
        _watchPaths = watchPaths.ToList();
        _selfPath = selfPath;
        _rateLimiter = rateLimiter;
        _logger = logger;
        _debouncer = new Debouncer<string>(debounceDelay ?? TimeSpan.FromMilliseconds(500), OnDebouncedFileChange);
    }

    public void Start()
    {
        if (_isWatching) return;

        foreach (var path in _watchPaths)
        {
            if (!Directory.Exists(path))
            {
                _logger?.LogWarning("Watch path does not exist: {Path}", path);
                continue;
            }

            var watcher = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = true,
                InternalBufferSize = 64 * 1024  // 64KB buffer
            };

            watcher.Created += OnWatcherEvent;
            watcher.Changed += OnWatcherEvent;
            watcher.Renamed += OnWatcherRenamed;
            watcher.Deleted += OnWatcherEvent;
            watcher.Error += OnWatcherError;

            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);

            _logger?.LogInformation("Started watching: {Path}", path);
        }

        _isWatching = _watchers.Count > 0;
    }

    public void Stop()
    {
        if (!_isWatching) return;

        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();
        _isWatching = false;

        _logger?.LogInformation("Stopped all file watchers");
    }

    private void OnWatcherEvent(object sender, FileSystemEventArgs e)
    {
        ProcessFileChange(e.FullPath, e.ChangeType.ToString());
    }

    private void OnWatcherRenamed(object sender, RenamedEventArgs e)
    {
        ProcessFileChange(e.FullPath, "Renamed");
    }

    private void ProcessFileChange(string filePath, string changeTypeStr)
    {
        // Self-filter: ignore changes under .engram workspace
        if (filePath.StartsWith(_selfPath, StringComparison.OrdinalIgnoreCase))
            return;

        // Rate limit check
        if (!_rateLimiter.TryAcquire())
        {
            _logger?.LogDebug("Rate limited file event: {Path}", filePath);
            return;
        }

        // Debounce: coalesce rapid changes to same file
        _debouncer.Debounce(filePath);
    }

    private void OnDebouncedFileChange(string filePath)
    {
        if (!File.Exists(filePath) && !Directory.Exists(filePath))
            return;

        _logger?.LogDebug("File change detected: {Path}", filePath);

        FileChanged?.Invoke(this, new FileChangeEventArgs
        {
            FilePath = filePath,
            ChangeType = FileChangeType.Changed,
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _logger?.LogError(e.GetException(), "FileSystemWatcher error — attempting recovery");

        // Restart the watcher
        Stop();
        Thread.Sleep(1000);
        Start();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Stop();
            _debouncer.Dispose();
            _disposed = true;
        }
    }
}
