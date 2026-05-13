using System.Text.Json;

namespace Engram.Store.Cloud;

/// <summary>
/// Semantic cache for non-private common research topics.
/// Stored at .engram/cache/clean-cache.json.
/// Private data is NEVER cached.
/// </summary>
public class CleanCache : IDisposable
{
    private readonly string _cachePath;
    private readonly int _maxEntries;
    private readonly object _lock = new();
    private Dictionary<string, CacheEntry> _entries = new();
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public CleanCache(string cacheDirectory, int maxEntries = 500)
    {
        ArgumentNullException.ThrowIfNull(cacheDirectory);
        Directory.CreateDirectory(cacheDirectory);
        _cachePath = Path.Combine(cacheDirectory, "clean-cache.json");
        _maxEntries = maxEntries;
        Load();
    }

    /// <summary>
    /// Try to get a cached response. Increments hit count on success.
    /// </summary>
    public bool TryGet(string key, out CacheEntry? entry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        entry = null;

        lock (_lock)
        {
            if (!_entries.TryGetValue(key, out var cached)) return false;
            if (cached.IsExpired)
            {
                _entries.Remove(key);
                Save();
                return false;
            }

            // Increment hit count
            var updated = new CacheEntry
            {
                Key = cached.Key,
                Response = cached.Response,
                Provider = cached.Provider,
                Model = cached.Model,
                CostUsd = cached.CostUsd,
                CreatedAt = cached.CreatedAt,
                HitCount = cached.HitCount + 1,
                LastHitAt = DateTimeOffset.UtcNow,
                TtlHours = cached.TtlHours
            };

            _entries[key] = updated;
            entry = updated;
            Save();
            return true;
        }
    }

    /// <summary>
    /// Store a response in the cache. Rejects private data.
    /// </summary>
    public bool Put(string key, CacheEntry entry, PrivacyClass privacyClass)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // NEVER cache private data
        if (privacyClass == PrivacyClass.Private || privacyClass == PrivacyClass.Sensitive)
            return false;

        lock (_lock)
        {
            // Evict oldest entries if at capacity
            if (_entries.Count >= _maxEntries)
            {
                var oldest = _entries.Values
                    .OrderBy(e => e.LastHitAt ?? e.CreatedAt)
                    .First();
                _entries.Remove(oldest.Key);
            }

            _entries[key] = entry;
            Save();
            return true;
        }
    }

    /// <summary>
    /// Get the number of cached entries.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock) { return _entries.Count; }
        }
    }

    /// <summary>
    /// Clear expired entries.
    /// </summary>
    public int EvictExpired()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_lock)
        {
            var expired = _entries.Where(e => e.Value.IsExpired).Select(e => e.Key).ToList();
            foreach (var key in expired) _entries.Remove(key);
            if (expired.Count > 0) Save();
            return expired.Count;
        }
    }

    private void Load()
    {
        if (!File.Exists(_cachePath)) return;

        var json = File.ReadAllText(_cachePath);
        _entries = JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(json, JsonOptions) ?? new();
    }

    private void Save()
    {
        var tmpPath = _cachePath + ".tmp";
        var json = JsonSerializer.Serialize(_entries, JsonOptions);
        File.WriteAllText(tmpPath, json);
        File.Move(tmpPath, _cachePath, overwrite: true);
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
