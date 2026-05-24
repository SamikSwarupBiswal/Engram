using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Engram.Store.Inference;

namespace Engram.Store.Automation;

/// <summary>
/// Orchestrates web page actions, screenshot captures, and coordinate mapping using IBrowserDriver.
/// Automatically handles simulation mode vs real browser automation.
/// Falls back to WebView2DriverFallback when PlaywrightBrowserDriver encounters failures.
/// </summary>
public class BrowserAgentRuntime : IDisposable, IAsyncDisposable
{
    private readonly ILogger<BrowserAgentRuntime>? _logger;
    private readonly WorkspacePaths? _paths;
    private IBrowserDriver? _driver;
    private bool _isSimulationMode = true;
    private readonly object _pathologyLock = new();

    public BrowserAgentRuntime(ILogger<BrowserAgentRuntime>? logger = null, WorkspacePaths? paths = null)
    {
        _logger = logger;
        _paths = paths;
    }

    /// <summary>
    /// For testing fallback behaviors: simulates Playwright initialization or command failure.
    /// </summary>
    public bool SimulatePlaywrightFailure { get; set; }

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
        if (_isSimulationMode)
        {
            if (_driver == null || _driver is not StubBrowserDriver)
            {
                await DisposeDriverAsync();
                _logger?.LogInformation("Creating StubBrowserDriver for simulation.");
                _driver = new StubBrowserDriver();
            }
            return _driver;
        }

        // If not in simulation mode, check if WebView2FallbackActive is currently degraded
        bool isDegraded = DegradationTracker.Instance.IsDegraded("WebView2FallbackActive");
        
        if (isDegraded)
        {
            if (_driver == null || _driver is not WebView2DriverFallback)
            {
                await DisposeDriverAsync();
                _logger?.LogWarning("Playwright is degraded. Creating WebView2DriverFallback.");
                _driver = new WebView2DriverFallback();
            }
            return _driver;
        }

        if (SimulatePlaywrightFailure)
        {
            _logger?.LogWarning("Simulating Playwright driver failure.");
            RecordFailure(new InvalidOperationException("Simulated Playwright driver failure."));
            _driver = new WebView2DriverFallback();
            return _driver;
        }

        // Playwright is not degraded. Ensure we have PlaywrightBrowserDriver
        if (_driver == null || _driver is not PlaywrightBrowserDriver)
        {
            await DisposeDriverAsync();
            _logger?.LogInformation("Creating PlaywrightBrowserDriver for real web automation.");
            
            try
            {
                _driver = new PlaywrightBrowserDriver();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to instantiate PlaywrightBrowserDriver. Falling back immediately to WebView2.");
                RecordFailure(ex);
                _driver = new WebView2DriverFallback();
            }
        }

