using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Agent;

/// <summary>
/// Manages citations in research summaries.
/// Extracts [N] citation markers, validates against sources, generates references.
/// </summary>
public class CitationEngine
{
    private readonly ILogger<CitationEngine>? _logger;

    public CitationEngine(ILogger<CitationEngine>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>Extract all citation markers [N] from text.</summary>
    public List<CitationMarker> ExtractCitations(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<CitationMarker>();

        var matches = Regex.Matches(text, @"\[(\d+)\]");
        return matches.Select(m => new CitationMarker
        {
            Index = int.Parse(m.Groups[1].Value),
            Position = m.Index,
            RawText = m.Value
        }).DistinctBy(c => c.Index).OrderBy(c => c.Index).ToList();
    }

    /// <summary>Validate that all citations have matching sources. Returns invalid indices.</summary>
    public List<int> ValidateCitations(string text, List<ResearchSource> sources)
    {
        var citations = ExtractCitations(text);
        var sourceIndices = sources.Select(s => s.CitationIndex).ToHashSet();
        return citations.Where(c => !sourceIndices.Contains(c.Index)).Select(c => c.Index).ToList();
    }

    /// <summary>Generate a formatted references section.</summary>
    public string GenerateReferences(List<ResearchSource> sources)
    {
        if (sources.Count == 0) return "No sources cited.";
        var refs = sources.OrderBy(s => s.CitationIndex).Select(s => $"[{s.CitationIndex}] {s.Title} -- {s.Url}");
        return "## References\n\n" + string.Join("\n", refs);
    }

    /// <summary>Replace [N] markers with [N](url) hyperlinks.</summary>
    public string LinkifyCitations(string text, List<ResearchSource> sources)
    {
        var sourceMap = sources.ToDictionary(s => s.CitationIndex, s => s.Url);
        return Regex.Replace(text, @"\[(\d+)\]", match =>
        {
            var index = int.Parse(match.Groups[1].Value);
            return sourceMap.TryGetValue(index, out var url) ? $"[{index}]({url})" : match.Value;
        });
    }
}

public class CitationMarker
{
    public int Index { get; init; }
    public int Position { get; init; }
    public string RawText { get; init; } = string.Empty;
}
