using System.Text.Json;
using Engram.Store.Cloud;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Tests;

public sealed class ProviderScopedMemoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "engram-memory-cloud-" + Guid.NewGuid().ToString("N"));
    private const string HistoricalText = "A private generated welcome for the new teammate.";
    private const string OriginalScope = "cloud-v1-1111111111111111111111111111111111111111111111111111111111111111";
    private const string OtherScope = "cloud-v1-2222222222222222222222222222222222222222222222222222222222222222";

    private CloudCallPipeline Pipeline(params ICloudModelProvider[] providers)
    {
        var config = new EngramConfig { Tier = TierLevel.Pro, CloudEnabled = true };
        var guard = new TierGuard(config);
        var audit = new CloudAuditLog(Path.Combine(_root, "logs"));
        return new(new ModelRouter(guard), new LocalFilter(), guard, new CloudRateLimiter(30, 200),
            new BudgetManager(BudgetConfig.FromConfig(config), audit), audit,
            new CleanCache(Path.Combine(_root, "cache")), providers);
    }

    private static CloudCallRequest Request(string memoryIdentity = OriginalScope) => new()
    {
        Reason = "Recall task output", Complexity = TaskComplexity.High,
        Payload = HistoricalText + " in a redundant legacy payload",
        Messages = [new("user", "Quote the note."), new("user", HistoricalText) { MemoryProvider = memoryIdentity }],
        PrivacyClass = PrivacyClass.Internal
    };

    [Fact]
    public async Task OriginalProviderReceivesMemoryWithoutCachingOrAuditContent()
    {
        var provider = new RecordingProvider("claude-sonnet", OriginalScope);
        var pipeline = Pipeline(provider);
        var first = await pipeline.ExecuteAsync(Request());
        var second = await pipeline.ExecuteAsync(Request());
        Assert.Equal(PipelineStatus.Completed, first.Status);
        Assert.Equal(OriginalScope, first.DisclosureIdentity);
        Assert.False(second.FromCache);
        Assert.Equal(2, provider.Requests.Count);
        Assert.Contains(HistoricalText, provider.Requests[0].Payload);
        Assert.Contains(provider.Requests[0].Messages, message => message.Content == HistoricalText);
        Assert.DoesNotContain(HistoricalText, File.ReadAllText(Path.Combine(_root, "logs", "cloud-audit.jsonl")));
        foreach (var file in Directory.GetFiles(_root, "*", SearchOption.AllDirectories))
            Assert.DoesNotContain(HistoricalText, File.ReadAllText(file));
    }

    [Fact]
    public async Task FallbackProviderCannotReceiveMemoryThroughMessagesPayloadOrCachedReply()
    {
        var original = new RecordingProvider("claude-sonnet", OriginalScope);
        var fallback = new RecordingProvider("gemini-flash", OtherScope);
        var pipeline = Pipeline(original, fallback);
        await pipeline.ExecuteAsync(Request());
        original.IsAvailable = false;
        var result = await pipeline.ExecuteAsync(Request());
        Assert.Equal(PipelineStatus.Completed, result.Status);
        Assert.False(result.FromCache);
        var request = Assert.Single(fallback.Requests);
        Assert.DoesNotContain(HistoricalText, request.Payload);
        Assert.DoesNotContain(request.Messages, message => message.Content.Contains(HistoricalText));
        Assert.Contains("Quote the note.", request.Payload);
    }

    [Fact]
    public void ProviderPermissionCannotBeSetByJson()
    {
        var cloud = JsonSerializer.Deserialize<CloudModelMessage>("{\"Role\":\"user\",\"Content\":\"text\",\"MemoryProvider\":\"claude-sonnet\"}")!;
        var local = JsonSerializer.Deserialize<Engram.Store.Inference.ChatMessage>("{\"Role\":\"user\",\"Content\":\"text\",\"MemoryProvider\":\"claude-sonnet\"}")!;
        Assert.Null(cloud.MemoryProvider);
        Assert.Null(local.MemoryProvider);
        Assert.DoesNotContain("MemoryProvider", JsonSerializer.Serialize(new CloudModelMessage("user", "text") { MemoryProvider = "claude-sonnet" }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProviderFailureCannotEchoMemoryIntoAuditOrFailureResult(bool throws)
    {
        var provider = new RecordingProvider("claude-sonnet", OriginalScope) { EchoError = true, ThrowError = throws };
        var result = await Pipeline(provider).ExecuteAsync(Request(provider.DisclosureIdentity!));
        Assert.Equal(PipelineStatus.Failed, result.Status);
        Assert.DoesNotContain(HistoricalText, result.ErrorMessage ?? "");
        Assert.DoesNotContain(HistoricalText, File.ReadAllText(Path.Combine(_root, "logs", "cloud-audit.jsonl")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HttpProviderDoesNotLogEchoedMemoryOnFailure(bool throws)
    {
        var logger = new RecordingLogger();
        using var client = new HttpClient(new EchoErrorHandler(throws));
        var provider = new OpenAICompatibleProvider("key", "https://model.test/v1", "test-model",
            "claude-sonnet", logger, client, maxTransientRetries: 0);
        var result = await Pipeline(provider).ExecuteAsync(Request(provider.DisclosureIdentity!));
        Assert.Equal(PipelineStatus.Failed, result.Status);
        Assert.NotEmpty(logger.Messages);
        Assert.DoesNotContain(logger.Messages, message => message.Contains(HistoricalText));
    }

    private sealed class EchoErrorHandler(bool throws) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Contains(HistoricalText, await request.Content!.ReadAsStringAsync(cancellationToken));
            if (throws) throw new HttpRequestException(HistoricalText);
            return new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest)
                { Content = new StringContent(HistoricalText) };
        }
    }

    private sealed class RecordingLogger : ILogger<OpenAICompatibleProvider>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception) + exception);
    }

    [Fact]
    public async Task SameFriendlyNameWithDifferentDestinationCannotReceiveOriginalMemory()
    {
        var original = new RecordingProvider("claude-sonnet", OriginalScope);
        await Pipeline(original).ExecuteAsync(Request());
        var replacement = new RecordingProvider("claude-sonnet", OtherScope);
        var result = await Pipeline(replacement).ExecuteAsync(Request());
        Assert.Equal(OtherScope, result.DisclosureIdentity);
        var request = Assert.Single(replacement.Requests);
        Assert.DoesNotContain(HistoricalText, request.Payload);
        Assert.DoesNotContain(request.Messages, message => message.Content.Contains(HistoricalText));
    }

    [Theory]
    [InlineData("claude-sonnet", null, OriginalScope)]
    [InlineData("local", "local", "local")]
    [InlineData("local", OtherScope, "local")]
    public async Task MissingIdentityAndLocalSentinelCannotAuthorizeCloudMemory(string name, string? identity, string memoryIdentity)
    {
        var provider = new RecordingProvider(name, identity);
        var result = await Pipeline(provider).ExecuteAsync(Request(memoryIdentity));
        Assert.Equal(PipelineStatus.Completed, result.Status);
        var request = Assert.Single(provider.Requests);
        Assert.DoesNotContain(HistoricalText, request.Payload);
        Assert.DoesNotContain(request.Messages, message => message.Content.Contains(HistoricalText));
    }

    [Fact]
    public async Task CacheCannotCrossDestinationOrAccountIdentity()
    {
        var request = new CloudCallRequest { Payload = "Explain this public topic.", Complexity = TaskComplexity.High };
        var original = new RecordingProvider("claude-sonnet", OriginalScope);
        Assert.False((await Pipeline(original).ExecuteAsync(request)).FromCache);
        var replacement = new RecordingProvider("claude-sonnet", OtherScope);
        var result = await Pipeline(replacement).ExecuteAsync(request);
        Assert.False(result.FromCache);
        Assert.Single(replacement.Requests);
        Assert.Equal(OtherScope, result.DisclosureIdentity);
        var cached = await Pipeline(replacement).ExecuteAsync(request);
        Assert.True(cached.FromCache);
        Assert.Equal(OtherScope, cached.DisclosureIdentity);
        Assert.Single(replacement.Requests);
    }

    [Fact]
    public void DisclosureIdentityIsOpaqueStableAndChangesWithEndpointAccountOrProvider()
    {
        var original = new OpenAICompatibleProvider("synthetic-account-one", "https://model.test/v1/", "first-model", "custom");
        var same = new OpenAICompatibleProvider("synthetic-account-one", "https://model.test/v1", "other-model", "custom");
        var endpoint = new OpenAICompatibleProvider("synthetic-account-one", "https://other.test/v1", "first-model", "custom");
        var account = new OpenAICompatibleProvider("synthetic-account-two", "https://model.test/v1", "first-model", "custom");
        var provider = new OpenAICompatibleProvider("synthetic-account-one", "https://model.test/v1", "first-model", "different");
        try
        {
            Assert.Equal(original.DisclosureIdentity, same.DisclosureIdentity);
            Assert.NotEqual(original.DisclosureIdentity, endpoint.DisclosureIdentity);
            Assert.NotEqual(original.DisclosureIdentity, account.DisclosureIdentity);
            Assert.NotEqual(original.DisclosureIdentity, provider.DisclosureIdentity);
            Assert.Matches("^cloud-v1-[A-F0-9]{64}$", original.DisclosureIdentity!);
            Assert.DoesNotContain("model.test", original.DisclosureIdentity);
            Assert.DoesNotContain("synthetic-account-one", original.DisclosureIdentity);
        }
        finally
        {
            original.Dispose(); same.Dispose(); endpoint.Dispose(); account.Dispose(); provider.Dispose();
        }
    }

    private sealed class RecordingProvider(string name, string? identity = null) : ICloudModelProvider
    {
        public bool IsAvailable { get; set; } = true;
        public string ProviderName => name;
        public string ModelName => "recording-model";
        public string? DisclosureIdentity => identity;
        public List<CloudModelRequest> Requests { get; } = [];
        public bool EchoError { get; init; }
        public bool ThrowError { get; init; }
        public Task<CloudModelResponse> SendAsync(CloudModelRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (ThrowError) throw new IOException(HistoricalText);
            if (EchoError) return Task.FromResult(new CloudModelResponse { Success = false, ErrorMessage = HistoricalText });
            return Task.FromResult(new CloudModelResponse
            {
                Success = true, Provider = ProviderName, Model = ModelName, Content = "A reply using the available context."
            });
        }
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
