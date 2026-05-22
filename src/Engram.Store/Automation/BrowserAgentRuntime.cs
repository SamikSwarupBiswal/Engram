using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Automation;

/// <summary>
/// Orchestrates web page actions, screenshot captures, and coordinate mapping using IBrowserDriver.
/// Automatically handles simulation mode vs real browser automation.
/// </summary>
public class BrowserAgentRuntime : IDisposable, IAsyncDisposable
{
    private readonly ILogger<BrowserAgentRuntime>? _logger;
    private IBrowserDriver? _driver;
    private bool _isSimulationMode = true;

    public BrowserAgentRuntime(ILogger<BrowserAgentRuntime>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Set to true to use StubBrowserDriver, or false to use PlaywrightBrowserDriver.
    /// Defaults to true for safety.
    /// </summary>
    public bool IsSimulationMode
    {
        get => _isSimulationMode;
        set
        {
            if (_isSimulationMode != value)
            {
                _isSimulationMode = value;
                // Recreate driver on demand when mode changes
                var disposeTask = DisposeDriverAsync();
                if (!disposeTask.IsCompleted)
                {
                    disposeTask.AsTask().GetAwaiter().GetResult();
                }
            }
        }
    }

    public async Task<IBrowserDriver> GetDriverAsync(CancellationToken ct = default)
    {
        if (_driver == null)
        {
            if (_isSimulationMode)
            {
                _logger?.LogInformation("Creating StubBrowserDriver for simulation.");
                _driver = new StubBrowserDriver();
            }
            else
            {
                _logger?.LogInformation("Creating PlaywrightBrowserDriver for real web automation.");
                _driver = new PlaywrightBrowserDriver();
            }
        }
        return _driver;
    }

    public async Task NavigateAsync(string url, CancellationToken ct = default)
    {
        var driver = await GetDriverAsync(ct);
        await driver.NavigateAsync(url, ct);
    }

    public async Task ClickAsync(string selector, CancellationToken ct = default)
    {
        var driver = await GetDriverAsync(ct);
        await driver.ClickAsync(selector, ct);
    }

    public async Task TypeAsync(string selector, string text, CancellationToken ct = default)
    {
        var driver = await GetDriverAsync(ct);
        await driver.TypeAsync(selector, text, ct);
    }

    public async Task<string> GetTextContentAsync(string selector, CancellationToken ct = default)
    {
        var driver = await GetDriverAsync(ct);
        return await driver.GetTextContentAsync(selector, ct);
    }

    public async Task<byte[]> TakeScreenshotAsync(CancellationToken ct = default)
    {
        var driver = await GetDriverAsync(ct);
        return await driver.TakeScreenshotAsync(ct);
    }

    public async Task<string> GetUrlAsync(CancellationToken ct = default)
    {
        var driver = await GetDriverAsync(ct);
        return await driver.GetUrlAsync(ct);
    }

    public void Dispose()
    {
        var task = DisposeDriverAsync();
        if (!task.IsCompleted)
        {
            task.AsTask().GetAwaiter().GetResult();
        }
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeDriverAsync();
        GC.SuppressFinalize(this);
    }

    private async ValueTask DisposeDriverAsync()
    {
        if (_driver != null)
        {
            await _driver.DisposeAsync();
            _driver = null;
        }
    }
}
