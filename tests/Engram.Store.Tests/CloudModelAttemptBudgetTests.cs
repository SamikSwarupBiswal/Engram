using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Engram.Store.Cloud;

namespace Engram.Store.Tests;

public sealed class CloudModelAttemptBudgetTests
{
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
