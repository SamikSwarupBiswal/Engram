using LLama;
using LLama.Common;
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

    public bool IsReady => _model != null && _context != null;
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

    /// <summary>
    /// Generate a chat completion from messages.
    /// Creates an InferenceSession with heartbeat tracking and watchdog.
    /// </summary>
    public async Task<InferenceResult> ChatCompletionAsync(
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

        session.Start();

        try
        {
            var prompt = FormatChatPrompt(messages);
            var executor = new InteractiveExecutor(_context!);

            var inferenceParams = new InferenceParams
            {
                MaxTokens = maxTokens,
                AntiPrompts = new[] { "User:", "Assistant:", "\nUser:" }
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

            // Clean up common artifacts
            if (response.StartsWith("Assistant:"))
                response = response["Assistant:".Length..].Trim();
            if (response.EndsWith("User:"))
                response = response[..^"User:".Length].Trim();

            // Memory telemetry: snapshot after
            var memAfter = GetMemorySnapshot();
            var memDelta = memAfter.WorkingSetMb - memBefore.WorkingSetMb;

            // Update global stats
            Interlocked.Increment(ref _totalInferences);
            Interlocked.Add(ref _totalTokensGenerated, tokenCount);
            _lastInferenceAt = DateTime.UtcNow;

            _log.Inference($"Session {session.SessionId}: complete — {tokenCount} tokens " +
                $"in {session.Elapsed.TotalSeconds:F1}s " +
                $"({tokenCount / Math.Max(0.1, session.Elapsed.TotalSeconds):F1} tok/s) " +
                $"[memΔ: {memDelta:+0;-0}MB]");

            return new InferenceResult
            {
                Success = true,
                Content = response,
                InputTokens = EstimateTokenCount(prompt),
                OutputTokens = (int)tokenCount,
                Model = _loadedConfig?.Name ?? "unknown",
                Provider = "local",
                SessionTelemetry = session.GetTelemetry(),
                MemoryDeltaMb = memDelta
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
                AntiPrompts = new[] { "User:", "Assistant:", "\nUser:" }
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
    /// Format messages into a chat prompt template.
    /// </summary>
    private static string FormatChatPrompt(ChatMessage[] messages)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("You are Engram, a personal semantic memory layer assistant. You help the user search their memory, generate briefs, and answer questions about their digital life.");

        foreach (var msg in messages)
        {
            var role = msg.Role.ToLowerInvariant() switch
            {
                "user" => "User",
                "assistant" => "Assistant",
                "system" => "System",
                _ => "User"
            };
            sb.AppendLine($"{role}: {msg.Content}");
        }

        sb.Append("Assistant:");
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
            return new InferenceEngineTelemetry
            {
                TotalInferences = Interlocked.Read(ref _totalInferences),
                TotalTokensGenerated = Interlocked.Read(ref _totalTokensGenerated),
                TotalViolations = Interlocked.Read(ref _totalViolations),
                LastInferenceAt = _lastInferenceAt,
                ActiveSession = _activeSession?.GetTelemetry()
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

    public static InferenceResult Failed(string error) => new()
    {
        Success = false,
        ErrorMessage = error,
        Provider = "local"
    };
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
}
