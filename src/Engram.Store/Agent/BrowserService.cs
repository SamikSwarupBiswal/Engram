using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Agent;

/// <summary>
/// Browser service for web research. Uses HttpClient for content extraction.
/// Respects rate limits and timeouts.
/// </summary>
public class BrowserService : IDisposable
{
    private readonly HttpClient _http;
    private readonly ILogger<BrowserService>? _logger;
    private bool _disposed;
    private static readonly TimeSpan RateLimitDelay = TimeSpan.FromMilliseconds(500);

    public BrowserService(ILogger<BrowserService>? logger = null)
    {
        _logger = logger;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Engram/1.0 (Research Bot)");
    }

    /// <summary>Search the web via DuckDuckGo HTML (no API key).</summary>
    public async Task<List<SearchResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        try
        {
            var url = $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(query)}";
            var html = await _http.GetStringAsync(url, ct);

            var results = new List<SearchResult>();
            var pattern = new Regex(@"<a[^>]+class=""result__a""[^>]+href=""([^""]+)""[^>]*>(.*?)</a>", RegexOptions.Singleline);
            foreach (Match m in pattern.Matches(html).Cast<Match>().Take(10))
            {
                var href = m.Groups[1].Value;
                var title = Regex.Replace(m.Groups[2].Value, "<[^>]+>", "").Trim();
                if (href.Contains("uddg="))
                {
                    var uddg = Regex.Match(href, @"uddg=([^&]+)");
                    if (uddg.Success) href = Uri.UnescapeDataString(uddg.Groups[1].Value);
                }
                if (Uri.TryCreate(href, UriKind.Absolute, out _))
                    results.Add(new SearchResult { Url = href, Title = title });
            }

            _logger?.LogInformation("Search '{Query}': {Count} results", query, results.Count);
            return results;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Search failed for '{Query}'", query);
            return new List<SearchResult>();
        }
    }

    /// <summary>Extract readable content from a URL.</summary>
    public async Task<ExtractedContent> ExtractContentAsync(string url, CancellationToken ct = default)
    {
        try
        {
            await Task.Delay(RateLimitDelay, ct);
            var html = await _http.GetStringAsync(url, ct);

            var titleMatch = Regex.Match(html, @"<title[^>]*>(.*?)</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            var title = titleMatch.Success ? titleMatch.Groups[1].Value.Trim() : "";

            var cleaned = Regex.Replace(html, @"<script[^>]*>.*?</script>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"<style[^>]*>.*?</style>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"<nav[^>]*>.*?</nav>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"<footer[^>]*>.*?</footer>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);

            var paragraphs = Regex.Matches(cleaned, @"<p[^>]*>(.*?)</p>", RegexOptions.Singleline | RegexOptions.IgnoreCase)
                .Cast<Match>()
                .Select(m => Regex.Replace(m.Groups[1].Value, "<[^>]+>", "").Trim())
                .Where(p => p.Length > 30)
                .Take(20)
                .ToList();

            var text = string.Join("\n\n", paragraphs);

            var links = Regex.Matches(cleaned, @"<a[^>]+href=""([^""]+)""", RegexOptions.IgnoreCase)
                .Cast<Match>()
                .Select(m => m.Groups[1].Value)
                .Where(l => l.StartsWith("http"))
                .Distinct()
                .Take(20)
                .ToList();

            _logger?.LogInformation("Extracted {Length} chars from {Url}", text.Length, url);
            return new ExtractedContent { Url = url, Title = title, Text = text, Links = links, ExtractedAt = DateTimeOffset.UtcNow };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Extraction failed for {Url}", url);
            return new ExtractedContent { Url = url, Error = ex.Message };
        }
    }

    public void Dispose()
    {
        if (!_disposed) { _http.Dispose(); _disposed = true; }
    }
}

public class SearchResult
{
    public string Url { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Snippet { get; init; } = string.Empty;
}

public class ExtractedContent
{
    public string Url { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public List<string> Links { get; init; } = new();
    public DateTimeOffset ExtractedAt { get; init; }
    public string? Error { get; init; }
}
