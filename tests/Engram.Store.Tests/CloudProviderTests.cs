using Engram.Store.Cloud;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for cloud model providers (Claude, Gemini, OpenAI-compatible).
/// Validates availability, metadata, error handling, and disposal.
/// </summary>
public class CloudProviderTests : IDisposable
{
    public void Dispose() { }

    // ─── ClaudeSonnetProvider ───

    [Fact]
    public void Claude_WithApiKey_IsAvailable()
    {
        var provider = new ClaudeSonnetProvider("test-key");
        Assert.True(provider.IsAvailable);
    }

    [Fact]
    public void Claude_WithoutApiKey_IsNotAvailable()
    {
        var provider = new ClaudeSonnetProvider();
        Assert.False(provider.IsAvailable);
    }

    [Fact]
    public void Claude_WithEmptyApiKey_IsNotAvailable()
    {
        var provider = new ClaudeSonnetProvider("");
        Assert.False(provider.IsAvailable);
    }

    [Fact]
    public void Claude_ProviderName_IsCorrect()
    {
        var provider = new ClaudeSonnetProvider();
        Assert.Equal("claude-sonnet", provider.ProviderName);
    }

    [Fact]
    public void Claude_ModelName_IsCorrect()
    {
        var provider = new ClaudeSonnetProvider();
        Assert.Equal("claude-4.5-sonnet", provider.ModelName);
    }

    [Fact]
    public async Task Claude_NotAvailable_ReturnsError()
    {
        var provider = new ClaudeSonnetProvider();
        var request = new CloudModelRequest { Payload = "test" };
        var response = await provider.SendAsync(request);

        Assert.False(response.Success);
        Assert.Contains("not configured", response.ErrorMessage);
    }

    [Fact]
    public async Task Claude_Available_ReturnsResponse()
    {
        var provider = new ClaudeSonnetProvider("test-key");
        var request = new CloudModelRequest { Payload = "test", MaxTokens = 100 };
        var response = await provider.SendAsync(request);

        Assert.True(response.Success);
        Assert.NotEmpty(response.Content);
        Assert.Equal("claude-sonnet", response.Provider);
        Assert.Equal("claude-4.5-sonnet", response.Model);
    }

    [Fact]
    public async Task Claude_Available_TracksTokenUsage()
    {
        var provider = new ClaudeSonnetProvider("test-key");
        var request = new CloudModelRequest { Payload = "test", MaxTokens = 100 };
        var response = await provider.SendAsync(request);

        Assert.True(response.InputTokens > 0);
        Assert.True(response.OutputTokens > 0);
    }

    // ─── GeminiFlashProvider ───

    [Fact]
    public void Gemini_WithApiKey_IsAvailable()
    {
        var provider = new GeminiFlashProvider("test-key");
        Assert.True(provider.IsAvailable);
    }

    [Fact]
    public void Gemini_WithoutApiKey_IsNotAvailable()
    {
        var provider = new GeminiFlashProvider();
        Assert.False(provider.IsAvailable);
    }

    [Fact]
    public void Gemini_WithEmptyApiKey_IsNotAvailable()
    {
        var provider = new GeminiFlashProvider("");
        Assert.False(provider.IsAvailable);
    }

    [Fact]
    public void Gemini_ProviderName_IsCorrect()
    {
        var provider = new GeminiFlashProvider();
        Assert.Equal("gemini-flash", provider.ProviderName);
    }

    [Fact]
    public void Gemini_ModelName_IsCorrect()
    {
        var provider = new GeminiFlashProvider();
        Assert.Equal("gemini-3-flash", provider.ModelName);
    }

    [Fact]
    public async Task Gemini_NotAvailable_ReturnsError()
    {
        var provider = new GeminiFlashProvider();
        var request = new CloudModelRequest { Payload = "test" };
        var response = await provider.SendAsync(request);

        Assert.False(response.Success);
        Assert.Contains("not configured", response.ErrorMessage);
    }

    [Fact]
    public async Task Gemini_Available_ReturnsResponse()
    {
        var provider = new GeminiFlashProvider("test-key");
        var request = new CloudModelRequest { Payload = "test", MaxTokens = 100 };
        var response = await provider.SendAsync(request);

        Assert.True(response.Success);
        Assert.NotEmpty(response.Content);
        Assert.Equal("gemini-flash", response.Provider);
        Assert.Equal("gemini-3-flash", response.Model);
    }

    [Fact]
    public async Task Gemini_Available_TracksTokenUsage()
    {
        var provider = new GeminiFlashProvider("test-key");
        var request = new CloudModelRequest { Payload = "test", MaxTokens = 100 };
        var response = await provider.SendAsync(request);

        Assert.True(response.InputTokens > 0);
        Assert.True(response.OutputTokens > 0);
    }

    // ─── OpenAICompatibleProvider ───

    [Fact]
    public void OpenAI_WithApiKey_IsAvailable()
    {
        var provider = new OpenAICompatibleProvider("test-key", "https://api.openai.com/v1", "gpt-4o");
        Assert.True(provider.IsAvailable);
    }

    [Fact]
    public void OpenAI_WithLocalhost_IsAvailable()
    {
        var provider = new OpenAICompatibleProvider("", "http://localhost:11434/v1", "llama3");
        Assert.True(provider.IsAvailable);
    }

    [Fact]
    public void OpenAI_WithoutApiKeyOrLocalhost_IsNotAvailable()
    {
        var provider = new OpenAICompatibleProvider("", "https://api.openai.com/v1", "gpt-4o");
        Assert.False(provider.IsAvailable);
    }

    [Fact]
    public void OpenAI_ProviderName_IsCustom()
    {
        var provider = new OpenAICompatibleProvider("key", "https://api.groq.com/v1", "llama3");
        Assert.NotEmpty(provider.ProviderName);
    }

    [Fact]
    public void OpenAI_ModelName_IsPreserved()
    {
        var provider = new OpenAICompatibleProvider("key", "https://api.openai.com/v1", "gpt-4o-mini");
        Assert.Equal("gpt-4o-mini", provider.ModelName);
    }

    [Fact]
    public void OpenAI_Dispose_DoesNotThrow()
    {
        var provider = new OpenAICompatibleProvider("key", "https://api.openai.com/v1", "gpt-4o");
        provider.Dispose();
    }

    [Fact]
    public void OpenAI_DoubleDispose_DoesNotThrow()
    {
        var provider = new OpenAICompatibleProvider("key", "https://api.openai.com/v1", "gpt-4o");
        provider.Dispose();
        provider.Dispose();
    }
}