        return _driver;
    }

    public async Task NavigateAsync(string url, CancellationToken ct = default)
    {
        await ExecuteWithFallbackAsync(async d => 
        {
            if (SimulatePlaywrightFailure && d is PlaywrightBrowserDriver)
                throw new InvalidOperationException("Simulated Playwright failure during NavigateAsync.");
            await d.NavigateAsync(url, ct);
        }, nameof(NavigateAsync), ct);
    }

    public async Task ClickAsync(string selector, CancellationToken ct = default)
    {
        await ExecuteWithFallbackAsync(async d => 
        {
            if (SimulatePlaywrightFailure && d is PlaywrightBrowserDriver)
                throw new InvalidOperationException("Simulated Playwright failure during ClickAsync.");
            await d.ClickAsync(selector, ct);
        }, nameof(ClickAsync), ct);
    }

    public async Task TypeAsync(string selector, string text, CancellationToken ct = default)
    {
        await ExecuteWithFallbackAsync(async d => 
        {
            if (SimulatePlaywrightFailure && d is PlaywrightBrowserDriver)
                throw new InvalidOperationException("Simulated Playwright failure during TypeAsync.");
            await d.TypeAsync(selector, text, ct);
        }, nameof(TypeAsync), ct);
    }

    public async Task<string> GetTextContentAsync(string selector, CancellationToken ct = default)
    {
        return await ExecuteWithFallbackAsync(async d => 
        {
            if (SimulatePlaywrightFailure && d is PlaywrightBrowserDriver)
                throw new InvalidOperationException("Simulated Playwright failure during GetTextContentAsync.");
            return await d.GetTextContentAsync(selector, ct);
        }, nameof(GetTextContentAsync), ct);
    }

    public async Task<byte[]> TakeScreenshotAsync(CancellationToken ct = default)
    {
        return await ExecuteWithFallbackAsync(async d => 
        {
            if (SimulatePlaywrightFailure && d is PlaywrightBrowserDriver)
                throw new InvalidOperationException("Simulated Playwright failure during TakeScreenshotAsync.");
            return await d.TakeScreenshotAsync(ct);
        }, nameof(TakeScreenshotAsync), ct);
    }

    public async Task<string> GetUrlAsync(CancellationToken ct = default)
    {
        return await ExecuteWithFallbackAsync(async d => 
        {
            if (SimulatePlaywrightFailure && d is PlaywrightBrowserDriver)
                throw new InvalidOperationException("Simulated Playwright failure during GetUrlAsync.");
            return await d.GetUrlAsync(ct);
        }, nameof(GetUrlAsync), ct);
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

    private async Task ExecuteWithFallbackAsync(Func<IBrowserDriver, Task> action, string operationName, CancellationToken ct)
    {
        var driver = await GetDriverAsync(ct);
        try
        {
            await action(driver);
        }
        catch (Exception ex) when (!_isSimulationMode && driver is PlaywrightBrowserDriver)
        {
            _logger?.LogWarning(ex, "Playwright driver operation {Operation} failed. Triggering WebView2 fallback.", operationName);
            RecordFailure(ex);
            
            var fallbackDriver = await GetDriverAsync(ct);
            _logger?.LogInformation("Retrying operation {Operation} with WebView2 fallback driver.", operationName);
            await action(fallbackDriver);
        }
    }

    private async Task<T> ExecuteWithFallbackAsync<T>(Func<IBrowserDriver, Task<T>> action, string operationName, CancellationToken ct)
    {
        var driver = await GetDriverAsync(ct);
        try
        {
            return await action(driver);
        }
        catch (Exception ex) when (!_isSimulationMode && driver is PlaywrightBrowserDriver)
        {
            _logger?.LogWarning(ex, "Playwright driver operation {Operation} failed. Triggering WebView2 fallback.", operationName);
            RecordFailure(ex);
            
            var fallbackDriver = await GetDriverAsync(ct);
            _logger?.LogInformation("Retrying operation {Operation} with WebView2 fallback driver.", operationName);
            return await action(fallbackDriver);
        }
    }

    private void RecordFailure(Exception ex)
    {
        lock (_pathologyLock)
        {
            try
            {
                var filePath = GetPathologyMemoryPath();
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                PathologyMemory data;
                if (File.Exists(filePath))
                {
                    try
                    {
                        var content = File.ReadAllText(filePath);
                        data = JsonSerializer.Deserialize<PathologyMemory>(content) ?? new PathologyMemory();
                    }
                    catch
                    {
                        data = new PathologyMemory();
                    }
                }
                else
                {
                    data = new PathologyMemory();
                }

                data.LastFailureTime = DateTime.UtcNow;
                data.FailureCount++;
                data.LastErrorMessage = ex.Message;

                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, json);
            }
            catch (Exception writeEx)
            {
                _logger?.LogError(writeEx, "Failed to write pathology memory.");
            }
        }

        DegradationTracker.Instance.SetDegradation("WebView2FallbackActive", true, ex.Message);
    }

    private string GetPathologyMemoryPath()
    {
        string rootDir;
        if (_paths != null)
        {
            rootDir = _paths.Root;
        }
        else
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            rootDir = Path.Combine(appData, "Engram");
        }
        var diagnosticsDir = Path.Combine(rootDir, "diagnostics");
        return Path.Combine(diagnosticsDir, "pathology_memory.json");
    }

    public DateTime GetLastFailureTime()
    {
        lock (_pathologyLock)
        {
            var filePath = GetPathologyMemoryPath();
            if (File.Exists(filePath))
            {
                try
                {
                    var content = File.ReadAllText(filePath);
                    var data = JsonSerializer.Deserialize<PathologyMemory>(content);
                    if (data != null) return data.LastFailureTime;
                }
                catch { /* Ignore */ }
            }
            return DateTime.MinValue;
        }
    }
}

public class PathologyMemory
{
    public DateTime LastFailureTime { get; set; }
    public int FailureCount { get; set; }
    public string LastErrorMessage { get; set; } = string.Empty;
}

