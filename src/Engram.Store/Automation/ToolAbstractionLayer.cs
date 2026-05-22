using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Engram.Store.Automation;

public class ToolAbstractionLayer
{
    private readonly BrowserAgentRuntime _browserRuntime;
    private readonly IDesktopOperator _desktopOperator;

    public ToolAbstractionLayer(BrowserAgentRuntime browserRuntime, IDesktopOperator desktopOperator)
    {
        _browserRuntime = browserRuntime ?? throw new ArgumentNullException(nameof(browserRuntime));
        _desktopOperator = desktopOperator ?? throw new ArgumentNullException(nameof(desktopOperator));
    }

    public async Task<string> SearchWebAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(query)) throw new ArgumentException("Query cannot be empty", nameof(query));

        // Use Google as default search engine
        var searchUrl = $"https://www.google.com/search?q={Uri.EscapeDataString(query)}";
        await _browserRuntime.NavigateAsync(searchUrl, ct);

        // Retrieve stub text or try extracting elements if not in simulation
        if (_browserRuntime.IsSimulationMode)
        {
            return $"[Simulation] Search results for: '{query}'. Found 10 results. Summary: AI trends and updates.";
        }
        else
        {
            try
            {
                // Simple scrape attempt of search results
                var text = await _browserRuntime.GetTextContentAsync("#search", ct);
                return text;
            }
            catch
            {
                return $"Failed to extract real page data for search query: {query}";
            }
        }
    }

    public async Task CreateDocumentAsync(string path, string content, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path cannot be empty", nameof(path));

        if (_desktopOperator.IsSimulationMode)
        {
            // Just simulate
            return;
        }

        // Real write: make sure directories exist
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        await File.WriteAllTextAsync(path, content, ct);
    }

    public async Task<string> CompareProductsAsync(List<string> products, CancellationToken ct = default)
    {
        if (products == null || products.Count == 0) throw new ArgumentException("Products list cannot be empty");

        var pList = string.Join(", ", products);
        if (_browserRuntime.IsSimulationMode)
        {
            return $"[Simulation] Product Comparison between ({pList}): Product A is highly rated for performance, Product B for price.";
        }

        // Navigate to a comparison URL or run web searches for each product
        await _browserRuntime.NavigateAsync($"https://www.google.com/search?q=compare+{Uri.EscapeDataString(pList)}", ct);
        return $"Comparison page loaded for: {pList}";
    }

    public async Task OpenApplicationAsync(string processName, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(processName)) throw new ArgumentException("Process name cannot be empty", nameof(processName));

        if (_desktopOperator.IsSimulationMode)
        {
            return;
        }

        // In real mode, use desktop operator to launch or simulate typing the launch command
        await _desktopOperator.KeyPressAsync("LWin", ct); // Windows key
        await Task.Delay(200, ct);
        await _desktopOperator.TypeAsync(processName, ct);
        await Task.Delay(200, ct);
        await _desktopOperator.KeyPressAsync("Enter", ct);
    }

    public async Task SaveFileAsync(string path, string content, CancellationToken ct = default)
    {
        await CreateDocumentAsync(path, content, ct);
    }

    public async Task<string> ExtractPageDataAsync(string selector, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(selector)) throw new ArgumentException("Selector cannot be empty", nameof(selector));

        if (_browserRuntime.IsSimulationMode)
        {
            return $"[Simulation] Extracted stub content for selector '{selector}'";
        }

        return await _browserRuntime.GetTextContentAsync(selector, ct);
    }
}
