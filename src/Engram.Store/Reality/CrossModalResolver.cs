using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Engram.Store.Wiki;

namespace Engram.Store.Reality;

/// <summary>
/// Scans WikiNodes for metadata patterns (path, url, repository, window, process, alias)
/// and resolves incoming modal inputs to their canonical Node IDs.
/// </summary>
public class CrossModalResolver
{
    private readonly WikiNodeStore _nodeStore;
    
    // Cached mapping indices
    private readonly List<PathMapping> _paths = new();
    private readonly List<UrlMapping> _urls = new();
    private readonly List<WindowMapping> _windows = new();
    private readonly Dictionary<string, string> _processes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AliasMapping> _aliases = new();
    private readonly object _lock = new();

    public CrossModalResolver(WikiNodeStore nodeStore)
    {
        _nodeStore = nodeStore ?? throw new ArgumentNullException(nameof(nodeStore));
        Refresh();
    }

    /// <summary>
    /// Reloads all WikiNodes from the store and rebuilds the resolution index.
    /// </summary>
    public void Refresh()
    {
        lock (_lock)
        {
            _paths.Clear();
            _urls.Clear();
            _windows.Clear();
            _processes.Clear();
            _aliases.Clear();

            var nodes = _nodeStore.LoadAll();
            foreach (var node in nodes)
            {
                // Also index node title and ID as default aliases
                AddAlias(node.Title, node.NodeId);
                AddAlias(node.NodeId, node.NodeId);

                foreach (var fact in node.Facts)
                {
                    ParseAndIndexFact(fact.Text, node.NodeId);
                }
            }
        }
    }

    private void ParseAndIndexFact(string text, string nodeId)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        int colonIndex = text.IndexOf(':');
        if (colonIndex <= 0) return;

        string key = text[..colonIndex].Trim().ToLowerInvariant();
        string val = text[(colonIndex + 1)..].Trim();

