using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Engram.Store.Automation;

/// <summary>
/// A high-fidelity simulated browser driver for fast testing and CI pipelines.
/// </summary>
public class StubBrowserDriver : IBrowserDriver
{
    public string CurrentUrl { get; set; } = "about:blank";
    public string CurrentHtml { get; set; } = "<html><body></body></html>";
    public Dictionary<string, string> MockPages { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> InputValues { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> ClickedSelectors { get; } = new();
    public bool IsDisposed { get; private set; }

    public Task NavigateAsync(string url, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (IsDisposed) throw new ObjectDisposedException(nameof(StubBrowserDriver));

        CurrentUrl = url;
        if (MockPages.TryGetValue(url, out var html))
        {
            CurrentHtml = html;
        }
        else
        {
            CurrentHtml = $"<html><body><h1>Stub Page</h1><p>Welcome to {url}</p></body></html>";
        }
        return Task.CompletedTask;
    }

    public Task ClickAsync(string selector, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (IsDisposed) throw new ObjectDisposedException(nameof(StubBrowserDriver));

        if (!ElementExists(selector))
        {
            throw new InvalidOperationException($"Element not found: {selector}");
        }

        ClickedSelectors.Add(selector);
        return Task.CompletedTask;
    }

    public Task TypeAsync(string selector, string text, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (IsDisposed) throw new ObjectDisposedException(nameof(StubBrowserDriver));

        if (!ElementExists(selector))
        {
            throw new InvalidOperationException($"Element not found: {selector}");
        }

        InputValues[selector] = text;
        return Task.CompletedTask;
    }

    public Task<string> GetTextContentAsync(string selector, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (IsDisposed) throw new ObjectDisposedException(nameof(StubBrowserDriver));

        if (!ElementExists(selector))
        {
            throw new InvalidOperationException($"Element not found: {selector}");
        }

        if (InputValues.TryGetValue(selector, out var value))
        {
            return Task.FromResult(value);
        }

        // Try parsing inner text from CurrentHtml using a simple regex
        // e.g. for selector "#title" matching <h1 id="title">Hello</h1>
        string elementText = ExtractTextBySelector(selector);
        return Task.FromResult(elementText);
    }

    public Task<byte[]> TakeScreenshotAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (IsDisposed) throw new ObjectDisposedException(nameof(StubBrowserDriver));

        // Returns a small valid mock PNG header
        return Task.FromResult(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82 });
    }

    public Task<string> GetUrlAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (IsDisposed) throw new ObjectDisposedException(nameof(StubBrowserDriver));

        return Task.FromResult(CurrentUrl);
    }

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }

    private bool ElementExists(string selector)
    {
        if (string.IsNullOrWhiteSpace(selector)) return false;
        if (CurrentHtml.Contains(selector)) return true;

        if (selector.StartsWith("#"))
        {
            var id = selector[1..];
            return CurrentHtml.Contains($"id=\"{id}\"") || CurrentHtml.Contains($"id='{id}'");
        }

        if (selector.StartsWith("."))
        {
            var cls = selector[1..];
            return CurrentHtml.Contains($"class=\"{cls}\"") || CurrentHtml.Contains($"class='{cls}'");
        }

        return false;
    }

    private string ExtractTextBySelector(string selector)
    {
        if (selector.StartsWith("#"))
        {
            var id = selector[1..];
            // Match tag containing id="id" or id='id' and grab its inner text
            var regex = new Regex($@"<[^>]*id=[""']{Regex.Escape(id)}[""'][^>]*>(?<text>.*?)</[^>]+>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var match = regex.Match(CurrentHtml);
            if (match.Success)
            {
                return match.Groups["text"].Value.Trim();
            }
        }
        else if (selector.StartsWith("."))
        {
            var cls = selector[1..];
            var regex = new Regex($@"<[^>]*class=[""']{Regex.Escape(cls)}[""'][^>]*>(?<text>.*?)</[^>]+>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var match = regex.Match(CurrentHtml);
            if (match.Success)
            {
                return match.Groups["text"].Value.Trim();
            }
        }

        // Return a default if we cannot extract
        return string.Empty;
    }
}
