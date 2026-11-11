using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Engram.Store;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Engram.Api.Tests;

[Collection("Provider environment")]
public sealed class ProviderConfigurationApiTests
{
    [Theory]
    [InlineData("https://other.provider.test/v1", "custom")]
    [InlineData("https://old.provider.test/another-account/v1", "custom")]
    [InlineData("https://old.provider.test/v1", "different-provider")]
    public async Task StartupDestinationOverrideWithoutFreshKeyDoesNotReuseStoredCredential(string endpoint, string provider)
    {
        using var host = new IsolatedHost(endpointOverride: endpoint, providerOverride: provider);
        var state = await host.Client.GetFromJsonAsync<JsonElement>("/api/provider");

        Assert.False(state.GetProperty("cloudConfigured").GetBoolean());
        Assert.False(state.GetProperty("hasApiKey").GetBoolean());
        Assert.Equal("fixture-original-key", host.LoadConfiguration().CustomProviderApiKey);
        Assert.Equal("https://old.provider.test/v1", host.LoadConfiguration().CustomProviderBaseUrl);
    }

    [Fact]
    public async Task StartupModelOverrideMayKeepSameProviderCredential()
    {
        using var host = new IsolatedHost(endpointOverride: "https://OLD.provider.test:443/v1/", providerOverride: "CUSTOM");
        var state = await host.Client.GetFromJsonAsync<JsonElement>("/api/provider");

        Assert.True(state.GetProperty("cloudConfigured").GetBoolean());
        Assert.True(state.GetProperty("hasApiKey").GetBoolean());
        Assert.Equal("environment-model", state.GetProperty("model").GetString());
    }

    [Fact]
    public async Task StartupDestinationOverrideWithFreshKeyConfiguresNewProvider()
    {
        using var host = new IsolatedHost(endpointOverride: "https://other.provider.test/v1", providerOverride: "custom",
            keyOverride: "fixture-environment-key");
        var state = await host.Client.GetFromJsonAsync<JsonElement>("/api/provider");

        Assert.True(state.GetProperty("cloudConfigured").GetBoolean());
        Assert.True(state.GetProperty("hasApiKey").GetBoolean());
        Assert.Equal("https://other.provider.test/v1", state.GetProperty("baseUrl").GetString());
        Assert.Equal("fixture-original-key", host.LoadConfiguration().CustomProviderApiKey);
    }

