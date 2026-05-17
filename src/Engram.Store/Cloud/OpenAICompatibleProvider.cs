using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Cloud;

/// <summary>
/// Generic cloud model provider that works with ANY OpenAI-compatible API.
/// Supports: OpenAI, Groq, Together.ai, Ollama, LM Studio, vLLM, etc.
/// User provides API key + base URL + model name.
/// </summary>
public class OpenAICompatibleProvider : ICloudModelProvider
{
    private readonly HttpClient _http;
    private readonly ILogger<OpenAICompatibleProvider>? _logger;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly string _modelName;
    private readonly string _providerName;
    private bool _disposed;

    public string ProviderName => _providerName;
    public string ModelName => _modelName;
    public bool IsAvailable => !string.IsNullOrEmpty(_apiKey) || _baseUrl.Contains("localhost");

    /// <summary>
    /// Create a provider for any OpenAI-compatible API.
    /// </summary>
    /// <param name="apiKey">API key (can be empty for local APIs like Ollama)</param>
    /// <param name="baseUrl">Base URL (e.g., "https://api.openai.com/v1" or "http://localhost:11434/v1")</param>
    /// <param name="modelName">Model name (e.g., "gpt-4o", "llama-3.3-70b", "mixtral-8x7b")</param>
    /// <param name="providerName">Friendly name for audit logging</param>
    public OpenAICompatibleProvider(
        string apiKey,
        string baseUrl,
        string modelName,
        string providerName = "custom",
        ILogger<OpenAICompatibleProvider>? logger = null)
    {
        _apiKey = apiKey ?? "";
        _baseUrl = baseUrl?.TrimEnd('/') ?? "";
        _modelName = modelName ?? "";
        _providerName = providerName;
        _logger = logger;

        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };

        if (!string.IsNullOrEmpty(_apiKey))
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
    }

    public async Task<CloudModelResponse> SendAsync(
        CloudModelRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger?.LogInformation("Sending to {Provider}/{Model}: {Reason}",
                _providerName, _modelName, request.Reason);

            var payload = new
            {
                model = _modelName,
                messages = new[]
                {
                    new { role = "user", content = request.Payload }
                },
                max_tokens = request.MaxTokens,
                temperature = 0.7,
                top_p = 0.9
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _http.PostAsync($"{_baseUrl}/chat/completions", content, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogError("API error {Status}: {Body}", response.StatusCode, responseBody[..Math.Min(200, responseBody.Length)]);
                return new CloudModelResponse
                {
                    Success = false,
                    ErrorMessage = $"API error {(int)response.StatusCode}: {responseBody[..Math.Min(200, responseBody.Length)]}",
                    Provider = _providerName,
                    Model = _modelName
                };
            }

            var result = JsonSerializer.Deserialize<OpenAIResponse>(responseBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var responseText = result?.Choices?.FirstOrDefault()?.Message?.Content ?? "";
            var inputTokens = result?.Usage?.PromptTokens ?? request.Payload.Length / 4;
            var outputTokens = result?.Usage?.CompletionTokens ?? responseText.Length / 4;

            _logger?.LogInformation("Response received: {Tokens} tokens",
                inputTokens + outputTokens);

            return new CloudModelResponse
            {
                Success = true,
                Content = responseText,
                Provider = _providerName,
                Model = _modelName,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                CostEstimate = EstimateCost(inputTokens, outputTokens)
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Cloud call to {Provider} failed", _providerName);
            return new CloudModelResponse
            {
                Success = false,
                ErrorMessage = ex.Message,
                Provider = _providerName,
                Model = _modelName
            };
        }
    }

    private decimal EstimateCost(int inputTokens, int outputTokens)
    {
        // Generic pricing estimate — actual cost depends on provider
        return _providerName.ToLowerInvariant() switch
        {
            "openai" => (inputTokens * 0.005m + outputTokens * 0.015m) / 1000,
            "groq" => (inputTokens * 0.0001m + outputTokens * 0.0001m) / 1000,
            "together" => (inputTokens * 0.0004m + outputTokens * 0.0004m) / 1000,
            _ => (inputTokens * 0.001m + outputTokens * 0.002m) / 1000
        };
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _http.Dispose();
            _disposed = true;
        }
    }

    // OpenAI response format
    private class OpenAIResponse
    {
        public OpenAIChoice[]? Choices { get; set; }
        public OpenAIUsage? Usage { get; set; }
    }

    private class OpenAIChoice
    {
        public OpenAIMessage? Message { get; set; }
    }

    private class OpenAIMessage
    {
        public string? Content { get; set; }
    }

    private class OpenAIUsage
    {
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
    }
}
