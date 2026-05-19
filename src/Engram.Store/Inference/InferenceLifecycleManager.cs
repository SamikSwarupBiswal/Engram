using System.Collections.Concurrent;

namespace Engram.Store.Inference;

/// <summary>
/// The single authoritative source of truth for inference system lifecycle.
/// 
/// All state transitions go through this manager.
/// No other component should independently track inference readiness.
/// 
/// State machine:
///   Starting → DetectingBackend → BackendReady → DownloadingModel → LoadingModel → Ready
///                   ↓                  ↓               ↓               ↓
///                 Error              Error           Error           Error
///                   ↓
///                Degraded (CPU fallback attempted)
/// </summary>
public sealed class InferenceLifecycleManager : IDisposable
{
    private readonly InferenceLogger _log = InferenceLogger.Instance;
    private readonly object _lock = new();
    private readonly DateTime _startTime = DateTime.UtcNow;

    // State
    private InferenceState _state = InferenceState.Starting;
    private string? _error;
    private string? _backend;
    private string? _modelName;
    private bool _modelLoaded;
    private double _progress; // 0-100 for download/load progress
    private int _retryCount;
    private readonly List<string> _stateHistory = new();
    private readonly ConcurrentDictionary<string, string> _metadata = new();

    // Components (injected after construction)
    private GpuDetector? _gpuDetector;
    private ModelManager? _modelManager;
    private LocalInferenceEngine? _localEngine;
    private InferenceRouter? _inferenceRouter;
    private Func<Task>? _downloadFunc;

    // Background task tracking
    private Task? _initTask;
    private CancellationTokenSource _cts = new();

    public event Action<InferenceState>? StateChanged;

    // ── Public state accessors ──

    public InferenceState State
    {
        get { lock (_lock) return _state; }
    }

    public HealthResponse GetHealth()
    {
        lock (_lock)
        {
            return new HealthResponse
            {
                State = _state.ToString(),
                Backend = _backend,
                ModelLoaded = _modelLoaded,
                ModelName = _modelName,
                Progress = _progress,
                Error = _error,
                UptimeSeconds = (int)(DateTime.UtcNow - _startTime).TotalSeconds,
                RetryCount = _retryCount,
                IsReady = _state == InferenceState.Ready,
                CanAcceptRequests = _state == InferenceState.Ready || _state == InferenceState.Degraded,
                StateHistory = _stateHistory.TakeLast(20).ToList(),
                Metadata = new Dictionary<string, string>(_metadata)
            };
        }
    }

    // ── Initialization ──

    /// <summary>
    /// Inject dependencies. Must be called before StartInitialization().
    /// </summary>
    public void Configure(
        GpuDetector gpuDetector,
        ModelManager modelManager,
        LocalInferenceEngine localEngine,
        InferenceRouter inferenceRouter,
        Func<Task>? downloadFunc = null)
    {
        _gpuDetector = gpuDetector;
        _modelManager = modelManager;
        _localEngine = localEngine;
        _inferenceRouter = inferenceRouter;
        _downloadFunc = downloadFunc;
    }

    /// <summary>
    /// Start non-blocking background initialization.
    /// Returns immediately. Poll /health for state.
    /// </summary>
    public void StartInitialization()
    {
        _log.Lifecycle("Starting background initialization");
        _initTask = Task.Run(() => RunInitializationSequence(_cts.Token));
    }

    /// <summary>
    /// The full initialization sequence, runs on background thread.
    /// </summary>
    private async Task RunInitializationSequence(CancellationToken ct)
    {
        try
        {
            // ── Phase 1: Detect backend ──
            TransitionTo(InferenceState.DetectingBackend);
            _log.Gpu("Starting GPU detection...");

            if (_gpuDetector == null)
            {
                SetError("GPU detector not configured");
                return;
            }

            var gpuInfo = _gpuDetector.Detect();
            _backend = gpuInfo.Backend.ToString();
            _metadata["gpuDevice"] = gpuInfo.DeviceName;
            _metadata["gpuVramMb"] = gpuInfo.VramMb.ToString();
            _metadata["gpuLayers"] = gpuInfo.LayerCount.ToString();
            _log.Gpu($"Backend selected: {gpuInfo.Description} (layers={gpuInfo.LayerCount})");

            TransitionTo(InferenceState.BackendReady);

            // ── Phase 2: Check/download model ──
            if (_modelManager == null || _localEngine == null)
            {
                SetError("Model manager or local engine not configured");
                return;
            }

            var config = ModelManager.Phi4Mini;
            _modelName = config.Name;

            if (!_modelManager.IsModelReady(config))
            {
                _log.Model($"Model not found at {ModelManager.GetModelPath(config)}");
                TransitionTo(InferenceState.DownloadingModel);
                _log.Model("Starting model download...");

                // Wait for download to complete if a download function is provided
                if (_downloadFunc != null)
                {
                    await _downloadFunc();
                }
                else
                {
                    // Direct download with progress reporting
                    var progress = new Progress<ModelDownloadProgress>(p =>
                    {
                        lock (_lock) _progress = p.Progress * 100;
                    });

                    await _modelManager.DownloadModelAsync(config, progress, ct);
                }

                if (!_modelManager.IsModelReady(config))
                {
                    SetError("Model download failed or incomplete");
                    return;
                }

                _log.Model("Model download complete");
            }
            else
            {
                _log.Model($"Model found: {ModelManager.GetModelPath(config)}");
            }

            // ── Phase 3: Load model ──
            TransitionTo(InferenceState.LoadingModel);
            _log.Model("Loading model into memory...");
            lock (_lock) _progress = 0;

            // Run LoadModel on a background thread (it's CPU/GPU intensive)
            var loadSuccess = await Task.Run(() => _localEngine.LoadModel(config), ct);

            if (!loadSuccess)
            {
                SetError("Model load failed. Check GPU drivers or available memory.");
                return;
            }

            _modelLoaded = true;
            lock (_lock) _progress = 100;
            _log.Model($"Model loaded: {config.Name} ({config.Description})");
            _log.Model($"Backend: {_backend}, Device: {_metadata.GetValueOrDefault("gpuDevice", "unknown")}");

            // ── Phase 4: Ready ──
            TransitionTo(InferenceState.Ready);
            _log.Lifecycle("=== ENGRAM INFERENCE READY ===");
        }
        catch (OperationCanceledException)
        {
            _log.LifecycleWarn("Initialization cancelled");
            SetError("Initialization cancelled");
        }
        catch (Exception ex)
        {
            _log.LifecycleError("Initialization failed", ex);
            SetError($"Initialization failed: {ex.Message}");
        }
    }

