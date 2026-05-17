using LLama;
using LLama.Common;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Inference;

/// <summary>
/// Local SLM inference engine using LLamaSharp.
/// Loads Phi-4-mini GGUF and generates chat completions.
/// Runs entirely on-device — no network calls.
/// </summary>
public class LocalInferenceEngine : IDisposable
{
    private readonly ILogger<LocalInferenceEngine>? _logger;
    private readonly ModelManager _modelManager;
    private readonly GpuDetector _gpuDetector;
    private LLamaWeights? _model;
    private LLamaContext? _context;
    private ModelConfig? _loadedConfig;
    private GpuInfo? _gpuInfo;
    private bool _disposed;
    private bool _isLoading;
    private readonly object _loadLock = new();

    public bool IsReady => _model != null && _context != null;
    public bool IsLoading => _isLoading;
    public GpuInfo? GpuInfo => _gpuInfo;
    public ModelConfig? LoadedModel => _loadedConfig;

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
                return false;
            }

            _logger?.LogInformation("Loading model: {Name} from {Path}", config.Name, modelPath);
            _gpuInfo = _gpuDetector.Detect();
            _logger?.LogInformation("Using: {Device} (layers={Layers})", _gpuInfo.Description, _gpuInfo.LayerCount);

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
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load model: {Name}", config.Name);
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
    /// </summary>
    public async Task<InferenceResult> ChatCompletionAsync(
        ChatMessage[] messages,
        int maxTokens = 1024,
        CancellationToken cancellationToken = default)
    {
        if (!IsReady)
            return InferenceResult.Failed("Model not loaded. Download and load a model first.");

        try
        {
            var prompt = FormatChatPrompt(messages);
            var executor = new InteractiveExecutor(_context!);

            var inferenceParams = new InferenceParams
            {
                MaxTokens = maxTokens,
                AntiPrompts = new[] { "User:", "Assistant:", "\nUser:" }
            };

            _logger?.LogDebug("Generating response (maxTokens={MaxTokens})", maxTokens);

            var responseBuilder = new System.Text.StringBuilder();
            await foreach (var token in executor.InferAsync(prompt, inferenceParams, cancellationToken))
            {
                responseBuilder.Append(token);
            }

            var response = responseBuilder.ToString().Trim();

            // Clean up common artifacts
            if (response.StartsWith("Assistant:"))
                response = response["Assistant:".Length..].Trim();
            if (response.EndsWith("User:"))
                response = response[..^"User:".Length].Trim();

            return new InferenceResult
            {
                Success = true,
                Content = response,
                InputTokens = EstimateTokenCount(prompt),
                OutputTokens = EstimateTokenCount(response),
                Model = _loadedConfig?.Name ?? "unknown",
                Provider = "local"
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Inference failed");
            return InferenceResult.Failed($"Inference error: {ex.Message}");
        }
    }

    /// <summary>
    /// Generate a streaming chat completion. Yields tokens as they're generated.
    /// </summary>
    public async IAsyncEnumerable<string> ChatCompletionStreamAsync(
        ChatMessage[] messages,
        int maxTokens = 1024,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!IsReady)
            yield break;

        var prompt = FormatChatPrompt(messages);
        var executor = new InteractiveExecutor(_context!);

        var inferenceParams = new InferenceParams
        {
            MaxTokens = maxTokens,
            AntiPrompts = new[] { "User:", "Assistant:", "\nUser:" }
        };

        await foreach (var token in executor.InferAsync(prompt, inferenceParams, cancellationToken))
        {
            yield return token;
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
        // Rough estimate: ~4 chars per token for English
        return text.Length / 4;
    }

    /// <summary>
    /// Unload the model from memory.
    /// </summary>
    public void UnloadModel()
    {
        lock (_loadLock)
        {
            _context?.Dispose();
            _context = null;
            _model?.Dispose();
            _model = null;
            _loadedConfig = null;
            _logger?.LogInformation("Model unloaded");
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

    public static InferenceResult Failed(string error) => new()
    {
        Success = false,
        ErrorMessage = error,
        Provider = "local"
    };
}
