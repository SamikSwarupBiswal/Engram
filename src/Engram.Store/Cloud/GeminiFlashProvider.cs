namespace Engram.Store.Cloud;

/// <summary>
/// Gemini 3 Flash provider — cheap cloud model for routine tasks.
/// Managed credit pooling — no user API keys.
/// STUB: Replace with actual Gemini API integration.
/// </summary>
public class GeminiFlashProvider : ICloudModelProvider
{
    private readonly string? _apiKey;

    public GeminiFlashProvider(string? apiKey = null)
    {
        _apiKey = apiKey;
    }

    public bool IsAvailable => !string.IsNullOrEmpty(_apiKey);
    public string ProviderName => "gemini-flash";
    public string ModelName => "gemini-3-flash";

    public Task<CloudModelResponse> SendAsync(CloudModelRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return Task.FromResult(new CloudModelResponse
            {
                Content = string.Empty,
                Provider = ProviderName,
                Model = ModelName,
                Success = false,
                ErrorMessage = "Gemini Flash provider not configured (no API key)."
            });
        }

        // STUB: Real implementation would call Gemini API
        // Cost model: ~$0.075/M input tokens, ~$0.30/M output tokens
        var inputTokens = EstimateTokens(request.Payload);
        var outputTokens = Math.Min(request.MaxTokens, 200);
        var cost = (inputTokens * 0.000075m) + (outputTokens * 0.0003m);

        return Task.FromResult(new CloudModelResponse
        {
            Content = $"[Gemini Flash STUB] Response to: {request.Reason}",
            Provider = ProviderName,
            Model = ModelName,
            CostEstimate = cost,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            Success = true
        });
    }

    private static int EstimateTokens(string text)
    {
        // Rough estimate: ~4 chars per token
        return (text?.Length ?? 0) / 4;
    }
}
