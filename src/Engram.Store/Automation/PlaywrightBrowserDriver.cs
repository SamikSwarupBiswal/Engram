using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Engram.Store.Automation;

/// <summary>
/// A real Playwright-based browser driver automating Chromium.
/// </summary>
public class PlaywrightBrowserDriver : IBrowserDriver
{
    private readonly ILogger<PlaywrightBrowserDriver>? _logger;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IPage? _page;
    private bool _isDisposed;

    public PlaywrightBrowserDriver(ILogger<PlaywrightBrowserDriver>? logger = null)
    {
        _logger = logger;
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(PlaywrightBrowserDriver));
        if (_page != null) return;

        try
        {
            _logger?.LogInformation("Initializing Playwright and launching Chromium browser...");
            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true
            });
            _page = await _browser.NewPageAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize Playwright browser driver.");
            throw new InvalidOperationException("Failed to launch Playwright browser driver. Make sure Playwright browsers are installed.", ex);
        }
    }

    public async Task NavigateAsync(string url, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        _logger?.LogInformation("Navigating to URL: {Url}", url);
        await _page!.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.Load });
    }

    public async Task ClickAsync(string selector, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        _logger?.LogInformation("Clicking element: {Selector}", selector);
        await _page!.ClickAsync(selector);
    }

    public async Task TypeAsync(string selector, string text, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        _logger?.LogInformation("Typing text into element: {Selector}", selector);
        await _page!.FillAsync(selector, text);
    }

    public async Task<string> GetTextContentAsync(string selector, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var content = await _page!.TextContentAsync(selector);
        return content ?? string.Empty;
    }

    public async Task<byte[]> TakeScreenshotAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        _logger?.LogInformation("Taking screenshot...");
        return await _page!.ScreenshotAsync(new PageScreenshotOptions { Type = ScreenshotType.Png });
    }

    public async Task<string> GetUrlAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        return _page!.Url;
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _logger?.LogInformation("Disposing Playwright browser driver...");

        if (_page != null)
        {
            try { await _page.CloseAsync(); } catch { /* Ignore */ }
        }

        if (_browser != null)
        {
            try { await _browser.CloseAsync(); } catch { /* Ignore */ }
            try { await _browser.DisposeAsync(); } catch { /* Ignore */ }
        }

        _playwright?.Dispose();
    }
}