    // ── Manual model load (for user-triggered load after error/download) ──

    /// <summary>
    /// Manually trigger model load. Used when user retries after error
    /// or when model becomes available after download.
    /// </summary>
    public async Task<bool> LoadModelAsync()
    {
        if (_localEngine == null || _modelManager == null)
        {
            _log.LifecycleError("Cannot load model: components not configured");
            return false;
        }

        var config = ModelManager.Phi4Mini;

        if (!_modelManager.IsModelReady(config))
        {
            _log.ModelWarn("Cannot load: model not downloaded");
            return false;
        }

        TransitionTo(InferenceState.LoadingModel);
        _log.Model("Manual model load triggered...");

        var loadSuccess = await Task.Run(() => _localEngine.LoadModel(config));

        if (loadSuccess)
        {
            _modelLoaded = true;
            lock (_lock) _progress = 100;
            TransitionTo(InferenceState.Ready);
            _log.Model("Model loaded successfully (manual)");
            return true;
        }
        else
        {
            SetError("Model load failed. Check GPU drivers or available memory.");
            return false;
        }
    }

    // ── Unload model ──

    public void UnloadModel()
    {
        _localEngine?.UnloadModel();
        lock (_lock)
        {
            _modelLoaded = false;
            _progress = 0;
        }
        _log.Model("Model unloaded");
        TransitionTo(InferenceState.BackendReady);
    }

    // ── Retry after error ──

    public void Retry()
    {
        lock (_lock)
        {
            if (_state != InferenceState.Error && _state != InferenceState.Degraded)
            {
                _log.LifecycleWarn($"Retry requested but state is {_state}, ignoring");
                return;
            }
            _retryCount++;
            _error = null;
        }

        _log.Lifecycle($"Retry #{_retryCount} starting...");
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        StartInitialization();
    }

    // ── State transition ──

    private void TransitionTo(InferenceState newState)
    {
        lock (_lock)
        {
            var old = _state;
            _state = newState;
            _stateHistory.Add($"{DateTime.UtcNow:HH:mm:ss} {old} → {newState}");
            _log.Lifecycle($"State: {old} → {newState}");
        }
        StateChanged?.Invoke(newState);
    }

    private void SetError(string error)
    {
        lock (_lock)
        {
            _error = error;
            _state = InferenceState.Error;
            _stateHistory.Add($"{DateTime.UtcNow:HH:mm:ss} → Error: {error}");
        }
        _log.LifecycleError(error);
        StateChanged?.Invoke(InferenceState.Error);
    }

    // ── Download progress update (called from Program.cs download handler) ──

    public void ReportDownloadProgress(double progress)
    {
        lock (_lock) _progress = progress;
    }

    public void ReportDownloadComplete()
    {
        _log.Model("Download reported complete by external handler");
    }

    public void ReportDownloadError(string error)
    {
        SetError($"Download failed: {error}");
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}

/// <summary>
/// Lifecycle states for the inference system.
/// </summary>
public enum InferenceState
{
    /// <summary>Server just started, nothing initialized yet.</summary>
    Starting,

    /// <summary>Probing GPU hardware, selecting backend.</summary>
    DetectingBackend,

    /// <summary>GPU backend selected and verified.</summary>
    BackendReady,

    /// <summary>Model file not found, downloading.</summary>
    DownloadingModel,

    /// <summary>Model file exists, loading into GPU/CPU memory.</summary>
    LoadingModel,

    /// <summary>Model loaded, ready for inference.</summary>
    Ready,

    /// <summary>Something failed. Check error field.</summary>
    Error,

    /// <summary>Using CPU fallback after GPU failure.</summary>
    Degraded,

    /// <summary>Sidecar process shutting down.</summary>
    Offline
}

/// <summary>
/// Response model for GET /health — the single source of truth.
/// </summary>
public class HealthResponse
{
    public string State { get; init; } = "Starting";
    public string? Backend { get; init; }
    public bool ModelLoaded { get; init; }
    public string? ModelName { get; init; }
    public double Progress { get; init; }
    public string? Error { get; init; }
    public int UptimeSeconds { get; init; }
    public int RetryCount { get; init; }
    public bool IsReady { get; init; }
    public bool CanAcceptRequests { get; init; }
    public List<string> StateHistory { get; init; } = new();
    public Dictionary<string, string> Metadata { get; init; } = new();
}
