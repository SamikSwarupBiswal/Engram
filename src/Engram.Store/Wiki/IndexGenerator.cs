using System.Text;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Wiki;

/// <summary>
/// Generates index.md — the navigation map for the wiki.
/// Grouped by node type with counts and [[links]].
/// </summary>
public class IndexGenerator
{
    private readonly WikiNodeStore _store;
    private readonly ILogger<IndexGenerator>? _logger;

    public IndexGenerator(WikiNodeStore store, ILogger<IndexGenerator>? logger = null)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Generate index.md content from all wiki nodes.
    /// </summary>
    public string Generate()
    {
        var nodes = _store.LoadAll();

        _logger?.LogInformation("Generating index for {Count} wiki nodes", nodes.Count);

        var sb = new StringBuilder();

        sb.AppendLine("# Engram Wiki Index");
        sb.AppendLine();
        sb.AppendLine($"*Generated: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss UTC}*");
        sb.AppendLine($"*Total nodes: {nodes.Count}*");
        sb.AppendLine();

        // Group by type
        var groups = nodes
            .GroupBy(n => n.NodeType)
            .OrderBy(g => g.Key.ToString());

        foreach (var group in groups)
        {
            sb.AppendLine($"## {group.Key} ({group.Count()})");
            sb.AppendLine();

            foreach (var node in group.OrderByDescending(n => n.Salience))
            {
                var staleWarning = IsStale(node) ? " ⚠️" : "";
                var summary = string.IsNullOrWhiteSpace(node.Summary) ? "" : $" — {Truncate(node.Summary, 80)}";
                sb.AppendLine($"- [[{node.NodeId}]]{staleWarning}{summary}");
            }

            sb.AppendLine();
        }

        // Recent changes
        var recent = nodes
            .OrderByDescending(n => n.LastTouchedAt)
            .Take(10)
            .ToList();

        if (recent.Count > 0)
        {
            sb.AppendLine("## Recently Updated");
            sb.AppendLine();
            foreach (var node in recent)
            {
                sb.AppendLine($"- [[{node.NodeId}]] — {node.LastTouchedAt:yyyy-MM-dd}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Save the generated index to .engram/wiki/index.md.
    /// </summary>
    public void SaveIndex(string wikiPath)
    {
        var content = Generate();
        var indexPath = Path.Combine(wikiPath, "index.md");
        var tmpPath = indexPath + ".tmp";

        File.WriteAllText(tmpPath, content);
        File.Move(tmpPath, indexPath, overwrite: true);

        _logger?.LogInformation("Index saved to {Path}", indexPath);
    }

    private static bool IsStale(WikiNode node)
    {
        return (DateTimeOffset.UtcNow - node.LastTouchedAt).TotalDays > 30;
    }

    private static string Truncate(string text, int maxLength)
    {
        return text.Length > maxLength ? text[..(maxLength - 3)] + "..." : text;
    }
}