    [Theory]
    [InlineData("https://other.provider.test/v1", "custom")]
    [InlineData("https://old.provider.test/another-account/v1", "custom")]
    [InlineData("https://old.provider.test/v1", "different-provider")]
    public async Task ProviderChangeWithoutFreshKeyRejectsAndPreservesConfiguration(string endpoint, string provider)
    {
        using var host = new IsolatedHost();
        var before = host.ReadConfiguration();
        var response = await host.Client.PostAsJsonAsync("/api/provider", new
        { baseUrl = endpoint, providerName = provider, model = "another-model" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, host.ReadConfiguration());
        var state = await host.Client.GetFromJsonAsync<JsonElement>("/api/provider");
        Assert.Equal("https://old.provider.test/v1", state.GetProperty("baseUrl").GetString());
        Assert.Equal("original-model", state.GetProperty("model").GetString());
    }

    [Fact]
    public async Task SameAccountMayKeepKeyWhileChangingModel()
    {
        using var host = new IsolatedHost();
        var response = await host.Client.PostAsJsonAsync("/api/provider", new
        { baseUrl = "https://OLD.provider.test:443/v1/", providerName = "CUSTOM", model = "another-model" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var config = host.LoadConfiguration();
        Assert.Equal("fixture-original-key", config.CustomProviderApiKey);
        Assert.Equal("another-model", config.CustomProviderModel);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("https://user@other.provider.test/v1")]
    [InlineData("https://other.provider.test/v1?account=other")]
    public async Task InvalidDestinationDoesNotMutateStoredOrActiveConfiguration(string endpoint)
    {
        using var host = new IsolatedHost();
        var before = host.ReadConfiguration();
        var response = await host.Client.PostAsJsonAsync("/api/provider", new
        { apiKey = "fixture-replacement-key", baseUrl = endpoint, providerName = "custom", model = "another-model" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, host.ReadConfiguration());
        var state = await host.Client.GetFromJsonAsync<JsonElement>("/api/provider");
        Assert.Equal("https://old.provider.test/v1", state.GetProperty("baseUrl").GetString());
    }

    [Fact]
    public async Task InvalidCredentialFormattingDoesNotMutateConfiguration()
    {
        using var host = new IsolatedHost();
        var before = host.ReadConfiguration();
        var response = await host.Client.PostAsJsonAsync("/api/provider", new
        { apiKey = "invalid\nfixture-key", baseUrl = "https://old.provider.test/v1", providerName = "custom", model = "another-model" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, host.ReadConfiguration());
        var state = await host.Client.GetFromJsonAsync<JsonElement>("/api/provider");
        Assert.Equal("original-model", state.GetProperty("model").GetString());
    }

    [Fact]
    public async Task ProviderStateReflectsReplacementAfterGeminiStartup()
    {
        using var host = new IsolatedHost(gemini: true);
        var response = await host.Client.PostAsJsonAsync("/api/provider", new
        { apiKey = "fixture-replacement-key", baseUrl = "https://other.provider.test/v1", providerName = "custom", model = "replacement-model" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var state = await host.Client.GetFromJsonAsync<JsonElement>("/api/provider");
        Assert.Equal("custom", state.GetProperty("providerName").GetString());
        Assert.Equal("https://other.provider.test/v1", state.GetProperty("baseUrl").GetString());
        Assert.Equal("replacement-model", state.GetProperty("model").GetString());
        Assert.False(state.GetProperty("developmentMode").GetBoolean());
        Assert.True(state.GetProperty("cloudConfigured").GetBoolean());
    }

    [Fact]
    public async Task OriginGuard_RejectsUntrustedBrowserOrigin()
    {
        using var host = new IsolatedHost();
        var before = host.ReadConfiguration();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/provider")
        {
            Content = JsonContent.Create(new { apiKey = "fixture-replacement-key", baseUrl = "https://other.provider.test/v1", providerName = "custom", model = "replacement-model" })
        };
        request.Headers.Add("Origin", "https://attacker.example");
        var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, host.ReadConfiguration());
    }

    [Fact]
    public async Task Cors_RejectsHostileProviderPreflight()
    {
        using var host = new IsolatedHost();
        var before = host.ReadConfiguration();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/provider");
        request.Headers.Add("Origin", "https://attacker.example");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");
        var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Equal(before, host.ReadConfiguration());
    }

    [Theory]
    [InlineData("http://tauri.localhost")]
    [InlineData("http://localhost:1420")]
    [InlineData("http://127.0.0.1:1420")]
    public async Task Cors_AllowsTauriFrontendOrigin(string origin)
    {
        using var host = new IsolatedHost();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/provider");
        request.Headers.Add("Origin", origin);
        var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(origin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task HostGuardRejectsRebindingHostnameWithoutMutatingProvider()
    {
        using var host = new IsolatedHost();
        var before = host.ReadConfiguration();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/provider")
        {
            Content = JsonContent.Create(new { apiKey = "fixture-replacement-key", baseUrl = "https://other.provider.test/v1", providerName = "custom", model = "replacement-model" })
        };
        request.Headers.Host = "attacker.example:5000";
        var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, host.ReadConfiguration());
    }

    private sealed class IsolatedHost : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "engram-provider-api-" + Guid.NewGuid().ToString("N"));
        private readonly WebApplicationFactory<Program> _factory;
        private readonly EngramConfigStore _store;
        private readonly Dictionary<string, string?> _priorEnvironment = new();
        public HttpClient Client { get; }

        public IsolatedHost(bool gemini = false, string? endpointOverride = null, string? providerOverride = null,
            string? keyOverride = null)
        {
            // This collection excludes parallel tests while fixture-only startup
            // overrides are installed in the test process, then restores them.
            foreach (var name in new[] { "ENGRAM_DEV_GEMINI_API_KEY", "GEMINI_API_KEY", "ENGRAM_GEMINI_MODEL",
                         "ENGRAM_CLOUD_BASE_URL", "ENGRAM_CLOUD_API_KEY", "ENGRAM_CLOUD_MODEL", "ENGRAM_CLOUD_PROVIDER" })
            {
                _priorEnvironment[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, null);
            }
            if (endpointOverride != null)
            {
                Environment.SetEnvironmentVariable("ENGRAM_CLOUD_BASE_URL", endpointOverride);
                Environment.SetEnvironmentVariable("ENGRAM_CLOUD_PROVIDER", providerOverride);
                Environment.SetEnvironmentVariable("ENGRAM_CLOUD_API_KEY", keyOverride);
                Environment.SetEnvironmentVariable("ENGRAM_CLOUD_MODEL", "environment-model");
            }
            _store = new EngramConfigStore(new WorkspacePaths(_root));
            _store.Save(new EngramConfig
            {
                Tier = TierLevel.Pro,
                CloudEnabled = true,
                DevelopmentCloudEnabled = gemini,
                CustomProviderApiKey = "fixture-original-key",
                CustomProviderBaseUrl = gemini ? "https://generativelanguage.googleapis.com/v1beta/openai" : "https://old.provider.test/v1",
                CustomProviderName = gemini ? "gemini-flash" : "custom",
                CustomProviderModel = gemini ? "gemini-explicit-fixture-model" : "original-model"
            });
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                    new Dictionary<string, string?> { ["Engram:WorkspaceRoot"] = _root })));
            Client = _factory.CreateClient();
        }

        public string ReadConfiguration() => File.ReadAllText(Path.Combine(_root, "config", "engram.json"));
        public EngramConfig LoadConfiguration() => _store.Load();

        public void Dispose()
        {
            Client.Dispose();
            _factory.Dispose();
            Directory.Delete(_root, recursive: true);
            foreach (var (name, value) in _priorEnvironment)
                Environment.SetEnvironmentVariable(name, value);
        }
    }
}

[CollectionDefinition("Provider environment", DisableParallelization = true)]
public sealed class ProviderEnvironmentCollection;
