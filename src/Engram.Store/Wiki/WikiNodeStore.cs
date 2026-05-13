using Microsoft.Extensions.Logging;

namespace Engram.Store.Wiki;

/// <summary>
/// Reads and writes WikiNode files to .engram/wiki/.
/// Thread-safe for concurrent access.
/// </summary>
public class WikiNodeStore : IDisposable
{
    private readonly string _wikiPath;
    private readonly WikiNodeSerializer _serializer;
    private readonly ILogger<WikiNodeStore>? _logger;
    private readonly ReaderWriterLockSlim _lock = new();
    private bool _disposed;

    public WikiNodeStore(WorkspacePaths paths, ILogger<WikiNodeStore>? logger = null)
    {
        _wikiPath = paths.Wiki;
        _serializer = new WikiNodeSerializer();
        _logger = logger;
    }

    /// <summary>
    /// Save a wiki node to disk. Atomic write (tmp + rename).
    /// </summary>
    public void Save(WikiNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var filePath = GetFilePath(node.NodeId);
        Directory.CreateDirectory(_wikiPath);

        _lock.EnterWriteLock();
        try
        {
            var markdown = _serializer.Serialize(node);
            var tmpPath = filePath + ".tmp";
            File.WriteAllText(tmpPath, markdown);
            File.Move(tmpPath, filePath, overwrite: true);

            _logger?.LogDebug("Saved wiki node: {NodeId} -> {Path}", node.NodeId, filePath);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Load a wiki node by ID. Returns null if not found.
    /// </summary>
    public WikiNode? Load(string nodeId)
    {
        var filePath = GetFilePath(nodeId);

        _lock.EnterReadLock();
        try
        {
            if (!File.Exists(filePath))
                return null;

            var markdown = File.ReadAllText(filePath);
            return _serializer.Deserialize(markdown);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load wiki node: {NodeId}", nodeId);
            return null;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Load all wiki nodes from the wiki directory.
    /// </summary>
    public IReadOnlyList<WikiNode> LoadAll()
    {
        var nodes = new List<WikiNode>();

        if (!Directory.Exists(_wikiPath))
            return nodes;

        _lock.EnterReadLock();
        try
        {
            foreach (var file in Directory.EnumerateFiles(_wikiPath, "*.md"))
            {
                if (Path.GetFileName(file) == "index.md") continue;

                try
                {
                    var markdown = File.ReadAllText(file);
                    var node = _serializer.Deserialize(markdown);
                    if (node != null)
                        nodes.Add(node);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to parse wiki file: {Path}", file);
                }
            }
        }
        finally
        {
            _lock.ExitReadLock();
        }

        return nodes;
    }

    /// <summary>
    /// Check if a node exists.
    /// </summary>
    public bool Exists(string nodeId)
    {
        return File.Exists(GetFilePath(nodeId));
    }

    /// <summary>
    /// Get the wiki directory path.
    /// </summary>
    public string GetWikiPath() => _wikiPath;

    private string GetFilePath(string nodeId)
    {
        // Sanitize node ID for file system
        var safeId = string.Join("_", nodeId.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_wikiPath, $"{safeId}.md");
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
