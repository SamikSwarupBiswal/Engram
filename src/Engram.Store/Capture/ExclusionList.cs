using System.Collections.Concurrent;

namespace Engram.Store.Capture;

/// <summary>
/// Manages the list of excluded applications that must NEVER be captured.
/// Thread-safe for concurrent access from multiple capture providers.
/// Matches by process name (case-insensitive).
/// </summary>
public class ExclusionList
{
    private readonly ConcurrentDictionary<string, byte> _excluded = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Default exclusions: security-sensitive applications.
    /// </summary>
    public static readonly string[] DefaultExclusions = new[]
    {
        "1password", "bitwarden", "keepass", "lastpass", "dashlane",
        "roboform", "keeper", "nordpass", "enpass", "truekey",
        "banking", "bank", "paypal", "venmo", "cashapp"
    };

    public ExclusionList()
    {
        foreach (var app in DefaultExclusions)
            _excluded.TryAdd(app, 0);
    }

    /// <summary>
    /// Check if a process name is excluded. Case-insensitive.
    /// </summary>
    public bool IsExcluded(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return false;

        return _excluded.ContainsKey(processName);
    }

    /// <summary>
    /// Add a process name to the exclusion list.
    /// </summary>
    public void Add(string processName)
    {
        if (!string.IsNullOrWhiteSpace(processName))
            _excluded.TryAdd(processName.Trim(), 0);
    }

    /// <summary>
    /// Remove a process name from the exclusion list.
    /// </summary>
    public bool Remove(string processName)
    {
        return _excluded.TryRemove(processName.Trim(), out _);
    }

    /// <summary>
    /// Get all excluded process names.
    /// </summary>
    public IReadOnlyCollection<string> GetAll()
    {
        return _excluded.Keys.ToList().AsReadOnly();
    }

    public void LoadFromConfig(IEnumerable<string>? configExclusions)
    {
        if (configExclusions == null) return;
        foreach (var app in configExclusions)
        {
            if (!string.IsNullOrWhiteSpace(app))
                _excluded.TryAdd(app.Trim(), 0);
        }
    }
}
