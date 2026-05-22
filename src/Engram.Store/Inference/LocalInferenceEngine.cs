using LLama;
using LLama.Common;
using LLama.Native;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Inference;

/// <summary>
/// Local SLM inference engine using LLamaSharp.
/// Loads Phi-4-mini GGUF and generates chat completions.
/// Runs entirely on-device — no network calls.
/// 
/// Each inference request gets an InferenceSession with:
/// - Token heartbeat tracking
/// - No-token watchdog (30s default)
/// - Hard timeout (5min default)
/// - Graceful cancellation with escalation
/// - Memory delta tracking
/// </summary>
public class LocalInferenceEngine : IDisposable
{
    private readonly ILogger<LocalInferenceEngine>? _logger;
    private readonly ModelManager _modelManager;
    private readonly GpuDetector _gpuDetector;
    private readonly InferenceLogger _log = InferenceLogger.Instance;
    private LLamaWeights? _model;
    private LLamaContext? _context;
    private ModelConfig? _loadedConfig;
    private GpuInfo? _gpuInfo;
    private bool _disposed;
    private bool _isLoading;
    private readonly object _loadLock = new();

    // ── Inference tracking ──
    private InferenceSession? _activeSession;
    private readonly object _sessionLock = new();
    private long _totalInferences;
    private long _totalTokensGenerated;
    private long _totalViolations;
    private DateTime? _lastInferenceAt;

    // ── KV cache management (Production lifecycle) ──
    /// <summary>
    /// When true, creates a fresh LLamaContext for each request
    /// and disposes it after. Model weights stay loaded.
    /// Test: is context reuse fundamentally unsafe?
    /// </summary>
    public bool FreshContextPerRequest { get; set; }

    // ── Cleanup telemetry ──
    private long _cleanupCount;
    private long _cleanupFailures;
    private long _cleanupVerificationFailures;
    private readonly List<double> _cleanupDurations = new();
    private readonly object _cleanupLock = new();

    // ── Events ──
    public event Action<CleanupOutcome, string>? OnCleanupResult;

    public virtual bool IsReady => _model != null && _context != null;
    public bool IsLoading => _isLoading;
    public GpuInfo? GpuInfo => _gpuInfo;
    public ModelConfig? LoadedModel => _loadedConfig;
    public InferenceSession? ActiveSession { get { lock (_sessionLock) return _activeSession; } }

    public LocalInferenceEngine(
        ModelManager modelManager,
        GpuDetector gpuDetector,
        ILogger<LocalInferenceEngine>? logger = null)
    {
        _modelManager = modelManager;
        _gpuDetector = gpuDetector;
        _logger = logger;
    }

    /// <summary>
    /// Load a model into memory. Call once at startup.
    /// </summary>
    public bool LoadModel(ModelConfig? config = null)
    {
        config ??= ModelManager.Phi4Mini;

        lock (_loadLock)
        {
            if (IsReady && _loadedConfig?.Name == config.Name)
                return true;

            if (_isLoading)
                return false;

            _isLoading = true;
        }

        try
        {
            var modelPath = ModelManager.GetModelPath(config);
            if (!_modelManager.IsModelReady(config))
            {
                _logger?.LogWarning("Model not ready: {Path}. Download it first.", modelPath);
                _log.ModelWarn($"Model not ready: {modelPath}");
                return false;
            }

            _logger?.LogInformation("Loading model: {Name} from {Path}", config.Name, modelPath);
            _log.Model($"Loading model: {config.Name} from {modelPath}");
            _gpuInfo = _gpuDetector.Detect();
            _logger?.LogInformation("Using: {Device} (layers={Layers})", _gpuInfo.Description, _gpuInfo.LayerCount);
            _log.Model($"Using: {_gpuInfo.Description} (layers={_gpuInfo.LayerCount})");

            var parameters = new ModelParams(modelPath)
            {
                ContextSize = (uint)config.ContextSize,
                GpuLayerCount = _gpuInfo.LayerCount,
                Threads = Environment.ProcessorCount,
                BatchSize = 512
            };

            _model = LLamaWeights.LoadFromFile(parameters);
            _context = _model.CreateContext(parameters);
            _loadedConfig = config;

            _logger?.LogInformation("Model loaded: {Name} ({Params})", config.Name, config.Description);
            _log.Model($"Model loaded: {config.Name} ({config.Description})");
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load model: {Name}", config.Name);
            _log.ModelError($"Failed to load model: {config.Name}", ex);
            _model?.Dispose();
            _model = null;
            _context?.Dispose();
            _context = null;
            return false;
        }
        finally
        {
            _isLoading = false;
        }
    }

