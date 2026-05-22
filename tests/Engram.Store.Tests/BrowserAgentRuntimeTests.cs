using System;
using System.Threading;
using System.Threading.Tasks;
using Engram.Store.Automation;
using Xunit;

namespace Engram.Store.Tests;

public class BrowserAgentRuntimeTests
{
    [Fact]
    public void Constructor_StartsInSimulationMode()
    {
        using var runtime = new BrowserAgentRuntime();
        Assert.True(runtime.IsSimulationMode);
    }

    [Fact]
    public async Task NavigateAsync_UpdatesUrlAndHtml_InSimulation()
    {
        await using var runtime = new BrowserAgentRuntime();
        runtime.IsSimulationMode = true;

        var driver = (StubBrowserDriver)await runtime.GetDriverAsync();
        driver.MockPages["https://example.com/test"] = "<html><body><h1 id='header'>Hello World</h1></body></html>";

        await runtime.NavigateAsync("https://example.com/test");

        Assert.Equal("https://example.com/test", await runtime.GetUrlAsync());
        var content = await runtime.GetTextContentAsync("#header");
        Assert.Equal("Hello World", content);
    }

    [Fact]
    public async Task ClickAsync_ThrowsException_IfElementDoesNotExist()
    {
        await using var runtime = new BrowserAgentRuntime();
        runtime.IsSimulationMode = true;

        await runtime.NavigateAsync("https://example.com");

        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.ClickAsync("#nonexistent-button"));
    }

    [Fact]
    public async Task ClickAsync_Succeeds_IfElementExists()
    {
        await using var runtime = new BrowserAgentRuntime();
        runtime.IsSimulationMode = true;

        var driver = (StubBrowserDriver)await runtime.GetDriverAsync();
        driver.CurrentHtml = "<html><body><button id='my-btn'>Click Me</button></body></html>";

        var exception = await Record.ExceptionAsync(() => runtime.ClickAsync("#my-btn"));
        Assert.Null(exception);
        Assert.Contains("#my-btn", driver.ClickedSelectors);
    }

    [Fact]
    public async Task TypeAsync_ThrowsException_IfElementDoesNotExist()
    {
        await using var runtime = new BrowserAgentRuntime();
        runtime.IsSimulationMode = true;

        await runtime.NavigateAsync("https://example.com");

        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.TypeAsync("#nonexistent-input", "Some text"));
    }

    [Fact]
    public async Task TypeAsync_UpdatesValue_IfElementExists()
    {
        await using var runtime = new BrowserAgentRuntime();
        runtime.IsSimulationMode = true;

        var driver = (StubBrowserDriver)await runtime.GetDriverAsync();
        driver.CurrentHtml = "<html><body><input class='search-box' /></body></html>";

        await runtime.TypeAsync(".search-box", "Engram Agent");

        var text = await runtime.GetTextContentAsync(".search-box");
        Assert.Equal("Engram Agent", text);
    }

    [Fact]
    public async Task TakeScreenshotAsync_ReturnsValidHeader_InSimulation()
    {
        await using var runtime = new BrowserAgentRuntime();
        runtime.IsSimulationMode = true;

        var bytes = await runtime.TakeScreenshotAsync();
        Assert.NotNull(bytes);
        Assert.True(bytes.Length >= 8);
        // Verify PNG header: 137 80 78 71 13 10 26 10
        Assert.Equal(137, bytes[0]);
        Assert.Equal(80, bytes[1]);
        Assert.Equal(78, bytes[2]);
        Assert.Equal(71, bytes[3]);
    }

    [Fact]
    public async Task TogglingSimulationMode_RecreatesDriver()
    {
        await using var runtime = new BrowserAgentRuntime();
        runtime.IsSimulationMode = true;

        var stubDriver1 = await runtime.GetDriverAsync();
        Assert.IsType<StubBrowserDriver>(stubDriver1);

        // Toggle to simulation mode false (Playwright)
        runtime.IsSimulationMode = false;
        var playwrightDriver = await runtime.GetDriverAsync();
        Assert.IsType<PlaywrightBrowserDriver>(playwrightDriver);

        // Toggle back to simulation mode true
        runtime.IsSimulationMode = true;
        var stubDriver2 = await runtime.GetDriverAsync();
        Assert.IsType<StubBrowserDriver>(stubDriver2);
        Assert.NotSame(stubDriver1, stubDriver2);
    }

    [Fact]
    public async Task DisposeAsync_DisposesActiveDriver()
    {
        BrowserAgentRuntime runtime;
        StubBrowserDriver stubDriver;

        await using (runtime = new BrowserAgentRuntime())
        {
            stubDriver = (StubBrowserDriver)await runtime.GetDriverAsync();
            Assert.False(stubDriver.IsDisposed);
        }

        Assert.True(stubDriver.IsDisposed);
    }
}
