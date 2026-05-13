using Engram.Store.Wiki;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Search;

public class SearchEngine : IDisposable
{
    private readonly WikiNodeStore _nodeStore;
    private readonly ILogger<SearchEngine>? _logger;
    private List<WikiNode>? _index;
    private bool _disposed;

    public SearchEngine(WikiNodeStore nodeStore, ILogger<SearchEngine>? logger = null)
    {
        _nodeStore = nodeStore;
        _logger = logger;
    }

    public SearchResponse Search(string query, int maxResults = 20)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(query))
            return new SearchResponse { Query = query, Results = Array.Empty<SearchResult>(), NodesSearched = 0, Duration = sw.Elapsed };

        if (_index == null) RebuildIndex();

        var terms = Tokenize(query);
        if (terms.Length == 0)
            return new SearchResponse { Query = query, Results = Array.Empty<SearchResult>(), NodesSearched = _index?.Count ?? 0, Duration = sw.Elapsed };

        var results = new List<SearchResult>();

        foreach (var node in _index!)
        {
            var (score, matchedFacts, matchedFields) = ScoreNode(node, terms);
            if (score > 0)
                results.Add(new SearchResult { Node = node, Relevance = score, MatchingFacts = matchedFacts, MatchedFields = matchedFields });
        }

        var sorted = results.OrderByDescending(r => r.Relevance).Take(maxResults).ToList();

        _logger?.LogInformation("Search \'{Query}\': {Count} results from {Total} nodes in {Ms}ms",
            query, sorted.Count, _index.Count, sw.ElapsedMilliseconds);

        return new SearchResponse { Query = query, Results = sorted, NodesSearched = _index.Count, Duration = sw.Elapsed };
    }

    public void InvalidateIndex() { _index = null; }

    public void RebuildIndex()
    {
        _index = _nodeStore.LoadAll().ToList();
        _logger?.LogInformation("Search index rebuilt: {Count} nodes", _index.Count);
    }

    private (double score, List<WikiFact> matchedFacts, List<string> matchedFields) ScoreNode(WikiNode node, string[] terms)
    {
        double totalScore = 0;
        var matchedFacts = new List<WikiFact>();
        var matchedFields = new List<string>();

        var allText = (node.Title + " " + node.Summary + " " +
            string.Join(" ", node.Facts.Select(f => f.Text)) + " " +
            string.Join(" ", node.OpenQuestions)).ToLowerInvariant();

        foreach (var term in terms)
        {
            if (!allText.Contains(term))
                return (0, matchedFacts, matchedFields);
        }

        var titleScore = ScoreText(node.Title, terms) * 3.0;
        if (titleScore > 0) { totalScore += titleScore; matchedFields.Add("title"); }

        var summaryScore = ScoreText(node.Summary, terms) * 2.0;
        if (summaryScore > 0) { totalScore += summaryScore; matchedFields.Add("summary"); }

        foreach (var fact in node.Facts)
        {
            var factScore = ScoreText(fact.Text, terms);
            if (factScore > 0)
            {
                totalScore += factScore;
                matchedFacts.Add(fact);
                if (!matchedFields.Contains("facts")) matchedFields.Add("facts");
            }
        }

        foreach (var q in node.OpenQuestions)
        {
            var qScore = ScoreText(q, terms) * 0.5;
            if (qScore > 0)
            {
                totalScore += qScore;
                if (!matchedFields.Contains("questions")) matchedFields.Add("questions");
            }
        }

        var maxPossibleScore = terms.Length * 6.0;
        var normalizedScore = Math.Min(1.0, totalScore / maxPossibleScore);

        return (normalizedScore, matchedFacts, matchedFields);
    }

    private static double ScoreText(string text, string[] terms)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var normalizedText = text.ToLowerInvariant();
        double score = 0;
        foreach (var term in terms)
        {
            int count = 0, idx = 0;
            while ((idx = normalizedText.IndexOf(term, idx, StringComparison.Ordinal)) >= 0)
            {
                count++;
                idx += term.Length;
            }
            if (count > 0) score += 1.0 + Math.Log(count);
        }
        return score;
    }

    private static string[] Tokenize(string query)
    {
        return query.ToLowerInvariant()
            .Split(new[] { ' ', (char)9, (char)10, (char)13 }, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 2)
            .Distinct()
            .ToArray();
    }

    public void Dispose()
    {
        if (!_disposed) { _index = null; _disposed = true; }
    }
}
