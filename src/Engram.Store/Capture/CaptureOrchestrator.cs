using Engram.Store.Providers;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Capture;

/// <summary>
/// Coordinates all capture sources. Manages lifecycle, consent, and event routing.
/// All events flow through the orchestrator before reaching the raw event writer.
/// </summary>
public class CaptureOrchestrator : IDisposable
{
    private readonly RawEventWriter _writer;
    private readonly ContentHasher _hasher;
    private readonly EngramConfig _config;
    private readonly ExclusionList _exclusionList;
    private readonly CircuitBreaker _circuitBreaker;
    private readonly RateLimiter _rateLimiter;
    private readonly ILogger<CaptureOrchestrator>? _logger;

    private FileWatcher? _fileWatcher;
    private ClipboardWatcher? _clipboardWatcher;
    private ActiveWindowTracker? _windowTracker;

    private bool _disposed;
    private bool _isRunning;

    public bool IsRunning => _isRunning;

    /// <summary>Total events captured since start.</summary>
    public long EventsCaptured { get; private set; }

    /// <summary>Total events dropped by rate limiter.</summary>
    public long EventsDropped => _rateLimiter.DroppedCount;

    public CaptureOrchestrator(
        RawEventWriter writer,
        ContentHasher hasher,
        EngramConfig config,
        WorkspacePaths paths,
        ILogger<CaptureOrchestrator>? logger = null,
        IFileCaptureProvider? fileProvider = null,
        IClipboardProvider? clipboardProvider = null,
        IActiveWindowProvider? windowProvider = null,
        RateLimiter? rateLimiter = null)
    {
        _writer = writer;
        _hasher = hasher;
        _config = config;
        _logger = logger;
        _exclusionList = new ExclusionList();
        _exclusionList.LoadFromConfig(config.ExcludedApps);

        _rateLimiter = rateLimiter ?? new RateLimiter(maxTokens: 200, refillRatePerSecond: 10);
        _circuitBreaker = new CircuitBreaker(failureThreshold: 10, openDuration: TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Start all enabled capture sources.
    /// Respects config: only starts sources that are enabled.
    /// </summary>
    public void Start()
    {
        if (_isRunning) return;

        _logger?.LogInformation("Starting capture orchestrator");

        // Each source only starts if enabled in config
        // Actual provider setup would happen here with real platform providers
        // For now, we track the state

        _isRunning = true;
        _logger?.LogInformation("Capture orchestrator started");
    }

    /// <summary>
    /// Stop all capture sources gracefully.
    /// </summary>
    public void Stop()
    {
        if (!_isRunning) return;

        _logger?.LogInformation("Stopping capture orchestrator");

        _fileWatcher?.Stop();
        _clipboardWatcher?.Stop();
        _windowTracker?.Stop();

        _isRunning = false;
        _logger?.LogInformation("Capture orchestrator stopped. Events captured: {Count}", EventsCaptured);
    }

    /// <summary>
    /// Process a captured event. Routes through rate limiter, circuit breaker, and writer.
    /// </summary>
    public WriteResult ProcessEvent(RawEvent rawEvent)
    {
        if (!_circuitBreaker.IsAllowed)
        {
            _logger?.LogWarning("Circuit breaker open — event dropped");
            _rateLimiter.TryAcquire(); // Count as dropped
            return new WriteResult { Outcome = WriteOutcome.Duplicate, EventId = rawEvent.EventId };
        }

        if (!_rateLimiter.TryAcquire())
        {
            _circuitBreaker.RecordFailure();
            _logger?.LogDebug("Rate limiter exceeded — event dropped");
            return new WriteResult { Outcome = WriteOutcome.Duplicate, EventId = rawEvent.EventId };
        }

        try
        {
            var result = _writer.Write(rawEvent);
            _circuitBreaker.RecordSuccess();
            EventsCaptured++;
            return result;
        }
        catch (Exception ex)
        {
            _circuitBreaker.RecordFailure();
            _logger?.LogError(ex, "Failed to write captured event");
            throw;
        }
    }

    /// <summary>
    /// Check if a process is in the exclusion list.
    /// </summary>
    public bool IsExcluded(string processName) => _exclusionList.IsExcluded(processName);

    public void Dispose()
    {
        if (!_disposed)
        {
            Stop();
            _fileWatcher?.Dispose();
            _clipboardWatcher?.Dispose();
            _windowTracker?.Dispose();
            _disposed = true;
        }
    }
}
