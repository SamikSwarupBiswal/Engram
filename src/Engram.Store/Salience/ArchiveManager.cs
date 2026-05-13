using Engram.Store.Wiki;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Salience;

/// <summary>
/// Manages archival of stale wiki nodes.
/// Moves nodes with low salience from wiki/ to archives/.
/// </summary>
public class ArchiveManager
{
    private readonly WikiNodeStore _nodeStore;
    private readonly SalienceScorer _scorer;
    private readonly string _archivePath;
    private readonly ILogger<ArchiveManager>? _logger;
    private readonly double _threshold;

    public ArchiveManager(
        WikiNodeStore nodeStore,
        SalienceScorer scorer,
        WorkspacePaths paths,
        double archiveThreshold = 0.1,
        ILogger<ArchiveManager>? logger = null)
    {
        _nodeStore = nodeStore;
        _scorer = scorer;
        _archivePath = paths.Archives;
        _logger = logger;
        _threshold = archiveThreshold;
    }

    /// <summary>
    /// Find nodes that should be archived.
    /// </summary>
    public IReadOnlyList<WikiNode> GetArchiveCandidates()
    {
        var nodes = _nodeStore.LoadAll();
        return nodes.Where(n => _scorer.ShouldArchive(n, _threshold)).ToList();
    }

    /// <summary>
    /// Archive a single node. Moves from wiki/ to archives/.
    /// </summary>
    public bool ArchiveNode(WikiNode node)
    {
        Directory.CreateDirectory(_archivePath);

        var serializer = new WikiNodeSerializer();
        var content = serializer.Serialize(node);

        var fileName = node.NodeId + ".md";
        var archivePath = Path.Combine(_archivePath, fileName);
        var tmpPath = archivePath + ".tmp";

        // Write to archives
        File.WriteAllText(tmpPath, content);
        File.Move(tmpPath, archivePath, overwrite: true);

        // Remove from wiki
        var wikiPath = Path.Combine(_nodeStore.GetWikiPath(), fileName);
        if (File.Exists(wikiPath))
            File.Delete(wikiPath);

        _logger?.LogInformation("Archived node: {NodeId} (salience: {Salience:F3})",
            node.NodeId, _scorer.Compute(node));

        return true;
    }

    /// <summary>
    /// Archive all nodes below the salience threshold.
    /// Returns list of archived node IDs.
    /// </summary>
    public IReadOnlyList<string> ArchiveStaleNodes()
    {
        var candidates = GetArchiveCandidates();
        var archived = new List<string>();

        foreach (var node in candidates)
        {
            if (ArchiveNode(node))
                archived.Add(node.NodeId);
        }

        if (archived.Count > 0)
        {
            _logger?.LogInformation("Archived {Count} stale nodes", archived.Count);
            GenerateArchiveIndex();
        }

        return archived;
    }

    /// <summary>
    /// Restore a node from archive back to wiki.
    /// </summary>
    public bool RestoreFromArchive(string nodeId)
    {
        var fileName = nodeId + ".md";
        var archivePath = Path.Combine(_archivePath, fileName);

        if (!File.Exists(archivePath))
        {
            _logger?.LogWarning("Archive file not found: {NodeId}", nodeId);
            return false;
        }

        var content = File.ReadAllText(archivePath);
        var serializer = new WikiNodeSerializer();
        var node = serializer.Deserialize(content);

        if (node == null)
        {
            _logger?.LogWarning("Failed to deserialize archived node: {NodeId}", nodeId);
            return false;
        }

        // Reset salience on restore
        node.Salience = 1.0;
        node.LastTouchedAt = DateTimeOffset.UtcNow;

        _nodeStore.Save(node);

        // Remove from archive
        File.Delete(archivePath);

        _logger?.LogInformation("Restored node from archive: {NodeId}", nodeId);
        return true;
    }

    /// <summary>
    /// List all archived nodes.
    /// </summary>
    public IReadOnlyList<WikiNode> ListArchived()
    {
        if (!Directory.Exists(_archivePath))
            return new List<WikiNode>();

        var serializer = new WikiNodeSerializer();
        var nodes = new List<WikiNode>();

        foreach (var file in Directory.EnumerateFiles(_archivePath, "*.md"))
        {
            if (Path.GetFileName(file) == "index.md") continue;

            try
            {
                var content = File.ReadAllText(file);
                var node = serializer.Deserialize(content);
                if (node != null) nodes.Add(node);
            }
            catch { }
        }

        return nodes;
    }

    /// <summary>
    /// Generate archives/index.md.
    /// </summary>
    private void GenerateArchiveIndex()
    {
        var archived = ListArchived();
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("# Archived Nodes");
        sb.AppendLine();
        sb.AppendLine("*Nodes with low salience, moved from wiki.*");
        sb.AppendLine($"*Generated: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm}*");
        sb.AppendLine();

        foreach (var node in archived)
        {
            sb.AppendLine($"- [{node.Title}]({node.NodeId}.md) ({node.NodeType}) — archived: {node.LastTouchedAt:yyyy-MM-dd}");
        }

        var indexPath = Path.Combine(_archivePath, "index.md");
        var tmpPath = indexPath + ".tmp";
        File.WriteAllText(tmpPath, sb.ToString());
        File.Move(tmpPath, indexPath, overwrite: true);
    }
}