    // ══════════════════════════════════════
    //  KV CACHE MANAGEMENT (Production Lifecycle)
    // ══════════════════════════════════════

    /// <summary>
    /// Execute the post-inference cleanup pipeline with lifecycle stages.
    /// 
    /// Lifecycle stages:
    ///   InferenceComplete → PostInferenceCleanupStarted → KvCacheCleared → ContextResetValidated → RuntimeReady
    /// 
    /// Cleanup is survivability-critical infrastructure.
    /// </summary>
    private CleanupOutcome ExecutePostInferenceCleanup(
        string sessionId,
        int kvTokensBefore, int kvTokensAfter,
        int kvCellsBefore, int kvCellsAfter,
        out double durationMs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        durationMs = 0;

        try
        {
            // Stage 1: PostInferenceCleanupStarted
            _log.Inference($"Session {sessionId}: [LIFECYCLE] PostInferenceCleanupStarted");

            // Stage 2: KvCacheCleared
            if (_context == null)
            {
                _log.InferenceWarn($"Session {sessionId}: [LIFECYCLE] KvCacheClear skipped — context is null");
                return CleanupOutcome.Skipped;
            }

            try
            {
                _context.NativeHandle.KvCacheClear();
                _log.Inference($"Session {sessionId}: [LIFECYCLE] KvCacheCleared");
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _cleanupFailures);
                _log.InferenceError($"Session {sessionId}: [LIFECYCLE] KvCacheClear FAILED", ex);
                sw.Stop();
                durationMs = sw.Elapsed.TotalMilliseconds;
                RecordCleanupDuration(durationMs);
                return CleanupOutcome.Failed;
            }

            // Stage 3: ContextResetValidated (verification)
            var kvTokensAfterClear = GetKvTokenCount();
            var kvCellsAfterClear = GetKvUsedCells();

            // Verification: KV should be reset to 0 or -1 (unavailable)
            if (kvTokensAfterClear > 0)
            {
                Interlocked.Increment(ref _cleanupVerificationFailures);
                _log.InferenceWarn($"Session {sessionId}: [LIFECYCLE] ContextResetValidated FAILED — " +
                    $"KV tokens not reset: expected ≤0, got {kvTokensAfterClear}");
                sw.Stop();
                durationMs = sw.Elapsed.TotalMilliseconds;
                RecordCleanupDuration(durationMs);
                OnCleanupResult?.Invoke(CleanupOutcome.VerificationFailed, sessionId);
                return CleanupOutcome.VerificationFailed;
            }

            _log.Inference($"Session {sessionId}: [LIFECYCLE] ContextResetValidated — " +
                $"KV tokens: {kvTokensAfter}→{kvTokensAfterClear}, cells: {kvCellsAfter}→{kvCellsAfterClear}");

            // Stage 4: RuntimeReady
            Interlocked.Increment(ref _cleanupCount);
            sw.Stop();
            durationMs = sw.Elapsed.TotalMilliseconds;
            RecordCleanupDuration(durationMs);

            _log.Inference($"Session {sessionId}: [LIFECYCLE] RuntimeReady — cleanup {durationMs:F1}ms");
            OnCleanupResult?.Invoke(CleanupOutcome.Success, sessionId);
            return CleanupOutcome.Success;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _cleanupFailures);
            _log.InferenceError($"Session {sessionId}: [LIFECYCLE] Cleanup pipeline failed", ex);
            sw.Stop();
            durationMs = sw.Elapsed.TotalMilliseconds;
            RecordCleanupDuration(durationMs);
            OnCleanupResult?.Invoke(CleanupOutcome.Failed, sessionId);
            return CleanupOutcome.Failed;
        }
    }

    private void RecordCleanupDuration(double durationMs)
    {
        lock (_cleanupLock)
        {
            _cleanupDurations.Add(durationMs);
            // Keep last 1000 durations for drift detection
            if (_cleanupDurations.Count > 1000)
                _cleanupDurations.RemoveAt(0);
        }
    }

    /// <summary>
    /// Get cleanup telemetry for survivability analysis.
    /// </summary>
    public CleanupTelemetry GetCleanupTelemetry()
    {
        lock (_cleanupLock)
        {
            var count = Interlocked.Read(ref _cleanupCount);
            var failures = Interlocked.Read(ref _cleanupFailures);
            var verificationFailures = Interlocked.Read(ref _cleanupVerificationFailures);
            var total = count + failures + verificationFailures;

            return new CleanupTelemetry
            {
                TotalCleanups = total,
                SuccessfulCleanups = count,
                FailedCleanups = failures,
                VerificationFailures = verificationFailures,
                SuccessRate = total > 0 ? (double)count / total : 1.0,
                AverageDurationMs = _cleanupDurations.Count > 0 ? _cleanupDurations.Average() : 0,
                MaxDurationMs = _cleanupDurations.Count > 0 ? _cleanupDurations.Max() : 0,
                MinDurationMs = _cleanupDurations.Count > 0 ? _cleanupDurations.Min() : 0,
                RecentDurations = _cleanupDurations.TakeLast(10).ToList()
            };
        }
    }

    /// <summary>
    /// Get the number of tokens currently in the KV cache.
    /// Returns -1 if unavailable.
    /// </summary>
    public int GetKvTokenCount()
    {
        if (_context == null) return -1;
        try
        {
            return _context.NativeHandle.KvCacheCountTokens();
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// Get the number of used KV cells.
    /// Returns -1 if unavailable.
    /// </summary>
    public int GetKvUsedCells()
    {
        if (_context == null) return -1;
        try
        {
            return _context.NativeHandle.KvCacheCountCells();
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// Generate a chat completion from messages.
    /// Creates an InferenceSession with heartbeat tracking and watchdog.
    /// Respects ClearKvCacheAfterInference and FreshContextPerRequest modes.
    /// </summary>
    public virtual async Task<InferenceResult> ChatCompletionAsync(
        ChatMessage[] messages,
        int maxTokens = 1024,
        CancellationToken cancellationToken = default)
    {
        if (!IsReady)
            return InferenceResult.Failed("Model not loaded. Download and load a model first.");

        // Create inference session with watchdog
        using var session = new InferenceSession();
        session.NoTokenTimeout = TimeSpan.FromSeconds(30);
        session.HardTimeout = TimeSpan.FromMinutes(5);

        // Link external cancellation with session's internal cancellation
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, session.Token);

        // Track active session
        lock (_sessionLock)
        {
            _activeSession = session;
        }

        // Wire up violation handler
        session.OnViolation += (sess, violation) =>
        {
            Interlocked.Increment(ref _totalViolations);
            _log.InferenceError($"Violation in session {sess.SessionId}: {violation.Message}");
        };

        // Memory telemetry: snapshot before
        var memBefore = GetMemorySnapshot();

        // KV telemetry: snapshot before
        var kvTokensBefore = GetKvTokenCount();
        var kvCellsBefore = GetKvUsedCells();

        // Determine context source for this request
        // Experiment 2: Fresh context per request (model weights stay loaded)
        LLamaContext? requestContext = null;
        LLamaContext? contextToDispose = null;
        bool usingFreshContext = FreshContextPerRequest && _model != null;

        if (usingFreshContext)
        {
            try
            {
                var modelPath = ModelManager.GetModelPath(_loadedConfig!);
                var parameters = new ModelParams(modelPath)
                {
                    ContextSize = (uint)_loadedConfig.ContextSize,
                    GpuLayerCount = _gpuInfo?.LayerCount ?? 0,
                    Threads = Environment.ProcessorCount,
                    BatchSize = 512
                };
                requestContext = _model.CreateContext(parameters);
                contextToDispose = requestContext;
                _log.Inference($"Session {session.SessionId}: using FRESH context (experiment mode)");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to create fresh context, falling back to shared");
                _log.InferenceError("Fresh context creation failed, using shared context", ex);
                requestContext = _context;
            }
        }
        else
        {
            requestContext = _context;
        }

        session.Start();

        try
        {
            var prompt = FormatChatPrompt(messages);
            var executor = new InteractiveExecutor(requestContext!);

            var inferenceParams = new InferenceParams
            {
                MaxTokens = maxTokens,
                AntiPrompts = new[] { "<|end|>", "<|user|>", "<|system|>" }
            };

            _log.Inference($"Session {session.SessionId}: generating (maxTokens={maxTokens})");

            var responseBuilder = new System.Text.StringBuilder();
            var tokenCount = 0L;

            await foreach (var token in executor.InferAsync(prompt, inferenceParams, linkedCts.Token))
            {
                responseBuilder.Append(token);
                tokenCount++;

                // Heartbeat: record each token
                session.RecordToken();
            }

            session.Complete();

            var response = responseBuilder.ToString().Trim();

            // Clean up template artifacts
            response = response.Replace("<|end|>", "").Trim();
            response = response.Replace("<|user|>", "").Trim();
            response = response.Replace("<|system|>", "").Trim();

            // Memory telemetry: snapshot after
            var memAfter = GetMemorySnapshot();
            var memDelta = memAfter.WorkingSetMb - memBefore.WorkingSetMb;

            // KV telemetry: snapshot after (before cleanup)
            var kvTokensAfter = usingFreshContext ? -1 : GetKvTokenCount();
            var kvCellsAfter = usingFreshContext ? -1 : GetKvUsedCells();

            // ══════════════════════════════════════════════════
            //  PRODUCTION LIFECYCLE: Mandatory post-inference cleanup
            // ══════════════════════════════════════════════════
            var cleanupResult = CleanupOutcome.Skipped;
            var cleanupDurationMs = 0.0;

            if (!usingFreshContext)
            {
                cleanupResult = ExecutePostInferenceCleanup(
                    session.SessionId, kvTokensBefore, kvTokensAfter, kvCellsBefore, kvCellsAfter,
                    out cleanupDurationMs);
            }

            // Update global stats
            Interlocked.Increment(ref _totalInferences);
            Interlocked.Add(ref _totalTokensGenerated, tokenCount);
            _lastInferenceAt = DateTime.UtcNow;

            // KV after cleanup (verification)
            var kvTokensAfterCleanup = usingFreshContext ? -1 : GetKvTokenCount();
            var kvCellsAfterCleanup = usingFreshContext ? -1 : GetKvUsedCells();

            _log.Inference($"Session {session.SessionId}: complete — {tokenCount} tokens " +
                $"in {session.Elapsed.TotalSeconds:F1}s " +
                $"({tokenCount / Math.Max(0.1, session.Elapsed.TotalSeconds):F1} tok/s) " +
                $"[memΔ: {memDelta:+0;-0}MB] [kv: {kvTokensBefore}→{kvTokensAfter}→{kvTokensAfterCleanup}] " +
                $"[cleanup: {cleanupResult} {cleanupDurationMs:F1}ms]");

            return new InferenceResult
            {
                Success = true,
                Content = response,
                InputTokens = EstimateTokenCount(prompt),
                OutputTokens = (int)tokenCount,
                Model = _loadedConfig?.Name ?? "unknown",
                Provider = "local",
                SessionTelemetry = session.GetTelemetry(),
                MemoryDeltaMb = memDelta,
                KvTokensBefore = kvTokensBefore,
                KvTokensAfter = kvTokensAfter,
                KvCellsBefore = kvCellsBefore,
                KvCellsAfter = kvCellsAfter,
                KvTokensAfterCleanup = kvTokensAfterCleanup,
                KvCellsAfterCleanup = kvCellsAfterCleanup,
                UsedFreshContext = usingFreshContext,
                CleanupResult = cleanupResult,
                CleanupDurationMs = cleanupDurationMs
            };
        }
        catch (OperationCanceledException) when (session.IsCancelled && session.Violation != null)
        {
            // Watchdog-triggered cancellation
            _log.InferenceError($"Session {session.SessionId}: watchdog cancelled — {session.Violation.Message}");
            return InferenceResult.Failed($"Inference cancelled: {session.Violation.Message}");
        }
        catch (OperationCanceledException)
        {
            // User or external cancellation
            _log.Inference($"Session {session.SessionId}: cancelled by request");
            return InferenceResult.Failed("Inference cancelled.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Inference failed");
            _log.InferenceError($"Session {session.SessionId}: failed", ex);
            return InferenceResult.Failed($"Inference error: {ex.Message}");
        }
        finally
        {
            // Dispose fresh context if we created one
            contextToDispose?.Dispose();

            lock (_sessionLock)
            {
                _activeSession = null;
            }
        }
    }

    /// <summary>
    /// Generate a streaming chat completion. Yields tokens as they're generated.
    /// Note: streaming does NOT use the watchdog (caller must handle timeout).
    /// </summary>
    public async IAsyncEnumerable<string> ChatCompletionStreamAsync(
        ChatMessage[] messages,
        int maxTokens = 1024,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!IsReady)
            yield break;

        using var session = new InferenceSession();
        lock (_sessionLock) _activeSession = session;
        session.Start();

        try
        {
            var prompt = FormatChatPrompt(messages);
            var executor = new InteractiveExecutor(_context!);

            var inferenceParams = new InferenceParams
            {
                MaxTokens = maxTokens,
                AntiPrompts = new[] { "<|end|>", "<|user|>", "<|system|>" }
            };

            await foreach (var token in executor.InferAsync(prompt, inferenceParams, cancellationToken))
            {
                session.RecordToken();
                yield return token;
            }

            session.Complete();
        }
        finally
        {
            lock (_sessionLock) _activeSession = null;
        }
    }

    /// <summary>
    /// Format messages into Phi-4-mini-instruct chat template.
    /// Template: <|system|>system msg<|end|><|user|>user msg<|end|><|assistant|>
    /// </summary>
    private static string FormatChatPrompt(ChatMessage[] messages)
    {
        var sb = new System.Text.StringBuilder();

        // Add system message if none provided
        var hasSystem = messages.Any(m => m.Role.Equals("system", StringComparison.OrdinalIgnoreCase));
        if (!hasSystem)
        {
            sb.Append("<|system|>You are Engram, a personal semantic memory layer assistant.<|end|>");
        }

        foreach (var msg in messages)
        {
            var role = msg.Role.ToLowerInvariant() switch
            {
                "user" => "user",
                "assistant" => "assistant",
                "system" => "system",
                _ => "user"
            };
            sb.Append($"<|{role}|>{msg.Content}<|end|>");
        }

        sb.Append("<|assistant|>");
        return sb.ToString();
    }

    private static int EstimateTokenCount(string text)
    {
        return text.Length / 4;
    }

    /// <summary>
    /// Get memory snapshot for telemetry.
    /// </summary>
    private static MemorySnapshot GetMemorySnapshot()
    {
        try
        {
            var process = System.Diagnostics.Process.GetCurrentProcess();
            return new MemorySnapshot
            {
                WorkingSetMb = process.WorkingSet64 / (1024.0 * 1024.0),
                GcHeapMb = GC.GetTotalMemory(false) / (1024.0 * 1024.0)
            };
        }
        catch
        {
            return new MemorySnapshot();
        }
    }

    /// <summary>
    /// Get inference telemetry for health reporting.
    /// </summary>
    public InferenceEngineTelemetry GetTelemetry()
    {
        lock (_sessionLock)
        {
            var cleanup = GetCleanupTelemetry();
            return new InferenceEngineTelemetry
            {
                TotalInferences = Interlocked.Read(ref _totalInferences),
                TotalTokensGenerated = Interlocked.Read(ref _totalTokensGenerated),
                TotalViolations = Interlocked.Read(ref _totalViolations),
                LastInferenceAt = _lastInferenceAt,
                ActiveSession = _activeSession?.GetTelemetry(),
                KvTokensInCache = GetKvTokenCount(),
                KvUsedCells = GetKvUsedCells(),
                FreshContextPerRequest = FreshContextPerRequest,
                Cleanup = cleanup,
                // Survivability metrics
                RuntimeOperational = cleanup.SuccessRate > 0.5 && Interlocked.Read(ref _cleanupVerificationFailures) == 0,
                RecentSuccessRate = cleanup.SuccessRate,
                ConsecutiveFailures = 0, // TODO: track consecutive failures
                GeneratedTokensSinceReset = Interlocked.Read(ref _totalTokensGenerated),
                RuntimeDegraded = cleanup.FailedCleanups > 0 || cleanup.VerificationFailures > 0
            };
        }
    }

    /// <summary>
    /// Unload the model from memory.
    /// </summary>
    public void UnloadModel()
    {
        lock (_loadLock)
        {
            // Cancel any active session first
            lock (_sessionLock)
            {
                _activeSession?.Cancel("model_unloading");
                _activeSession = null;
            }

            _context?.Dispose();
            _context = null;
            _model?.Dispose();
            _model = null;
            _loadedConfig = null;
            _logger?.LogInformation("Model unloaded");
            _log.Model("Model unloaded");
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            UnloadModel();
            _disposed = true;
        }
    }
}

public class ChatMessage
{
    public string Role { get; init; } = "user";
    public string Content { get; init; } = string.Empty;
}

public class InferenceResult
{
    public bool Success { get; init; }
    public string Content { get; init; } = string.Empty;
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public string Model { get; init; } = string.Empty;
    public string Provider { get; init; } = "local";
    public string? ErrorMessage { get; init; }
    public InferenceSessionTelemetry? SessionTelemetry { get; init; }
    public double MemoryDeltaMb { get; init; }

    // ── KV cache telemetry ──
    public int KvTokensBefore { get; init; } = -1;
    public int KvTokensAfter { get; init; } = -1;
    public int KvCellsBefore { get; init; } = -1;
    public int KvCellsAfter { get; init; } = -1;
    public int KvTokensAfterCleanup { get; init; } = -1;
    public int KvCellsAfterCleanup { get; init; } = -1;
    public bool UsedFreshContext { get; init; }

    // ── Cleanup telemetry ──
    public CleanupOutcome CleanupResult { get; init; }
    public double CleanupDurationMs { get; init; }

    public static InferenceResult Failed(string error) => new()
    {
        Success = false,
        ErrorMessage = error,
        Provider = "local"
    };
}

/// <summary>
/// Outcome of the post-inference cleanup pipeline.
/// </summary>
public enum CleanupOutcome
{
    /// <summary>Cleanup succeeded, KV verified reset.</summary>
    Success,
    /// <summary>Cleanup failed (exception during KvCacheClear).</summary>
    Failed,
    /// <summary>KvCacheClear succeeded but verification failed (tokens not reset).</summary>
    VerificationFailed,
    /// <summary>Cleanup skipped (fresh context mode or null context).</summary>
    Skipped
}

/// <summary>
/// Telemetry for the cleanup pipeline — survivability-critical infrastructure.
/// </summary>
public class CleanupTelemetry
{
    public long TotalCleanups { get; init; }
    public long SuccessfulCleanups { get; init; }
    public long FailedCleanups { get; init; }
    public long VerificationFailures { get; init; }
    public double SuccessRate { get; init; }
    public double AverageDurationMs { get; init; }
    public double MaxDurationMs { get; init; }
    public double MinDurationMs { get; init; }
    public List<double> RecentDurations { get; init; } = new();
}

/// <summary>
/// Memory snapshot for telemetry.
/// </summary>
public struct MemorySnapshot
{
    public double WorkingSetMb;
    public double GcHeapMb;
}

/// <summary>
/// Aggregate telemetry for the inference engine.
/// </summary>
public class InferenceEngineTelemetry
{
    public long TotalInferences { get; init; }
    public long TotalTokensGenerated { get; init; }
    public long TotalViolations { get; init; }
    public DateTime? LastInferenceAt { get; init; }
    public InferenceSessionTelemetry? ActiveSession { get; init; }

    // ── KV cache state ──
    public int KvTokensInCache { get; init; } = -1;
    public int KvUsedCells { get; init; } = -1;
    public bool FreshContextPerRequest { get; init; }

    // ── Cleanup telemetry ──
    public CleanupTelemetry? Cleanup { get; init; }

    // ── Survivability metrics ──
    public bool RuntimeOperational { get; init; }
    public double RecentSuccessRate { get; init; } = 1.0;
    public int ConsecutiveFailures { get; init; }
    public long GeneratedTokensSinceReset { get; init; }
    public DateTime? LastSuccessfulInferenceAt { get; init; }
    public bool RuntimeDegraded { get; init; }
}
