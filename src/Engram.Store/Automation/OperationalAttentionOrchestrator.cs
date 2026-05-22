using System;
using System.Collections.Generic;
using System.Linq;

namespace Engram.Store.Automation;

public class OperationalAttentionOrchestrator
{
    private readonly object _lock = new();
    private readonly HashSet<string> _salientProcesses = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _salientTabKeywords = new(StringComparer.OrdinalIgnoreCase);

    public OperationalAttentionOrchestrator()
    {
        // Default default salient processes
        lock (_lock)
        {
            _salientProcesses.Add("chrome");
            _salientProcesses.Add("msedge");
            _salientProcesses.Add("explorer");
            _salientProcesses.Add("cmd");
            _salientProcesses.Add("powershell");
        }
    }

    public void SetFocus(IEnumerable<string> processes, IEnumerable<string> tabKeywords)
    {
        lock (_lock)
        {
            _salientProcesses.Clear();
            foreach (var p in processes)
            {
                _salientProcesses.Add(p);
            }

            _salientTabKeywords.Clear();
            foreach (var kw in tabKeywords)
            {
                _salientTabKeywords.Add(kw);
            }
        }
    }

    public bool IsRelevant(string processName, string windowTitle)
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(processName)) return false;

            // If the process is explicitly salient
            if (_salientProcesses.Contains(processName))
            {
                return true;
            }

            // Check if window title contains any of the salient keywords
            if (!string.IsNullOrEmpty(windowTitle))
            {
                if (_salientTabKeywords.Any(kw => windowTitle.Contains(kw, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public List<string> FilterContext(IEnumerable<string> rawContextLines)
    {
        lock (_lock)
        {
            var filtered = new List<string>();
            foreach (var line in rawContextLines)
            {
                if (string.IsNullOrEmpty(line)) continue;

                // Simple salience weighting: check if line contains any salient process or keyword
                bool hasProcessMatch = _salientProcesses.Any(p => line.Contains(p, StringComparison.OrdinalIgnoreCase));
                bool hasKeywordMatch = _salientTabKeywords.Any(kw => line.Contains(kw, StringComparison.OrdinalIgnoreCase));

                // Always keep lines containing "error", "exception", "fail", "warning"
                bool isErrorOrWarning = line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                                       line.Contains("exception", StringComparison.OrdinalIgnoreCase) ||
                                       line.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
                                       line.Contains("warning", StringComparison.OrdinalIgnoreCase);

                if (hasProcessMatch || hasKeywordMatch || isErrorOrWarning)
                {
                    filtered.Add(line);
                }
            }

            return filtered;
        }
    }
}
