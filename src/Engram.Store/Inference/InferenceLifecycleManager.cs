using System.Collections.Concurrent;

namespace Engram.Store.Inference;

/// <summary>
/// The single authoritative source of truth for inference system lifecycle.
/// 
/// All state transitions go through this manager.
/// No other component should independently track inference readiness.
/// 
/// LEGAL TRANSITIONS (enforced):
///   Starting → DetectingBackend, Error, Offline
///   DetectingBackend → BackendReady, Error, Offline
///   BackendReady → DownloadingModel, LoadingModel, Error, Offline
///   DownloadingModel → LoadingModel, Error, Offline
///   LoadingModel → Ready, Error, Offline
///   Ready → LoadingModel (reload), BackendReady (unload), Error, Offline
///   Error → Starting (retry), Offline
///   Degraded → Starting (retry), Offline
///   Offline → (terminal)
/// </summary>
public sealed class InferenceLifecycleManager : IDisposable
{
    private readonly InferenceLogger _log = InferenceLogger.Instance;
    private readonly object _lock = new();
    private readonly DateTime _startTime = DateTime.UtcNow;

    // ── Legal transition map ──
    private static readonly Dictionary<InferenceState, HashSet<InferenceState>> LegalTransitions = new()
    {
        [InferenceState.Starting] = new()
            { InferenceState.DetectingBackend, InferenceState.Error, InferenceState.Offline, InferenceState.SafeMode },
        [InferenceState.DetectingBackend] = new()
            { InferenceState.BackendReady, InferenceState.Error, InferenceState.Offline, InferenceState.SafeMode },
        [InferenceState.BackendReady] = new()
            { InferenceState.DownloadingModel, InferenceState.LoadingModel, InferenceState.Error, InferenceState.Offline },
        [InferenceState.DownloadingModel] = new()
            { InferenceState.LoadingModel, InferenceState.Error, InferenceState.Offline },
        [InferenceState.LoadingModel] = new()
            { InferenceState.Ready, InferenceState.Error, InferenceState.Offline },
        [InferenceState.Ready] = new()
            { InferenceState.LoadingModel, InferenceState.BackendReady, InferenceState.Error, InferenceState.Offline },
        [InferenceState.Error] = new()
            { InferenceState.Starting, InferenceState.Offline },
        [InferenceState.Degraded] = new()
            { InferenceState.Starting, InferenceState.Offline },
        [InferenceState.SafeMode] = new()
            { InferenceState.Starting, InferenceState.Offline },
        [InferenceState.Offline] = new() { } // terminal
    };

    // ── State ──
    private InferenceState _state = InferenceState.Starting;
    private string? _error;
    private string? _backend;
    private string? _modelName;
    private bool _modelLoaded;
    private double _progress; // 0-100 for download/load progress
    private int _retryCount;
    private readonly List<string> _stateHistory = new();
    private readonly ConcurrentDictionary<string, string> _metadata = new();

    // ── Startup metrics ──
    private DateTime? _detectBackendStart;
    private DateTime? _detectBackendEnd;
    private DateTime? _downloadStart;
    private DateTime? _downloadEnd;
    private DateTime? _loadStart;
    private DateTime? _loadEnd;
    private DateTime? _readyTime;
    private DateTime? _errorTime;

    // ── Degraded mode tracking ──
    private string? _degradationReason;
    private InferenceState _degradationFrom;
    private bool _degradationRetryAllowed = true;
    private long _consecutiveCleanupFailures;
    private const int MaxConsecutiveCleanupFailures = 3;

    // ── Components (injected after construction) ──
    private GpuDetector? _gpuDetector;
    private ModelManager? _modelManager;
    private LocalInferenceEngine? _localEngine;
    private InferenceRouter? _inferenceRouter;
    private Func<Task>? _downloadFunc;
    private BackendProbe? _probe;
    private VerdictStore? _verdictStore;

    // ── Background task tracking ──
    private Task? _initTask;
    private CancellationTokenSource _cts = new();

    public event Action<InferenceState>? StateChanged;

    // ══════════════════════════════════════════
    //  PUBLIC STATE ACCESSORS
    // ══════════════════════════════════════════

    public InferenceState State
    {
        get { lock (_lock) return _state; }
    }

