using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Engram.Store.Automation;

public class FalseCompletionDetector
{
    private readonly IUiEmbodimentProvider _uiProvider;

    public FalseCompletionDetector(IUiEmbodimentProvider uiProvider)
    {
        _uiProvider = uiProvider ?? throw new ArgumentNullException(nameof(uiProvider));
    }

    public async Task<bool> DetectFalseCompletionAsync(string? resultSnippet = null, CancellationToken ct = default)
    {
        var (proc, title) = await _uiProvider.GetActiveWindowAsync(ct);

        // 1. Check window titles for common modal/error blockages
        if (Regex.IsMatch(title, @"\b(Error|Failed|Exception|Warning|Blocked|Interrupted|Save As|Open File|Confirm|Sign In|Auth|Login)\b", RegexOptions.IgnoreCase))
        {
            return true;
        }

        // 2. Check result snippet (if any) for stale states or hidden failures
        if (resultSnippet != null)
        {
            if (Regex.IsMatch(resultSnippet, @"\b(Access denied|Internal server error|Authentication failed|Operation timeout|Failed to load|Stale|Loading\.\.\.|Please wait)\b", RegexOptions.IgnoreCase))
            {
                return true;
            }
        }

        // 3. Check browser active URL (if we're in a browser, check if URL contains error patterns)
        var url = await _uiProvider.GetUrlAsync(ct);
        if (!string.IsNullOrEmpty(url))
        {
            if (url.Contains("error", StringComparison.OrdinalIgnoreCase) || 
                url.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("404", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("500", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
