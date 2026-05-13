using System.Text;
using Engram.Store.Wiki;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Search;

/// <summary>
/// Generates morning and evening briefs from wiki memory.
/// Briefs cite source wiki nodes and raw events.
/// </summary>
public class BriefGenerator
{
    private readonly WikiNodeStore _nodeStore;
    private readonly ILogger<BriefGenerator>? _logger;

    public BriefGenerator(WikiNodeStore nodeStore, ILogger<BriefGenerator>? logger = null)
    {
        _nodeStore = nodeStore;
        _logger = logger;
    }

    /// <summary>
    /// Generate a morning brief: recent changes, stale items, open questions.
    /// </summary>
    public BriefResult GenerateMorningBrief()
    {
        var nodes = _nodeStore.LoadAll();
        var now = DateTimeOffset.UtcNow;

        var recentChanges = nodes
            .Where(n => (now - n.LastTouchedAt).TotalDays <= 1)
            .OrderByDescending(n => n.LastTouchedAt)
            .Take(10)
            .ToList();

        var staleItems = nodes
            .Where(n => (now - n.LastTouchedAt).TotalDays > 7)
            .OrderBy(n => n.LastTouchedAt)
            .Take(10)
            .ToList();

        var openQuestions = nodes
            .Where(n => n.OpenQuestions.Count > 0)
            .SelectMany(n => n.OpenQuestions.Select(q => (Node: n, Question: q)))
            .Take(10)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("# Morning Brief");
        sb.AppendLine($"*Generated: {now:yyyy-MM-dd HH:mm}*");
        sb.AppendLine();

        if (recentChanges.Count > 0)
        {
            sb.AppendLine("## Recent Changes (last 24h)");
            sb.AppendLine();
            foreach (var node in recentChanges)
                sb.AppendLine($"- [[{node.NodeId}]] — {node.Title} ({node.LastTouchedAt:MMM dd HH:mm})");
            sb.AppendLine();
        }

        if (staleItems.Count > 0)
        {
            sb.AppendLine("## Stale Items (>7 days)");
            sb.AppendLine();
            foreach (var node in staleItems)
                sb.AppendLine($"- [[{node.NodeId}]] — {node.Title} (last touched: {node.LastTouchedAt:MMM dd})");
            sb.AppendLine();
        }

        if (openQuestions.Count > 0)
        {
            sb.AppendLine("## Open Questions");
            sb.AppendLine();
            foreach (var (node, question) in openQuestions)
                sb.AppendLine($"- {question} *({node.Title})*");
            sb.AppendLine();
        }

        if (recentChanges.Count == 0 && staleItems.Count == 0 && openQuestions.Count == 0)
        {
            sb.AppendLine("No activity to report. Your wiki is quiet.");
            sb.AppendLine();
        }

        var content = sb.ToString();
        _logger?.LogInformation("Morning brief generated: {Recent} recent, {Stale} stale, {Questions} questions",
            recentChanges.Count, staleItems.Count, openQuestions.Count);

        return new BriefResult
        {
            Type = BriefType.Morning,
            Content = content,
            RecentChanges = recentChanges.Count,
            StaleItems = staleItems.Count,
            OpenQuestions = openQuestions.Count,
            GeneratedAt = now
        };
    }

    /// <summary>
    /// Generate an evening brief: what was accomplished, what's pending.
    /// </summary>
    public BriefResult GenerateEveningBrief()
    {
        var nodes = _nodeStore.LoadAll();
        var now = DateTimeOffset.UtcNow;
        var today = now.Date;

        var todayChanges = nodes
            .Where(n => n.LastTouchedAt.Date == today)
            .OrderByDescending(n => n.LastTouchedAt)
            .ToList();

        var pendingQuestions = nodes
            .Where(n => n.OpenQuestions.Count > 0)
            .SelectMany(n => n.OpenQuestions.Select(q => (Node: n, Question: q)))
            .Take(10)
            .ToList();

        var lowSalience = nodes
            .Where(n => n.Salience < 0.5)
            .OrderBy(n => n.Salience)
            .Take(5)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("# Evening Brief");
        sb.AppendLine($"*Generated: {now:yyyy-MM-dd HH:mm}*");
        sb.AppendLine();

        if (todayChanges.Count > 0)
        {
            sb.AppendLine("## Today's Activity");
            sb.AppendLine();
            foreach (var node in todayChanges)
                sb.AppendLine($"- [[{node.NodeId}]] — {node.Title} ({node.Facts.Count} facts)");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("## Today's Activity");
            sb.AppendLine();
            sb.AppendLine("No wiki changes today.");
            sb.AppendLine();
        }

        if (pendingQuestions.Count > 0)
        {
            sb.AppendLine("## Still Pending");
            sb.AppendLine();
            foreach (var (node, question) in pendingQuestions)
                sb.AppendLine($"- {question} *({node.Title})*");
            sb.AppendLine();
        }

        if (lowSalience.Count > 0)
        {
            sb.AppendLine("## Fading Knowledge (low salience)");
            sb.AppendLine();
            foreach (var node in lowSalience)
                sb.AppendLine($"- [[{node.NodeId}]] — {node.Title} (salience: {node.Salience:F2})");
            sb.AppendLine();
        }

        var content = sb.ToString();
        _logger?.LogInformation("Evening brief generated: {Today} changes, {Pending} pending, {Fading} fading",
            todayChanges.Count, pendingQuestions.Count, lowSalience.Count);

        return new BriefResult
        {
            Type = BriefType.Evening,
            Content = content,
            RecentChanges = todayChanges.Count,
            StaleItems = lowSalience.Count,
            OpenQuestions = pendingQuestions.Count,
            GeneratedAt = now
        };
    }

    /// <summary>
    /// Save brief to .engram/wiki/.
    /// </summary>
    public void SaveBrief(BriefResult brief, string wikiPath)
    {
        var fileName = brief.Type == BriefType.Morning ? "brief_morning.md" : "brief_evening.md";
        var filePath = Path.Combine(wikiPath, fileName);
        var tmpPath = filePath + ".tmp";

        File.WriteAllText(tmpPath, brief.Content);
        File.Move(tmpPath, filePath, overwrite: true);

        _logger?.LogInformation("Brief saved to {Path}", filePath);
    }
}

public class BriefResult
{
    public BriefType Type { get; init; }
    public string Content { get; init; } = string.Empty;
    public int RecentChanges { get; init; }
    public int StaleItems { get; init; }
    public int OpenQuestions { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
}

public enum BriefType
{
    Morning,
    Evening
}
