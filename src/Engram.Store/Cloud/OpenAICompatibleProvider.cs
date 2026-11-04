using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Cloud;

/// <summary>
/// Generic cloud model provider that works with ANY OpenAI-compatible API.
/// Supports: OpenAI, Groq, Together.ai, Ollama, LM Studio, vLLM, etc.
/// User provides API key + base URL + model name.
/// </summary>
public class OpenAICompatibleProvider : ICloudModelProvider
{
    private const string ClientTimeoutError = "The cloud model request timed out.";

    private readonly HttpClient _http;
    private readonly ILogger<OpenAICompatibleProvider>? _logger;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly IReadOnlyList<string> _modelNames;
    private readonly string _providerName;
    private readonly int _maxTransientRetries;
    private readonly TimeSpan _baseRetryDelay;
    private bool _disposed;

    public string ProviderName => _providerName;
    public string ModelName => _modelNames[0];
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
        ILogger<OpenAICompatibleProvider>? logger = null,
        HttpClient? httpClient = null,
        int maxTransientRetries = 2,
        TimeSpan? baseRetryDelay = null,
        IEnumerable<string>? fallbackModelNames = null)
    {
        _apiKey = apiKey ?? "";
        _baseUrl = baseUrl?.TrimEnd('/') ?? "";
        _modelNames = new[] { modelName ?? string.Empty }
            .Concat(fallbackModelNames ?? Array.Empty<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (_modelNames.Count == 0)
            throw new ArgumentException("At least one model name is required.", nameof(modelName));
        _providerName = providerName;
        _logger = logger;
        _maxTransientRetries = Math.Max(0, maxTransientRetries);
        _baseRetryDelay = baseRetryDelay ?? TimeSpan.FromMilliseconds(750);

        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(45) };

        if (!string.IsNullOrEmpty(_apiKey))
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
    }

    public async Task<CloudModelResponse> SendAsync(
        CloudModelRequest request,
        CancellationToken cancellationToken = default)
    {
        CloudModelResponse? lastResponse = null;
        foreach (var modelName in _modelNames)
        {
            lastResponse = await SendWithModelAsync(request, modelName, cancellationToken);
            if (lastResponse.Success || cancellationToken.IsCancellationRequested || !IsModelFallbackEligible(lastResponse))
                return lastResponse;

            _logger?.LogWarning(
                "Transient failures exhausted for {Provider}/{Model}; trying the next dynamically discovered model.",
                _providerName, modelName);
        }

        return lastResponse ?? new CloudModelResponse
        {
            Success = false,
            ErrorMessage = "No configured model could satisfy the request.",
            Provider = _providerName,
            Model = ModelName
        };
    }

    private async Task<CloudModelResponse> SendWithModelAsync(
        CloudModelRequest request,
        string modelName,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger?.LogInformation("Sending to {Provider}/{Model}: {Reason}",
                _providerName, modelName, request.Reason);

            var messages = request.Messages.Count > 0
                ? request.Messages.Select(message => new { role = message.Role, content = message.Content }).ToArray()
                : new[] { new { role = "user", content = request.Payload } };
            var payload = new Dictionary<string, object?>
            {
                ["model"] = modelName,
                ["messages"] = messages,
                ["temperature"] = 0.2,
                ["top_p"] = 0.9
            };
            if (_providerName.StartsWith("gemini", StringComparison.OrdinalIgnoreCase))
            {
                payload["max_completion_tokens"] = request.MaxTokens;
                payload["reasoning_effort"] = request.Metadata.TryGetValue("reasoning_effort", out var effort)
                    ? effort
                    : "low";
            }
            else
            {
                payload["max_tokens"] = request.MaxTokens;
            }

            if (request.Metadata.TryGetValue("response_json_schema", out var responseJsonSchema))
            {
                using var schema = JsonDocument.Parse(responseJsonSchema);
                if (schema.RootElement.ValueKind != JsonValueKind.Object)
                    throw new JsonException("The response JSON schema must be an object.");

                payload["response_format"] = new
                {
                    type = "json_schema",
                    json_schema = new { name = "engram_response", strict = true, schema = schema.RootElement.Clone() }
                };
            }

            var json = JsonSerializer.Serialize(payload);
            string responseBody = string.Empty;
            HttpResponseMessage? response = null;
            for (var attempt = 0; attempt <= _maxTransientRetries; attempt++)
            {
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                response?.Dispose();
                response = await _http.PostAsync($"{_baseUrl}/chat/completions", content, cancellationToken);
                responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                if (response.IsSuccessStatusCode)
                    break;

                if (!IsTransient(response.StatusCode) || attempt >= _maxTransientRetries)
                {
                    _logger?.LogError("API error {Status}: {Body}", response.StatusCode, responseBody[..Math.Min(200, responseBody.Length)]);
                    response.Dispose();
                    return new CloudModelResponse
                    {
                        Success = false,
                        ErrorMessage = $"API error {(int)response.StatusCode}: {responseBody[..Math.Min(200, responseBody.Length)]}",
                        Provider = _providerName,
                        Model = modelName
                    };
                }

                var delay = GetRetryDelay(response, attempt);
                _logger?.LogWarning(
                    "Transient API error {Status}; retrying {Provider}/{Model} in {DelayMs} ms (attempt {Attempt}/{Total}).",
                    response.StatusCode, _providerName, modelName, delay.TotalMilliseconds, attempt + 2, _maxTransientRetries + 1);
                await Task.Delay(delay, cancellationToken);
            }

            response?.Dispose();

            var result = JsonSerializer.Deserialize<OpenAIResponse>(responseBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var choice = result?.Choices?.FirstOrDefault();
            var responseText = choice?.Message?.Content ?? "";
            var inputTokens = result?.Usage?.PromptTokens ?? request.Payload.Length / 4;
            var outputTokens = result?.Usage?.CompletionTokens ?? responseText.Length / 4;

            if (string.IsNullOrWhiteSpace(responseText) ||
                choice?.FinishReason?.Equals("length", StringComparison.OrdinalIgnoreCase) == true)
            {
                return new CloudModelResponse
                {
                    Success = false,
                    ErrorMessage = choice?.FinishReason?.Equals("length", StringComparison.OrdinalIgnoreCase) == true
                        ? "The model response exceeded its completion-token budget."
                        : "The model returned an empty response.",
                    Provider = _providerName,
                    Model = modelName,
                    InputTokens = inputTokens,
                    OutputTokens = outputTokens
                };
            }

            _logger?.LogInformation("Response received: {Tokens} tokens",
                inputTokens + outputTokens);

            return new CloudModelResponse
            {
                Success = true,
                Content = responseText,
                Provider = _providerName,
                Model = string.IsNullOrWhiteSpace(result?.Model) ? modelName : result.Model,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                CostEstimate = EstimateCost(inputTokens, outputTokens)
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Cloud call to {Provider}/{Model} failed", _providerName, modelName);
            var clientTimedOut = !cancellationToken.IsCancellationRequested &&
                ex is OperationCanceledException { InnerException: TimeoutException };
            return new CloudModelResponse
            {
                Success = false,
                ErrorMessage = clientTimedOut ? ClientTimeoutError : ex.Message,
                Provider = _providerName,
                Model = modelName
            };
        }
    }

    private static bool IsModelFallbackEligible(CloudModelResponse response)
    {
        if (response.Success || string.IsNullOrWhiteSpace(response.ErrorMessage)) return false;
        return response.ErrorMessage == ClientTimeoutError ||
            new[] { "API error 404:", "API error 408:", "API error 429:", "API error 500:", "API error 502:", "API error 503:", "API error 504:" }
            .Any(prefix => response.ErrorMessage.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static bool IsTransient(System.Net.HttpStatusCode statusCode) => statusCode is
        System.Net.HttpStatusCode.RequestTimeout or
        System.Net.HttpStatusCode.TooManyRequests or
        System.Net.HttpStatusCode.InternalServerError or
        System.Net.HttpStatusCode.BadGateway or
        System.Net.HttpStatusCode.ServiceUnavailable or
        System.Net.HttpStatusCode.GatewayTimeout;

    private TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
            return delta;
        if (retryAfter?.Date is { } date)
        {
            var remaining = date - DateTimeOffset.UtcNow;
            if (remaining > TimeSpan.Zero) return remaining;
        }

        return TimeSpan.FromMilliseconds(_baseRetryDelay.TotalMilliseconds * Math.Pow(2, attempt));
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
        public string? Model { get; set; }
        public OpenAIChoice[]? Choices { get; set; }
        public OpenAIUsage? Usage { get; set; }
    }

    private class OpenAIChoice
    {
        public OpenAIMessage? Message { get; set; }

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }
    }

    private class OpenAIMessage
    {
        public string? Content { get; set; }
    }

    private class OpenAIUsage
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; set; }
    }
}
