using System.Text;

namespace Engram.Store;

/// <summary>
/// Maintains a hash-to-filepath index for O(1) duplicate detection.
/// Index stored at .engram/raw/.hash-index as pipe-delimited text.
/// Loaded into memory on first access, persisted on each new write.
/// </summary>
public class HashIndex : IDisposable
{
    private readonly string _indexPath;
    private readonly Dictionary<string, string> _index = new();
    private readonly ReaderWriterLockSlim _lock = new();
    private bool _loaded;
    private bool _disposed;

    public HashIndex(string rawDirectory)
    {
        _indexPath = Path.Combine(rawDirectory, ".hash-index");
    }

    /// <summary>
    /// Try to find an existing file path for the given hash. O(1) lookup.
    /// </summary>
    public bool TryGet(string hash, out string filePath)
    {
        EnsureLoaded();
        _lock.EnterReadLock();
        try
        {
            return _index.TryGetValue(hash, out filePath!);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Add a hash->filepath mapping and persist to disk.
    /// </summary>
    public void Add(string hash, string filePath)
    {
        _lock.EnterWriteLock();
        try
        {
            _index[hash] = filePath;
            PersistEntry(hash, filePath);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Count of indexed entries.
    /// </summary>
    public int Count
    {
        get
        {
            _lock.EnterReadLock();
            try { return _index.Count; }
            finally { _lock.ExitReadLock(); }
        }
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;

        _lock.EnterWriteLock();
        try
        {
            if (_loaded) return;

            if (File.Exists(_indexPath))
            {
                foreach (var line in File.ReadLines(_indexPath))
                {
                    var parts = line.Split('|', 2);
                    if (parts.Length == 2)
                        _index[parts[0]] = parts[1];
                }
            }

            _loaded = true;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    private void PersistEntry(string hash, string filePath)
    {
        var dir = Path.GetDirectoryName(_indexPath);
        if (dir != null) Directory.CreateDirectory(dir);

        File.AppendAllText(_indexPath, hash + "|" + filePath + Environment.NewLine);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _lock.Dispose();
            _disposed = true;
        }
    }
}