    public HealthResponse GetHealth()
    {
        lock (_lock)
        {
            var inferenceTelemetry = _localEngine?.GetTelemetry();
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
                Metadata = new Dictionary<string, string>(_metadata),
                Metrics = GetMetrics(),
                Inference = inferenceTelemetry,
                // Survivability metrics from inference engine
                RuntimeOperational = inferenceTelemetry?.RuntimeOperational ?? false,
                RecentSuccessRate = inferenceTelemetry?.RecentSuccessRate ?? 1.0,
                ConsecutiveFailures = inferenceTelemetry?.ConsecutiveFailures ?? 0,
                GeneratedTokensSinceReset = inferenceTelemetry?.GeneratedTokensSinceReset ?? 0,
                LastSuccessfulInferenceAt = inferenceTelemetry?.LastSuccessfulInferenceAt,
                RuntimeDegraded = inferenceTelemetry?.RuntimeDegraded ?? false
            };
        }
    }

    private StartupMetrics GetMetrics()
    {
        return new StartupMetrics
        {
            BackendDetectionMs = MsBetween(_detectBackendStart, _detectBackendEnd),
            ModelDownloadMs = MsBetween(_downloadStart, _downloadEnd),
            ModelLoadMs = MsBetween(_loadStart, _loadEnd),
            TotalStartupMs = _readyTime.HasValue
                ? (int)(_readyTime.Value - _startTime).TotalMilliseconds
                : (int)(DateTime.UtcNow - _startTime).TotalMilliseconds,
            ReadyAt = _readyTime,
            ErrorAt = _errorTime,
            DegradationReason = _degradationReason,
            DegradationFrom = _degradationFrom.ToString()
        };
    }

    private static int? MsBetween(DateTime? start, DateTime? end)
    {
        if (start == null || end == null) return null;
        return (int)(end.Value - start.Value).TotalMilliseconds;
    }

    // ══════════════════════════════════════════
    //  INITIALIZATION
    // ══════════════════════════════════════════

    public void Configure(
        GpuDetector gpuDetector,
        ModelManager modelManager,
        LocalInferenceEngine localEngine,
        InferenceRouter inferenceRouter,
        Func<Task>? downloadFunc = null,
        BackendProbe? probe = null,
        VerdictStore? verdictStore = null)
    {
        _gpuDetector = gpuDetector;
        _modelManager = modelManager;
        _localEngine = localEngine;
        _inferenceRouter = inferenceRouter;
        _downloadFunc = downloadFunc;
        _probe = probe;
        _verdictStore = verdictStore;

        // Subscribe to cleanup events for survivability monitoring
        _localEngine.OnCleanupResult += ReportCleanupResult;
    }

    public bool IsSafeModeActive
    {
        get
        {
            lock (_lock)
            {
                return _state == InferenceState.SafeMode || 
                       Array.Exists(Environment.GetCommandLineArgs(), arg => arg == "--safe-mode") ||
                       Environment.GetEnvironmentVariable("ENGRAM_SAFE_MODE") == "true";
            }
        }
    }

    /// <summary>
    /// Start non-blocking background initialization.
    /// Returns immediately. Poll /health for state.
    /// </summary>
    public void StartInitialization()
    {
        _log.Lifecycle("Starting background initialization");

        if (Array.Exists(Environment.GetCommandLineArgs(), arg => arg == "--safe-mode") ||
            Environment.GetEnvironmentVariable("ENGRAM_SAFE_MODE") == "true")
        {
            _log.Lifecycle("Safe Mode state detected. Initializing in read-only Safe Mode.");
            TransitionTo(InferenceState.SafeMode, "Safe-Mode state detected");
            
            // Set degradation tracker to SafeModeActive
            DegradationTracker.Instance.SetDegradation("SafeModeActive", true, "Ontology or WAL consistency check failed");
            return;
        }

        _initTask = Task.Run(() => RunInitializationSequence(_cts.Token));
    }

    private async Task RunInitializationSequence(CancellationToken ct)
    {
        try
        {
            // ── Phase 1: Detect backend + probe stability ──
            _detectBackendStart = DateTime.UtcNow;
            TransitionTo(InferenceState.DetectingBackend, "startup sequence");
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
            _detectBackendEnd = DateTime.UtcNow;
            _log.Gpu($"Backend detected: {gpuInfo.Description} [{_detectBackendEnd.Value - _detectBackendStart.Value}ms]");

            // ── Phase 1b: Probe backend stability ──
            if (_probe != null && _verdictStore != null)
            {
                // Check cached verdict first
                var cachedVerdict = _verdictStore.GetVerdict(_backend);
                if (cachedVerdict != null)
                {
                    if (cachedVerdict.Status == VerdictStatus.Success)
                    {
                        _log.Gpu($"Using cached verdict: {_backend} = SUCCESS (from {cachedVerdict.Timestamp:u})");
                        _metadata["probeSource"] = "cache";
                        _metadata["probeCachedAt"] = cachedVerdict.Timestamp.ToString("u");
                    }
                    else
                    {
                        // Cached failure — skip this backend, try CPU
                        _log.GpuWarn($"Cached verdict: {_backend} = {cachedVerdict.Status} at '{cachedVerdict.FailureStage}' — {cachedVerdict.Reason}");
                        _log.GpuWarn("Skipping to CPU fallback (cached failure verdict)");

                        if (gpuInfo.Backend != GpuBackend.Cpu)
                        {
                            _backend = "Cpu";
                            gpuInfo = new GpuInfo
                            {
                                Backend = GpuBackend.Cpu,
                                DeviceName = "CPU",
                                VramMb = 0,
                                LayerCount = 0,
                                Description = "CPU+SIMD (cached GPU failure)"
                            };
                            _metadata["gpuDevice"] = "CPU (fallback)";
                            _metadata["gpuLayers"] = "0";
                            _metadata["probeSource"] = "cache_skip";
                            _metadata["probeFailureReason"] = cachedVerdict.Reason ?? "unknown";
                        }
                    }
                }
                else
                {
                    // No cached verdict — run live probe
                    _log.Gpu($"No cached verdict for {_backend}, running stability probe...");
                    var modelPath = _modelManager != null ? ModelManager.GetModelPath(ModelManager.Phi4Mini) : null;
                    var probeTimeout = TimeSpan.FromSeconds(30);
                    var verdict = await _probe.ProbeAsync(_backend, gpuInfo, modelPath, probeTimeout);

                    _verdictStore.Record(verdict);
                    _metadata["probeSource"] = "live";
                    _metadata["probeDurationMs"] = verdict.ProbeDurationMs.ToString();

                    if (verdict.Status != VerdictStatus.Success)
                    {
                        _log.GpuWarn($"Probe failed: {verdict.FailureStage} — {verdict.Reason}");

                        // Fallback to CPU
                        if (gpuInfo.Backend != GpuBackend.Cpu)
                        {
                            _log.Gpu("Falling back to CPU...");
                            _backend = "Cpu";
                            gpuInfo = new GpuInfo
                            {
                                Backend = GpuBackend.Cpu,
                                DeviceName = "CPU",
                                VramMb = 0,
                                LayerCount = 0,
                                Description = $"CPU+SIMD (probe failed: {verdict.FailureStage})"
                            };
                            _metadata["gpuDevice"] = "CPU (fallback)";
                            _metadata["gpuLayers"] = "0";
                            _metadata["probeFailureReason"] = verdict.Reason ?? "unknown";

                            // Record CPU verdict as success (it's our fallback)
                            _verdictStore.Record(new BackendVerdict
                            {
                                Backend = "Cpu",
                                Status = VerdictStatus.Success,
                                GpuDevice = "CPU",
                                ProbeDurationMs = 0,
                                MachineHash = verdict.MachineHash,
                                AppVersion = verdict.AppVersion
                            });
                        }
                    }
                    else
                    {
                        _log.Gpu($"Probe PASSED: {_backend} is stable [{verdict.ProbeDurationMs}ms]");
                    }
                }
            }
            else
            {
                _log.Gpu("No probe/verdict store — proceeding without stability check");
                _metadata["probeSource"] = "none";
            }

            TransitionTo(InferenceState.BackendReady, $"backend verified: {_backend}");

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
                _downloadStart = DateTime.UtcNow;
                TransitionTo(InferenceState.DownloadingModel, "model not found on disk");
                _log.Model("Starting model download...");

                if (_downloadFunc != null)
                {
                    await _downloadFunc();
                }
                else
                {
                    var progress = new Progress<ModelDownloadProgress>(p =>
                    {
                        lock (_lock) _progress = p.Progress * 100;
                    });
                    await _modelManager.DownloadModelAsync(config, progress, ct);
                }

                _downloadEnd = DateTime.UtcNow;

                if (!_modelManager.IsModelReady(config))
                {
                    SetError("Model download failed or incomplete");
                    return;
                }

                _log.Model($"Model download complete [{(_downloadEnd.Value - _downloadStart.Value).TotalSeconds:F1}s]");
            }
            else
            {
                _log.Model($"Model found: {ModelManager.GetModelPath(config)}");
            }

            // ── Phase 3: Load model ──
            _loadStart = DateTime.UtcNow;
            TransitionTo(InferenceState.LoadingModel, "model file ready");
            _log.Model("Loading model into memory...");
            lock (_lock) _progress = 0;

            var loadSuccess = await Task.Run(() => _localEngine.LoadModel(config), ct);

            _loadEnd = DateTime.UtcNow;

            if (!loadSuccess)
            {
                SetError("Model load failed. Check GPU drivers or available memory.");
                return;
            }

            _modelLoaded = true;
            lock (_lock) _progress = 100;
            _log.Model($"Model loaded: {config.Name} [{(_loadEnd.Value - _loadStart.Value).TotalSeconds:F1}s]");
            _log.Model($"Backend: {_backend}, Device: {_metadata.GetValueOrDefault("gpuDevice", "unknown")}");

            // ── Phase 4: Ready ──
            _readyTime = DateTime.UtcNow;
            TransitionTo(InferenceState.Ready, "initialization complete");
            _log.Lifecycle($"=== ENGRAM INFERENCE READY === [total: {(_readyTime.Value - _startTime).TotalSeconds:F1}s]");
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

    // ══════════════════════════════════════════
    //  MANUAL OPERATIONS
    // ══════════════════════════════════════════

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

        _loadStart = DateTime.UtcNow;
        TransitionTo(InferenceState.LoadingModel, "manual load triggered");
        _log.Model("Manual model load triggered...");

        var loadSuccess = await Task.Run(() => _localEngine.LoadModel(config));

        _loadEnd = DateTime.UtcNow;

        if (loadSuccess)
        {
            _modelLoaded = true;
            lock (_lock) _progress = 100;
            _readyTime = DateTime.UtcNow;
            TransitionTo(InferenceState.Ready, "manual load complete");
            _log.Model($"Model loaded successfully (manual) [{(_loadEnd.Value - _loadStart.Value).TotalSeconds:F1}s]");
            return true;
        }
        else
        {
            SetError("Model load failed. Check GPU drivers or available memory.");
            return false;
        }
    }

    /// <summary>
    /// Unload model from memory. Returns to BackendReady.
    /// </summary>
    public void UnloadModel()
    {
        _localEngine?.UnloadModel();
        lock (_lock)
        {
            _modelLoaded = false;
            _progress = 0;
        }
        _log.Model("Model unloaded");
        TransitionTo(InferenceState.BackendReady, "user requested unload");
    }

    /// <summary>
    /// Retry after error. Resets state and re-runs initialization.
    /// </summary>
    public void Retry()
    {
        lock (_lock)
        {
            if (_state != InferenceState.Error && _state != InferenceState.Degraded)
            {
                _log.LifecycleWarn($"Retry requested but state is {_state}, ignoring");
                return;
            }

            if (_state == InferenceState.Degraded && !_degradationRetryAllowed)
            {
                _log.LifecycleWarn("Retry not allowed for this degradation");
                return;
            }

            _retryCount++;
            _error = null;
            _degradationReason = null;
            // Reset metrics for fresh attempt
            _detectBackendStart = _detectBackendEnd = null;
            _downloadStart = _downloadEnd = null;
            _loadStart = _loadEnd = null;
            _readyTime = _errorTime = null;
        }

        _log.Lifecycle($"Retry #{_retryCount} starting...");
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        StartInitialization();
    }

    // ══════════════════════════════════════════
    //  CLEANUP HEALTH MONITORING
    // ══════════════════════════════════════════

    /// <summary>
    /// Check cleanup health after inference. Transitions to Degraded if cleanup fails repeatedly.
    /// Called by the inference engine after each request.
    /// </summary>
    public void ReportCleanupResult(CleanupOutcome outcome, string sessionId)
    {
        if (outcome == CleanupOutcome.Success || outcome == CleanupOutcome.Skipped)
        {
            Interlocked.Exchange(ref _consecutiveCleanupFailures, 0);
            return;
        }

        // Cleanup failed or verification failed
        var failures = Interlocked.Increment(ref _consecutiveCleanupFailures);
        _log.LifecycleWarn($"Cleanup failure #{failures} for session {sessionId}: {outcome}");

        if (failures >= MaxConsecutiveCleanupFailures)
        {
            lock (_lock)
            {
                if (_state == InferenceState.Ready)
                {
                    _degradationReason = $"Cleanup pipeline failed {failures} consecutive times (last: {outcome})";
                    _degradationFrom = _state;
                    _degradationRetryAllowed = true;
                    TransitionTo(InferenceState.Degraded, _degradationReason);
                    _log.LifecycleError($"Entered DEGRADED state: {_degradationReason}");
                }
            }
        }
    }

    /// <summary>
    /// Reset consecutive cleanup failure counter.
    /// Called when cleanup succeeds after failures.
    /// </summary>
    public void ResetCleanupFailureCount()
    {
        Interlocked.Exchange(ref _consecutiveCleanupFailures, 0);
    }

    private bool TransitionTo(InferenceState newState, string reason)
    {
        lock (_lock)
        {
            var old = _state;

            // Guard: validate legal transition
            if (!LegalTransitions.TryGetValue(old, out var allowed) || !allowed.Contains(newState))
            {
                _log.LifecycleError($"ILLEGAL TRANSITION: {old} → {newState} (reason: {reason}). Blocked.");
                _stateHistory.Add($"{DateTime.UtcNow:HH:mm:ss} ILLEGAL: {old} → {newState} BLOCKED ({reason})");
                return false;
            }

            _state = newState;
            var entry = $"{DateTime.UtcNow:HH:mm:ss} {old} → {newState}";
            if (!string.IsNullOrEmpty(reason))
                entry += $" ({reason})";
            _stateHistory.Add(entry);
            _log.Lifecycle($"State: {old} → {newState} ({reason})");
        }

        StateChanged?.Invoke(newState);
        return true;
    }

    private void SetError(string error)
    {
        lock (_lock)
        {
            _errorTime = DateTime.UtcNow;
            _error = error;
            _state = InferenceState.Error;
            _stateHistory.Add($"{DateTime.UtcNow:HH:mm:ss} → Error: {error}");
        }
        _log.LifecycleError(error);
        StateChanged?.Invoke(InferenceState.Error);
    }

    // ══════════════════════════════════════════
    //  DOWNLOAD PROGRESS (external handler)
    // ══════════════════════════════════════════

    public void ReportDownloadProgress(double progress)
    {
        lock (_lock) _progress = progress;
    }

    public void ReportDownloadComplete()
    {
        _downloadEnd = DateTime.UtcNow;
        _log.Model("Download reported complete by external handler");
    }

    public void ReportDownloadError(string error)
    {
        SetError($"Download failed: {error}");
    }

    public void Dispose()
    {
        // Unsubscribe from cleanup events
        if (_localEngine != null)
        {
            _localEngine.OnCleanupResult -= ReportCleanupResult;
        }

        TransitionTo(InferenceState.Offline, "disposing");
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

    /// <summary>System operating in read-only quarantine due to semantic uncertainty.</summary>
    SafeMode,

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
    public StartupMetrics Metrics { get; init; } = new();
    public InferenceEngineTelemetry? Inference { get; init; }

    // ── Survivability metrics ──
    public bool RuntimeOperational { get; init; }
    public double RecentSuccessRate { get; init; } = 1.0;
    public int ConsecutiveFailures { get; init; }
    public long GeneratedTokensSinceReset { get; init; }
    public DateTime? LastSuccessfulInferenceAt { get; init; }
    public bool RuntimeDegraded { get; init; }
}

/// <summary>
/// Timing metrics for startup phases. Enables quantitative visibility.
/// </summary>
public class StartupMetrics
{
    /// <summary>Time to detect GPU backend (ms). Null if not yet completed.</summary>
    public int? BackendDetectionMs { get; init; }

    /// <summary>Time to download model (ms). Null if model was already cached.</summary>
    public int? ModelDownloadMs { get; init; }

    /// <summary>Time to load model into memory (ms). Null if not yet attempted.</summary>
    public int? ModelLoadMs { get; init; }

    /// <summary>Total time from process start to Ready state (ms).</summary>
    public int TotalStartupMs { get; init; }

    /// <summary>UTC timestamp when Ready state was reached. Null if not ready.</summary>
    public DateTime? ReadyAt { get; init; }

    /// <summary>UTC timestamp when Error state was reached. Null if no error.</summary>
    public DateTime? ErrorAt { get; init; }

    /// <summary>Reason for entering Degraded state. Null if not degraded.</summary>
    public string? DegradationReason { get; init; }

    /// <summary>Which state degradation came from. Empty if not degraded.</summary>
    public string DegradationFrom { get; init; } = "";
}
