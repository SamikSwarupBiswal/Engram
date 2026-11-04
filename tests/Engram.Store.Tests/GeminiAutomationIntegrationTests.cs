using System.Net;
using System.Text;
using System.Text.Json;
using Engram.Store;
using Engram.Store.Cloud;
using Engram.Store.Inference;

namespace Engram.Store.Tests;

public sealed class GeminiAutomationIntegrationTests
{
    [Fact]
    public async Task ModelResolver_SelectsHighestStableFullFlashModelAtRuntime()
    {
        var handler = new JsonHandler("""
        {
          "models": [
            { "name": "models/gemini-3-flash-preview", "supportedGenerationMethods": ["generateContent"], "outputTokenLimit": 65536 },
            { "name": "models/gemini-3.1-flash-lite", "supportedGenerationMethods": ["generateContent"], "outputTokenLimit": 65536 },
            { "name": "models/gemini-3.5-flash", "supportedGenerationMethods": ["generateContent"], "outputTokenLimit": 65536 },
            { "name": "models/gemini-9-pro", "supportedGenerationMethods": ["generateContent"], "outputTokenLimit": 65536 }
          ]
        }
        """);
        var resolver = new GeminiModelResolver(new HttpClient(handler));

        var model = await resolver.ResolveAsync("test-key");

        Assert.Equal("gemini-3.5-flash", model);
        Assert.Equal("test-key", handler.LastRequest?.Headers.GetValues("x-goog-api-key").Single());
    }

    [Fact]
    public async Task ModelResolver_ExplicitConfigurationBypassesDiscovery()
    {
        var handler = new JsonHandler("{}") { ResponseStatus = HttpStatusCode.InternalServerError };
        var resolver = new GeminiModelResolver(new HttpClient(handler));

        var model = await resolver.ResolveAsync("test-key", "models/custom-flash-model");

        Assert.Equal("custom-flash-model", model);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task ModelResolver_ReturnsDynamicallyRankedFallbackCandidates()
    {
        var handler = new JsonHandler("""
        {
          "models": [
            { "name": "models/gemini-3.5-flash", "supportedGenerationMethods": ["generateContent"], "outputTokenLimit": 65536 },
            { "name": "models/gemini-2.5-flash", "supportedGenerationMethods": ["generateContent"], "outputTokenLimit": 65536 },
            { "name": "models/gemini-3.1-flash-lite", "supportedGenerationMethods": ["generateContent"], "outputTokenLimit": 65536 },
            { "name": "models/gemini-9-flash-image", "supportedGenerationMethods": ["generateContent"], "outputTokenLimit": 65536 }
          ]
        }
        """);
        var resolver = new GeminiModelResolver(new HttpClient(handler));

        var candidates = await resolver.ResolveCandidatesAsync("test-key");

        Assert.Equal(new[] { "gemini-3.5-flash", "gemini-2.5-flash", "gemini-3.1-flash-lite" }, candidates);
    }

    [Fact]
    public async Task OpenAiCompatibleGemini_UsesRoleMessagesAndThinkingAwareCompletionBudget()
    {
        var handler = new JsonHandler("""
        { "choices": [{ "message": { "content": "[{\"id\":\"step-1\"}]" }, "finish_reason": "stop" }],
          "usage": { "prompt_tokens": 12, "completion_tokens": 8 } }
        """);
        var provider = new OpenAICompatibleProvider("key", "https://gemini.test/v1beta/openai",
            "runtime-model", "gemini-flash", httpClient: new HttpClient(handler));

        var response = await provider.SendAsync(new CloudModelRequest
        {
            Payload = "fallback",
            MaxTokens = 4096,
            Messages = new[]
            {
                new CloudModelMessage("system", "Return JSON actions."),
                new CloudModelMessage("user", "Open Word.")
            },
            Metadata = new Dictionary<string, string>
            {
                ["reasoning_effort"] = "low",
                ["response_json_schema"] = """{"type":"object","properties":{"answer":{"type":"string"}},"required":["answer"],"additionalProperties":false}"""
            }
        });

        Assert.True(response.Success);
        Assert.Equal(12, response.InputTokens);
        Assert.Equal(8, response.OutputTokens);
        using var request = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal(4096, request.RootElement.GetProperty("max_completion_tokens").GetInt32());
        Assert.Equal("low", request.RootElement.GetProperty("reasoning_effort").GetString());
        Assert.False(request.RootElement.TryGetProperty("max_tokens", out _));
        Assert.Equal("system", request.RootElement.GetProperty("messages")[0].GetProperty("role").GetString());
        var format = request.RootElement.GetProperty("response_format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.True(format.GetProperty("json_schema").GetProperty("strict").GetBoolean());
    }

    [Fact]
    public async Task OpenAiCompatibleGemini_RejectsTruncatedCompletion()
    {
        var handler = new JsonHandler("""
        { "choices": [{ "message": { "content": "[{" }, "finish_reason": "length" }],
          "usage": { "prompt_tokens": 12, "completion_tokens": 100 } }
        """);
        var provider = new OpenAICompatibleProvider("key", "https://gemini.test/v1beta/openai",
            "runtime-model", "gemini-flash", httpClient: new HttpClient(handler));

        var response = await provider.SendAsync(new CloudModelRequest { Payload = "plan", MaxTokens = 100 });

        Assert.False(response.Success);
        Assert.Contains("token budget", response.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenAiCompatibleProvider_RetriesTransientServiceUnavailable()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.ServiceUnavailable, "{\"error\":\"busy\"}"),
            (HttpStatusCode.OK, """
            { "choices": [{ "message": { "content": "RECOVERED" }, "finish_reason": "stop" }],
              "usage": { "prompt_tokens": 3, "completion_tokens": 2 } }
            """));
        var provider = new OpenAICompatibleProvider(
            "key",
            "https://gemini.test/v1beta/openai",
            "runtime-model",
            "gemini-flash",
            httpClient: new HttpClient(handler),
            maxTransientRetries: 1,
            baseRetryDelay: TimeSpan.Zero);

        var result = await provider.SendAsync(new CloudModelRequest { Payload = "test", MaxTokens = 32 });

        Assert.True(result.Success);
        Assert.Equal("RECOVERED", result.Content);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task OpenAiCompatibleProvider_FallsBackToDiscoveredModelAfterTransientExhaustion()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.ServiceUnavailable, "{\"error\":\"busy\"}"),
            (HttpStatusCode.OK, """
            { "model": "fallback-snapshot", "choices": [{ "message": { "content": "FALLBACK_OK" }, "finish_reason": "stop" }],
              "usage": { "prompt_tokens": 4, "completion_tokens": 2 } }
            """));
        var provider = new OpenAICompatibleProvider(
            "key",
            "https://gemini.test/v1beta/openai",
            "primary-flash",
            "gemini-flash",
            httpClient: new HttpClient(handler),
            maxTransientRetries: 0,
            baseRetryDelay: TimeSpan.Zero,
            fallbackModelNames: new[] { "fallback-flash" });