        switch (key)
        {
            case "path":
                AddPath(val, nodeId);
                break;
            case "url":
            case "repository":
            case "repo":
                AddUrl(val, nodeId);
                break;
            case "window":
                AddWindow(val, nodeId);
                break;
            case "process":
                AddProcess(val, nodeId);
                break;
            case "alias":
                // Support comma-separated aliases
                var parts = val.Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var p in parts)
                {
                    AddAlias(p.Trim(), nodeId);
                }
                break;
        }
    }

    private void AddPath(string pattern, string nodeId)
    {
        var normalized = NormalizePath(pattern);
        if (!string.IsNullOrEmpty(normalized))
        {
            _paths.Add(new PathMapping { Pattern = normalized, NodeId = nodeId });
        }
    }

    private void AddUrl(string pattern, string nodeId)
    {
        var normalized = NormalizeUrl(pattern);
        if (!string.IsNullOrEmpty(normalized))
        {
            _urls.Add(new UrlMapping { Pattern = normalized, NodeId = nodeId });
        }
    }

    private void AddWindow(string pattern, string nodeId)
    {
        if (!string.IsNullOrEmpty(pattern))
        {
            _windows.Add(new WindowMapping { Pattern = pattern, NodeId = nodeId });
        }
    }

    private void AddProcess(string pattern, string nodeId)
    {
        var parts = pattern.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            var cleanProcess = p.Trim();
            if (!string.IsNullOrEmpty(cleanProcess))
            {
                _processes[cleanProcess] = nodeId;
            }
        }
    }

    private void AddAlias(string alias, string nodeId)
    {
        if (!string.IsNullOrEmpty(alias))
        {
            _aliases.Add(new AliasMapping { Alias = alias, NodeId = nodeId });
        }
    }

    /// <summary>
    /// Resolve a file path to a WikiNode ID.
    /// Returns the node ID of the most specific (longest) matching path pattern, or null.
    /// </summary>
    public string? ResolvePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return null;
        var normalizedInput = NormalizePath(filePath);

        lock (_lock)
        {
            // Find path patterns that are prefixes of our input, ordered by pattern length descending (longest/most specific match wins)
            var match = _paths
                .Where(p => normalizedInput.StartsWith(p.Pattern))
                .OrderByDescending(p => p.Pattern.Length)
                .FirstOrDefault();

            return match?.NodeId;
        }
    }

    /// <summary>
    /// Resolve a URL or repository string to a WikiNode ID.
    /// </summary>
    public string? ResolveUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var normalizedInput = NormalizeUrl(url);

        lock (_lock)
        {
            // Match if input contains the pattern, ordered by pattern length descending
            var match = _urls
                .Where(u => normalizedInput.Contains(u.Pattern))
                .OrderByDescending(u => u.Pattern.Length)
                .FirstOrDefault();

            return match?.NodeId;
        }
    }

    /// <summary>
    /// Resolve a window title to a WikiNode ID.
    /// </summary>
    public string? ResolveWindow(string windowTitle)
    {
        if (string.IsNullOrWhiteSpace(windowTitle)) return null;

        lock (_lock)
        {
            // Check wildcards/regex or contains match
            var match = _windows
                .FirstOrDefault(w => IsMatch(windowTitle, w.Pattern));

            return match?.NodeId;
        }
    }

    /// <summary>
    /// Resolve a process name to a WikiNode ID.
    /// </summary>
    public string? ResolveProcess(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return null;

        lock (_lock)
        {
            if (_processes.TryGetValue(processName, out var nodeId))
            {
                return nodeId;
            }
            return null;
        }
    }

    /// <summary>
    /// Resolve a mention/alias to a WikiNode ID.
    /// </summary>
    public string? ResolveAlias(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        lock (_lock)
        {
            // Match alias case-insensitively. Prefer exact matches, then starts-with, then contains.
            var exactMatch = _aliases.FirstOrDefault(a => string.Equals(a.Alias, text, StringComparison.OrdinalIgnoreCase));
            if (exactMatch != null) return exactMatch.NodeId;

            var startsWithMatch = _aliases.FirstOrDefault(a => text.StartsWith(a.Alias, StringComparison.OrdinalIgnoreCase));
            if (startsWithMatch != null) return startsWithMatch.NodeId;

            var containsMatch = _aliases.FirstOrDefault(a => text.Contains(a.Alias, StringComparison.OrdinalIgnoreCase));
            return containsMatch?.NodeId;
        }
    }

    /// <summary>
    /// Resolve any input string to a WikiNode ID by attempting all modalities.
    /// </summary>
    public string? Resolve(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        // 1. Check if it looks like a path (has directory separators)
        if (input.Contains('\\') || input.Contains('/'))
        {
            var node = ResolvePath(input);
            if (node != null) return node;
        }

        // 2. Check if it looks like a URL
        if (input.Contains("://") || input.Contains("www.") || input.Contains(".com") || input.Contains(".org"))
        {
            var node = ResolveUrl(input);
            if (node != null) return node;
        }

        // 3. Try process lookup
        var processNode = ResolveProcess(input);
        if (processNode != null) return processNode;

        // 4. Try window match
        var windowNode = ResolveWindow(input);
        if (windowNode != null) return windowNode;

        // 5. Fallback to alias match
        return ResolveAlias(input);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
    }

    private static string NormalizeUrl(string url)
    {
        url = url.ToLowerInvariant();
        if (url.StartsWith("https://")) url = url["https://".Length..];
        if (url.StartsWith("http://")) url = url["http://".Length..];
        if (url.StartsWith("www.")) url = url["www.".Length..];
        return url.TrimEnd('/');
    }

    private static bool IsMatch(string input, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return false;

        // Wildcard support: e.g. "*Engram*"
        if (pattern.Contains('*') || pattern.Contains('?'))
        {
            try
            {
                var escaped = Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".");
                return Regex.IsMatch(input, $"^{escaped}$", RegexOptions.IgnoreCase);
            }
            catch
            {
                // Fallback to simple contains
                return input.Contains(pattern.Replace("*", ""), StringComparison.OrdinalIgnoreCase);
            }
        }

        return input.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }

    private class PathMapping
    {
        public required string Pattern { get; set; }
        public required string NodeId { get; set; }
    }

    private class UrlMapping
    {
        public required string Pattern { get; set; }
        public required string NodeId { get; set; }
    }

    private class WindowMapping
    {
        public required string Pattern { get; set; }
        public required string NodeId { get; set; }
    }

    private class AliasMapping
    {
        public required string Alias { get; set; }
        public required string NodeId { get; set; }
    }
}
