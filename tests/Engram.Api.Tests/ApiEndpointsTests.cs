using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Engram.Api.Tests;

/// <summary>
/// Integration tests for all Engram API endpoints.
/// Uses WebApplicationFactory to spin up the API in-memory.
/// </summary>
public class ApiEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ApiEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    // ─── Health ───

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await _client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Engram API", json.GetProperty("service").GetString());
        Assert.Equal("1.0.0", json.GetProperty("version").GetString());
        Assert.Equal("running", json.GetProperty("status").GetString());
    }

    // ─── Search ───

    [Fact]
    public async Task Search_WithoutQuery_ReturnsBadRequest()
    {
        var response = await _client.GetAsync("/api/search");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_WithQuery_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/search?q=test");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("test", json.GetProperty("query").GetString());
        Assert.True(json.GetProperty("nodesSearched").GetInt32() >= 0);
        Assert.True(json.GetProperty("duration").GetDouble() >= 0);
        Assert.True(json.GetProperty("results").GetArrayLength() >= 0);
    }

    // ─── Wiki ───

    [Fact]
    public async Task Wiki_ReturnsOkWithCount()
    {
        var response = await _client.GetAsync("/api/wiki");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("count").GetInt32() >= 0);
        Assert.True(json.GetProperty("nodes").GetArrayLength() >= 0);
    }

    [Fact]
    public async Task Wiki_UnknownNode_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/wiki/nonexistent_node_12345");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ─── Briefs ───

    [Fact]
    public async Task Brief_Morning_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/brief?time=morning");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("morning", json.GetProperty("type").GetString());
        Assert.False(string.IsNullOrEmpty(json.GetProperty("content").GetString()));
    }

    [Fact]
    public async Task Brief_Evening_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/brief?time=evening");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("evening", json.GetProperty("type").GetString());
    }

    // ─── Events ───

    [Fact]
    public async Task Events_ReturnsOkWithCount()
    {
        var response = await _client.GetAsync("/api/events");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("count").GetInt32() >= 0);
        Assert.True(json.GetProperty("events").GetArrayLength() >= 0);
    }

    [Fact]
    public async Task Events_WithPagination_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/events?offset=0&limit=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ─── Status ───

    [Fact]
    public async Task Status_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Free", json.GetProperty("tier").GetString());
        Assert.False(json.GetProperty("cloudEnabled").GetBoolean());
        Assert.True(json.GetProperty("rawEvents").GetInt32() >= 0);
        Assert.True(json.GetProperty("wikiNodes").GetInt32() >= 0);
    }

    // ─── Identity ───

    [Fact]
    public async Task Identity_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/identity");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        // Identity may or may not be discovered
        Assert.True(json.TryGetProperty("discovered", out _));
    }

    // ─── Drift ───

    [Fact]
    public async Task Drift_ReturnsOkWithCount()
    {
        var response = await _client.GetAsync("/api/drift");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("count").GetInt32() >= 0);
    }

    // ─── Chat Completions ───

    [Fact]
    public async Task ChatCompletions_ReturnsOkWithMockResponse()
    {
        var request = new
        {
            messages = new[]
            {
                new { role = "user", content = "Hello Engram" }
            }
        };

        var response = await _client.PostAsJsonAsync("/v1/chat/completions", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("chat.completion", json.GetProperty("object").GetString());
        Assert.True(json.GetProperty("choices").GetArrayLength() > 0);

        var content = json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        Assert.Contains("Hello Engram", content!);
    }

    // ─── CopilotKit ───

    [Fact]
    public async Task CopilotKit_ReturnsSSE()
    {
        var response = await _client.PostAsync("/v1/copilotkit", new StringContent("{}"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("data:", body);
        Assert.Contains("[DONE]", body);
    }

    // ─── CORS ───

    [Fact]
    public async Task Search_SupportsCORS()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/search");
        request.Headers.Add("Origin", "http://localhost:1420");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await _client.SendAsync(request);
        // CORS preflight should succeed
        Assert.True(
            response.StatusCode == HttpStatusCode.OK ||
            response.StatusCode == HttpStatusCode.NoContent
        );
    }
}