        var result = await provider.SendAsync(new CloudModelRequest { Payload = "test", MaxTokens = 32 });

        Assert.True(result.Success);
        Assert.Equal("FALLBACK_OK", result.Content);
        Assert.Equal("fallback-snapshot", result.Model);
        Assert.Contains("\"model\":\"primary-flash\"", handler.RequestBodies[0]);
        Assert.Contains("\"model\":\"fallback-flash\"", handler.RequestBodies[1]);
    }

    [Fact]
    public async Task OpenAiCompatibleProvider_SkipsAdvertisedButUnavailableModel()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.NotFound, "{\"error\":\"model unavailable\"}"),
            (HttpStatusCode.OK, """
            { "choices": [{ "message": { "content": "NEXT_MODEL_OK" }, "finish_reason": "stop" }],
              "usage": { "prompt_tokens": 4, "completion_tokens": 2 } }
            """));
        var provider = new OpenAICompatibleProvider(
            "key",
            "https://gemini.test/v1beta/openai",
            "advertised-but-unavailable",
            "gemini-flash",
            httpClient: new HttpClient(handler),
            maxTransientRetries: 0,
            fallbackModelNames: new[] { "available-model" });

        var result = await provider.SendAsync(new CloudModelRequest { Payload = "test", MaxTokens = 32 });

        Assert.True(result.Success);
        Assert.Equal("NEXT_MODEL_OK", result.Content);
        Assert.Equal("available-model", result.Model);
    }

    private sealed class JsonHandler(string responseJson) : HttpMessageHandler
    {
        public HttpStatusCode ResponseStatus { get; init; } = HttpStatusCode.OK;
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(ResponseStatus)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class SequenceHandler(params (HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new(responses);
        public int CallCount { get; private set; }
        public List<string> RequestBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestBodies.Add(request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            var response = _responses.Dequeue();
            return new HttpResponseMessage(response.Status)
            {
                Content = new StringContent(response.Body, Encoding.UTF8, "application/json")
            };
        }
    }
}
