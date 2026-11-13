using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Engram.Store.Cloud;

namespace Engram.Store.Tests;

public sealed class CloudModelAttemptBudgetTests
{
    [Fact]
    public async Task SingleProviderAllowsAValidResponseAfterTheFormerFortyFiveSecondCutoff()
    {
        var portReservation = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        portReservation.Start();
        var port = ((IPEndPoint)portReservation.LocalEndpoint).Port;
        portReservation.Stop();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        using var caller = new CancellationTokenSource(TimeSpan.FromSeconds(75));
        var serve = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync().WaitAsync(caller.Token);
            await Task.Delay(TimeSpan.FromSeconds(46), caller.Token);
            var bytes = Encoding.UTF8.GetBytes("""{"model":"slow-valid-model","choices":[{"message":{"content":"Valid plan"},"finish_reason":"stop"}]}""");
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            try { await context.Response.OutputStream.WriteAsync(bytes, caller.Token); }
            finally { context.Response.Close(); }
        });
        var provider = new OpenAICompatibleProvider("", $"http://127.0.0.1:{port}/v1", "slow-valid-model",
            maxTransientRetries: 0);
        try
        {
            var result = await provider.SendAsync(new() { Payload = "Synthetic timing test only." }, caller.Token);
            await serve;
            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal("slow-valid-model", result.Model);
            Assert.Equal("Valid plan", result.Content);
        }
        finally { provider.Dispose(); }
    }

    [Fact]
    public async Task FallbackBudgetIncludesSlowRequestAndRetryDelay()
    {
        using var caller = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var handler = new CandidateHandler();
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(250) };
        var provider = new OpenAICompatibleProvider(
            "key", "https://model.test/v1", "stalled", httpClient: client,
            maxTransientRetries: 1, fallbackModelNames: ["busy", "available"]);

        var result = await provider.SendAsync(new CloudModelRequest { Payload = "test" }, caller.Token);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("available", result.Model);
        Assert.Equal(new[] { "stalled", "busy", "available" }, handler.Models);
    }

    [Fact]
    public async Task CallerCancellationStopsBeforeTheNextCandidate()
    {
        using var caller = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var handler = new CandidateHandler(caller.Cancel);
        using var client = new HttpClient(handler);
        var provider = new OpenAICompatibleProvider(
            "key", "https://model.test/v1", "stalled", httpClient: client,
            fallbackModelNames: ["available"]);

        var result = await provider.SendAsync(new CloudModelRequest { Payload = "test" }, caller.Token);

        Assert.False(result.Success);
        Assert.Equal("stalled", result.Model);
        Assert.Equal("stalled", Assert.Single(handler.Models));
    }

    [Fact]
    public async Task RecentSuccessfulFallbackIsTriedFirstWithoutChangingConfiguredIdentity()
    {
        var handler = new SelectionHandler((model, _, _, _) => Task.FromResult(
            model == "primary" ? Failure(HttpStatusCode.NotFound) : Success("resolved-working")));
        using var client = new HttpClient(handler);
        var provider = new OpenAICompatibleProvider("key", "https://model.test/v1", "primary",
            httpClient: client, maxTransientRetries: 0, fallbackModelNames: ["working"]);
        var identity = provider.DisclosureIdentity;

        var first = await provider.SendAsync(new() { Payload = "first" });
        var second = await provider.SendAsync(new() { Payload = "second" });

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(new[] { "primary", "working", "working" }, handler.Models);
        Assert.Equal("resolved-working", second.Model);
        Assert.Equal("primary", provider.ModelName);
        Assert.Equal(identity, provider.DisclosureIdentity);
    }

    [Fact]
    public async Task PreferredFailureReturnsToConfiguredOrderWithoutDuplicateCandidates()
    {
        var handler = new SelectionHandler((model, prompt, _, _) => Task.FromResult(prompt == "warm"
            ? model == "primary" ? Failure(HttpStatusCode.NotFound) : Success()
            : model == "backup" ? Success() : Failure(HttpStatusCode.ServiceUnavailable)));
        using var client = new HttpClient(handler);
        var provider = new OpenAICompatibleProvider("key", "https://model.test/v1", "primary",
            httpClient: client, maxTransientRetries: 0, fallbackModelNames: ["working", "backup"]);

        Assert.True((await provider.SendAsync(new() { Payload = "warm" })).Success);
        var result = await provider.SendAsync(new() { Payload = "retry" });

        Assert.True(result.Success);
        Assert.Equal("backup", result.Model);
        Assert.Equal(new[] { "primary", "working", "working", "primary", "backup" }, handler.Models);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task PreferredTerminalErrorDoesNotTryAnotherCandidate(HttpStatusCode status)
    {
        var handler = new SelectionHandler((model, prompt, _, _) => Task.FromResult(prompt == "warm"
            ? model == "primary" ? Failure(HttpStatusCode.NotFound) : Success()
            : Failure(status)));
        using var client = new HttpClient(handler);
        var provider = new OpenAICompatibleProvider("key", "https://model.test/v1", "primary",
            httpClient: client, maxTransientRetries: 0, fallbackModelNames: ["working"]);

        Assert.True((await provider.SendAsync(new() { Payload = "warm" })).Success);
        var result = await provider.SendAsync(new() { Payload = "terminal" });

        Assert.False(result.Success);
        Assert.Equal("working", result.Model);
        Assert.Equal(new[] { "primary", "working", "working" }, handler.Models);
    }

    [Fact]
    public async Task PreferredCancellationDoesNotTryAnotherCandidate()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var handler = new SelectionHandler(async (model, prompt, _, ct) =>
        {
            if (prompt == "warm") return model == "primary" ? Failure(HttpStatusCode.NotFound) : Success();
            cancellation.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Success();
        });
        using var client = new HttpClient(handler);
        var provider = new OpenAICompatibleProvider("key", "https://model.test/v1", "primary",
            httpClient: client, maxTransientRetries: 0, fallbackModelNames: ["working"]);

        Assert.True((await provider.SendAsync(new() { Payload = "warm" })).Success);
        var result = await provider.SendAsync(new() { Payload = "cancel" }, cancellation.Token);

        Assert.False(result.Success);
        Assert.Equal("working", result.Model);
        Assert.Equal(new[] { "primary", "working", "working" }, handler.Models);
    }

    [Fact]
    public async Task SuccessfulCandidateIsNotSharedBetweenProviderInstances()
    {
        var firstHandler = new SelectionHandler((model, _, _, _) => Task.FromResult(
            model == "primary" ? Failure(HttpStatusCode.NotFound) : Success()));
        var secondHandler = new SelectionHandler((_, _, _, _) => Task.FromResult(Success()));
        using var firstClient = new HttpClient(firstHandler);
        using var secondClient = new HttpClient(secondHandler);
        var first = new OpenAICompatibleProvider("first-key", "https://model.test/v1", "primary",
            httpClient: firstClient, maxTransientRetries: 0, fallbackModelNames: ["working"]);
        var second = new OpenAICompatibleProvider("second-key", "https://model.test/v1", "primary",
            httpClient: secondClient, maxTransientRetries: 0, fallbackModelNames: ["working"]);

        Assert.True((await first.SendAsync(new() { Payload = "first" })).Success);
        Assert.True((await second.SendAsync(new() { Payload = "second" })).Success);

        Assert.Equal("primary", Assert.Single(secondHandler.Models));
        Assert.NotEqual(first.DisclosureIdentity, second.DisclosureIdentity);
    }

    [Fact]
    public async Task SuccessfulCandidateExpiresAndConfiguredPriorityIsRetried()
    {
        var clock = new ManualTimeProvider();
        var handler = new SelectionHandler((model, prompt, _, _) => Task.FromResult(
            prompt == "warm" && model == "primary" ? Failure(HttpStatusCode.NotFound) : Success()));
        using var client = new HttpClient(handler);
        var provider = new OpenAICompatibleProvider("key", "https://model.test/v1", "primary",
            httpClient: client, maxTransientRetries: 0, fallbackModelNames: ["working"], timeProvider: clock);

        Assert.True((await provider.SendAsync(new() { Payload = "warm" })).Success);
        clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromTicks(1));
        Assert.True((await provider.SendAsync(new() { Payload = "recent" })).Success);
        clock.Advance(TimeSpan.FromMinutes(5));
        var result = await provider.SendAsync(new() { Payload = "expired" });

        Assert.True(result.Success);
        Assert.Equal("primary", result.Model);
        Assert.Equal(new[] { "primary", "working", "working", "primary" }, handler.Models);
    }

    [Fact]
    public async Task OlderPreferredFailureCannotClearANewerSuccessfulPreference()
    {
        var oldStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new SelectionHandler(async (model, prompt, _, ct) =>
        {
            if (prompt == "warm") return model == "primary" ? Failure(HttpStatusCode.NotFound) : Success();
            if (prompt == "older" && model == "working")
            {
                oldStarted.TrySetResult();
                await releaseOld.Task.WaitAsync(ct);
                return Failure(HttpStatusCode.ServiceUnavailable);
            }
            if (prompt == "older") return Failure(HttpStatusCode.Unauthorized);
            if (prompt == "newer") return model == "backup" ? Success() : Failure(HttpStatusCode.ServiceUnavailable);
            return Success();
        });
        using var client = new HttpClient(handler);
        var provider = new OpenAICompatibleProvider("key", "https://model.test/v1", "primary",
            httpClient: client, maxTransientRetries: 0, fallbackModelNames: ["working", "backup"]);
        Assert.True((await provider.SendAsync(new() { Payload = "warm" })).Success);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var older = provider.SendAsync(new() { Payload = "older" }, deadline.Token);
        await oldStarted.Task.WaitAsync(deadline.Token);
        try
        {
            var newer = await provider.SendAsync(new() { Payload = "newer" }, deadline.Token);
            Assert.True(newer.Success);
            Assert.Equal("backup", newer.Model);
        }
        finally { releaseOld.TrySetResult(); }
        Assert.False((await older).Success);
        var result = await provider.SendAsync(new() { Payload = "after" }, deadline.Token);

        Assert.True(result.Success);
        Assert.Equal("backup", result.Model);
        Assert.Equal("backup", handler.Models[^1]);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);
        public void Advance(TimeSpan duration) => Interlocked.Add(ref _timestamp, duration.Ticks);
    }

    private static HttpResponseMessage Success(string? actualModel = null) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            model = actualModel,
            choices = new[] { new { message = new { content = "Ready" }, finish_reason = "stop" } }
        }), Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage Failure(HttpStatusCode status) => new(status)
    {
        Content = new StringContent("Unavailable")
    };

    private sealed class SelectionHandler(
        Func<string, string, int, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        private readonly object _sync = new();
        private readonly List<string> _models = new();
        public string[] Models { get { lock (_sync) return _models.ToArray(); } }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var model = payload.RootElement.GetProperty("model").GetString()!;
            var prompt = payload.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
            int index;
            lock (_sync)
            {
                _models.Add(model);
                index = _models.Count;
            }
            return await respond(model, prompt, index, cancellationToken);
        }
    }

    private sealed class CandidateHandler(Action? onStall = null) : HttpMessageHandler
    {
        public List<string> Models { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var model = payload.RootElement.GetProperty("model").GetString()!;
            Models.Add(model);
            if (model == "stalled")
            {
                onStall?.Invoke();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            if (model == "busy")
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("temporarily unavailable"),
                    Headers = { RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30)) }
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"choices":[{"message":{"content":"Ready"},"finish_reason":"stop"}]}""",
                    Encoding.UTF8, "application/json")
            };
        }
    }
}
