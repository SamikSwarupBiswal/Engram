using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using Engram.Store.Governance;
using Engram.Store.Inference;

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
    private GovernanceIsolationBoundary? _boundary;

    public void SetBoundary(GovernanceIsolationBoundary boundary)
    {
        _boundary = boundary;
    }

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

        if (Array.Exists(Environment.GetCommandLineArgs(), arg => arg == "--safe-mode") ||
            Environment.GetEnvironmentVariable("ENGRAM_SAFE_MODE") == "true")
        {
            throw new InvalidOperationException("System is running in read-only Safe Mode due to semantic uncertainty.");
        }

        try
        {
            _boundary?.VerifyWriteSafety($"Save node {node.NodeId}");
        }
        catch (InvalidOperationException ex)
        {
            // Record deferred mutation intent
            var mutation = new DeferredMutation
            {
                OperationType = "Save",
                TargetNodeId = node.NodeId,
                TargetContent = _serializer.Serialize(node),
                CausalReason = ex.Message,
                ConfidenceState = DegradationTracker.Instance.GetEnvironmentalConfidence(),
                DegradationState = string.Join(",", DegradationTracker.Instance.GetCapabilityDetails().Keys)
            };
            SaveDeferredMutation(mutation);
            throw;
        }

        // Dynamically populate provenance if default
        if (string.IsNullOrEmpty(node.ProvenanceWorkflowId))
        {
            node.ProvenanceConfidence = node.Confidence;
            node.ProvenanceEnvironmentalReliability = DegradationTracker.Instance.GetEnvironmentalConfidence();
            node.ProvenanceDegradationState = string.Join(",", DegradationTracker.Instance.GetCapabilityDetails().Keys);
            node.ProvenanceApprovalSource = _boundary != null ? "SafetyConstitutionBounded" : "AutoApproved";
        }

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
    /// Delete a wiki node by ID. Returns true if deleted, false if not found.
    /// Thread-safe.
    /// </summary>
    public bool Delete(string nodeId)
    {
        if (Array.Exists(Environment.GetCommandLineArgs(), arg => arg == "--safe-mode") ||
            Environment.GetEnvironmentVariable("ENGRAM_SAFE_MODE") == "true")
        {
            throw new InvalidOperationException("System is running in read-only Safe Mode due to semantic uncertainty.");
        }

        try
        {
            _boundary?.VerifyWriteSafety($"Delete node {nodeId}");
        }
        catch (InvalidOperationException ex)
        {
            var mutation = new DeferredMutation
            {
                OperationType = "Delete",
                TargetNodeId = nodeId,
                CausalReason = ex.Message,
                ConfidenceState = DegradationTracker.Instance.GetEnvironmentalConfidence(),
                DegradationState = string.Join(",", DegradationTracker.Instance.GetCapabilityDetails().Keys)
            };
            SaveDeferredMutation(mutation);
            throw;
        }

        var filePath = GetFilePath(nodeId);

        _lock.EnterWriteLock();
        try
        {
            if (!File.Exists(filePath))
                return false;

            File.Delete(filePath);
            _logger?.LogInformation("Deleted wiki node: {NodeId}", nodeId);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to delete wiki node: {NodeId}", nodeId);
            return false;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
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

    private void SaveDeferredMutation(DeferredMutation mutation)
    {
        try
        {
            var deferredPath = Path.Combine(_wikiPath, "..", "deferred_mutations");
            Directory.CreateDirectory(deferredPath);
            var filePath = Path.Combine(deferredPath, $"{mutation.MutationId}.json");
            var json = System.Text.Json.JsonSerializer.Serialize(mutation, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            var tmpPath = filePath + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to save deferred mutation {MutationId}", mutation.MutationId);
        }
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

public class DeferredMutation
{
    public string MutationId { get; set; } = Guid.NewGuid().ToString("n");
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string OperationType { get; set; } = string.Empty; // "Save" or "Delete"
    public string TargetNodeId { get; set; } = string.Empty;
    public string TargetContent { get; set; } = string.Empty;
    public string CausalReason { get; set; } = string.Empty;
    public double ConfidenceState { get; set; } = 1.0;
    public string DegradationState { get; set; } = string.Empty;
}
