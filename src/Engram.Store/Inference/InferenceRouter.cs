using Engram.Store.Cloud;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Inference;

/// <summary>
/// Routes inference requests between local SLM (Eco mode) and cloud (Turbo mode).
/// This is the single entry point for all chat completions.
///
/// Eco mode:  Phi-4-mini via LLamaSharp (free, offline, ~3GB RAM)
/// Turbo mode: Gemini 3 Flash / Claude 4.5 Sonnet (Pro tier, internet required)
/// </summary>
public class InferenceRouter : IDisposable
{
    private readonly LocalInferenceEngine _localEngine;
    private readonly CloudCallPipeline? _cloudPipeline;
    private readonly ILogger<InferenceRouter>? _logger;
    private PowerMode _powerMode = PowerMode.Eco;

    public InferenceRouter(
        LocalInferenceEngine localEngine,
        CloudCallPipeline? cloudPipeline = null,
        ILogger<InferenceRouter>? logger = null)
    {
        _localEngine = localEngine;
        _cloudPipeline = cloudPipeline;
        _logger = logger;
    }

    /// <summary>
    /// Current power mode.
    /// </summary>
    public PowerMode PowerMode
    {
        get => _powerMode;
        set
        {
            _powerMode = value;
            _logger?.LogInformation("Power mode changed to: {Mode}", value);
        }
    }

    /// <summary>
    /// Is the local engine ready to serve requests?
    /// </summary>
    public bool IsLocalReady => _localEngine.IsReady;

    /// <summary>
    /// Get the local engine for model management.
    /// </summary>
    public LocalInferenceEngine LocalEngine => _localEngine;

    /// <summary>
    /// Route a chat completion request to the appropriate engine.
    /// </summary>
    public async Task<InferenceResult> ChatCompletionAsync(
        ChatMessage[] messages,
        int maxTokens = 1024,
        CancellationToken cancellationToken = default)
    {
        // Try local first in Eco mode
        if (_powerMode == PowerMode.Eco)
        {
            if (_localEngine.IsReady)
            {
                _logger?.LogDebug("Routing to local engine (Eco mode)");
                return await _localEngine.ChatCompletionAsync(messages, maxTokens, cancellationToken);
            }

            // Local not ready — try cloud fallback if available
            if (_cloudPipeline != null)
            {
                _logger?.LogWarning("Local engine not ready, falling back to cloud");
                return await RouteToCloud(messages, maxTokens, cancellationToken);
            }

            return InferenceResult.Failed(
                "Local model not loaded. Download it from Settings > Power Mode.");
        }

        // Turbo mode — use cloud
        if (_cloudPipeline != null)
        {
            _logger?.LogDebug("Routing to cloud engine (Turbo mode)");
            return await RouteToCloud(messages, maxTokens, cancellationToken);
        }

        // Turbo mode but no cloud — fallback to local
        if (_localEngine.IsReady)
        {
            _logger?.LogWarning("Cloud not available, falling back to local engine");
            return await _localEngine.ChatCompletionAsync(messages, maxTokens, cancellationToken);
        }

        return InferenceResult.Failed(
            "No inference engine available. Download the local model or enable Pro tier.");
    }

    /// <summary>
    /// Route a streaming chat completion.
    /// </summary>
    public async IAsyncEnumerable<string> ChatCompletionStreamAsync(
        ChatMessage[] messages,
        int maxTokens = 1024,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_powerMode == PowerMode.Eco && _localEngine.IsReady)
        {
            await foreach (var token in _localEngine.ChatCompletionStreamAsync(messages, maxTokens, cancellationToken))
            {
                yield return token;
            }
            yield break;
        }

        // Non-streaming fallback for cloud
        var result = await ChatCompletionAsync(messages, maxTokens, cancellationToken);
        if (result.Success)
            yield return result.Content;
    }

    private async Task<InferenceResult> RouteToCloud(
        ChatMessage[] messages,
        int maxTokens,
        CancellationToken cancellationToken)
    {
        var lastMessage = messages.Length > 0 ? messages[^1].Content : "";
        var complexity = EstimateComplexity(lastMessage);

        var request = new CloudCallRequest
        {
            Reason = "Chat completion",
            Complexity = complexity,
            Payload = string.Join("\n", messages.Select(m => $"{m.Role}: {m.Content}")),
            PrivacyClass = PrivacyClass.Internal,
            MaxTokens = maxTokens
        };

        var result = await _cloudPipeline!.ExecuteAsync(request, cancellationToken);

        return new InferenceResult
        {
            Success = result.Status == PipelineStatus.Completed,
            Content = result.Content,
            InputTokens = result.InputTokens,
            OutputTokens = result.OutputTokens,
            Model = result.Model,
            Provider = "cloud",
            ErrorMessage = result.ErrorMessage
        };
    }

    private static TaskComplexity EstimateComplexity(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return TaskComplexity.Low;

        var wordCount = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        // Simple heuristic: longer/more complex queries need more powerful model
        if (wordCount > 100 || text.Contains("analyze") || text.Contains("compare") || text.Contains("research"))
            return TaskComplexity.High;

        if (wordCount > 20 || text.Contains("summarize") || text.Contains("explain"))
            return TaskComplexity.Medium;

        return TaskComplexity.Low;
    }

    public void Dispose()
    {
        _localEngine.Dispose();
    }
}

public enum PowerMode
{
    Eco = 0,    // Local SLM (Phi-4-mini via LLamaSharp)
    Turbo = 1   // Cloud API (Gemini Flash / Claude Sonnet)
}
