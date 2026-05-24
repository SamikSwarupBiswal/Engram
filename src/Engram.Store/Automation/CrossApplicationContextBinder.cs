using System;
using System.Collections.Generic;

namespace Engram.Store.Automation;

public class CrossApplicationContextBinder
{
    private readonly Dictionary<string, object> _boundContext = new(StringComparer.OrdinalIgnoreCase);

    public void Bind(string key, object value)
    {
        if (string.IsNullOrEmpty(key)) return;
        _boundContext[key] = value;
    }

    public T? GetBoundValue<T>(string key)
    {
        if (_boundContext.TryGetValue(key, out var val) && val is T typedVal)
        {
            return typedVal;
        }
        return default;
    }

    /// <summary>
    /// Bridges browser scraped data (like HTML or text tables) into document context variables.
    /// </summary>
    public void BridgeBrowserToDocument(string scrapedBrowserData, ExecutionContext docContext)
    {
        if (docContext == null) return;

        // Simulate extracting tabular/structured data from browser and mapping to document context
        docContext.SetVariable("extracted_browser_data", scrapedBrowserData);

        // Simple mock extraction: split lines
        var lines = scrapedBrowserData.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length > 0)
        {
            docContext.SetVariable("document_title", lines[0]);
        }
        if (lines.Length > 1)
        {
            docContext.SetVariable("document_content", string.Join(" ", lines[1..]));
        }
    }

    /// <summary>
    /// Bridges filesystem path outputs into communication context variables (like drafts).
    /// </summary>
    public void BridgeFilesystemToCommunication(string filepath, ExecutionContext commContext)
    {
        if (commContext == null) return;

        commContext.SetVariable("attachment_path", filepath);
        commContext.SetVariable("email_subject", $"Draft: Report for {System.IO.Path.GetFileNameWithoutExtension(filepath)}");
        commContext.SetVariable("email_body", $"Please find the generated report attached: {filepath}");
    }
}
