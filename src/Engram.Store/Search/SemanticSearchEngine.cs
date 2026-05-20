using System.Text.RegularExpressions;
using Engram.Store.Wiki;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Search;

/// <summary>
/// Enhanced search engine with hybrid retrieval.
/// 
/// Improvements over basic SearchEngine:
/// 1. Salience-weighted ranking (recent/important nodes rank higher)
/// 2. Type-based boosting (Person/Project/Goal matches get priority)
/// 3. Exact phrase matching (multi-word queries match as phrases)
/// 4. Fuzzy matching for partial matches
/// 5. Fact-level relevance (highlights which facts matched)
/// 
/// This is the retrieval component that makes PromptAssembler effective.
/// </summary>
public class SemanticSearchEngine : IDisposable
{
    private readonly WikiNodeStore _nodeStore;
    private readonly Salience.SalienceScorer _salienceScorer;
    private readonly ILogger<SemanticSearchEngine>? _logger;
    private List<WikiNode>? _index;
    private bool _disposed;

    public SemanticSearchEngine(
        WikiNodeStore nodeStore,
        Salience.SalienceScorer? salienceScorer = null,
        ILogger<SemanticSearchEngine>? logger = null)
    {
        _nodeStore = nodeStore;
        _salienceScorer = salienceScorer ?? new Salience.SalienceScorer();
        _logger = logger;
    }

    /// <summary>
    /// Search for relevant wiki nodes using hybrid retrieval.
    /// </summary>
    public SearchResponse Search(string query, int maxResults = 20)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(query))
            return new SearchResponse { Query = query, Results = Array.Empty<SearchResult>(), NodesSearched = 0, Duration = sw.Elapsed };

        if (_index == null) RebuildIndex();

        var terms = Tokenize(query);
        var phrase = query.Trim().ToLowerInvariant();

        if (terms.Length == 0)
            return new SearchResponse { Query = query, Results = Array.Empty<SearchResult>(), NodesSearched = _index?.Count ?? 0, Duration = sw.Elapsed };

        var results = new List<SearchResult>();

        foreach (var node in _index!)
        {
            var (score, matchedFacts, matchedFields) = ScoreNode(node, terms, phrase);
            if (score > 0)
            {
                // Apply salience weighting
                var salience = _salienceScorer.Compute(node);
                var salienceBoost = 0.8 + (0.2 * salience); // 0.8-1.0 range

                // Apply type boosting
                var typeBoost = GetTypeBoost(node.NodeType);

                var finalScore = score * salienceBoost * typeBoost;

                results.Add(new SearchResult
                {
                    Node = node,
                    Relevance = Math.Min(1.0, finalScore),
                    MatchingFacts = matchedFacts,
                    MatchedFields = matchedFields
                });
            }
        }

        var sorted = results.OrderByDescending(r => r.Relevance).Take(maxResults).ToList();

        _logger?.LogInformation("Semantic search '{Query}': {Count} results from {Total} nodes in {Ms}ms",
            query, sorted.Count, _index.Count, sw.ElapsedMilliseconds);

        return new SearchResponse { Query = query, Results = sorted, NodesSearched = _index.Count, Duration = sw.Elapsed };
    }

    public void InvalidateIndex() { _index = null; }

    public void RebuildIndex()
    {
        _index = _nodeStore.LoadAll().ToList();
        _logger?.LogInformation("Semantic search index rebuilt: {Count} nodes", _index.Count);
    }

    private (double score, List<WikiFact> matchedFacts, List<string> matchedFields) ScoreNode(
        WikiNode node, string[] terms, string phrase)
    {
        double totalScore = 0;
        var matchedFacts = new List<WikiFact>();
        var matchedFields = new List<string>();

        // Build searchable text corpus
        var titleLower = (node.Title ?? "").ToLowerInvariant();
        var summaryLower = (node.Summary ?? "").ToLowerInvariant();
        var allFactsText = string.Join(" ", node.Facts.Select(f => f.Text)).ToLowerInvariant();
        var allQuestionsText = string.Join(" ", node.OpenQuestions).ToLowerInvariant();

        var allText = $"{titleLower} {summaryLower} {allFactsText} {allQuestionsText}";

        // Check if ALL terms appear (AND semantics)
        foreach (var term in terms)
        {
            if (!allText.Contains(term))
                return (0, matchedFacts, matchedFields);
        }

        // 1. Exact phrase match (highest signal)
        if (phrase.Length >= 3 && allText.Contains(phrase))
        {
            totalScore += 5.0;
            matchedFields.Add("exact_phrase");
        }

        // 2. Title matching (high weight)
        var titleScore = ScoreText(titleLower, terms) * 4.0;
        if (titleScore > 0)
        {
            totalScore += titleScore;
            matchedFields.Add("title");
        }

        // 3. Summary matching (medium weight)
        var summaryScore = ScoreText(summaryLower, terms) * 2.5;
        if (summaryScore > 0)
        {
            totalScore += summaryScore;
            matchedFields.Add("summary");
        }

        // 4. Fact matching (base weight, but track which facts)
        foreach (var fact in node.Facts)
        {
            var factText = (fact.Text ?? "").ToLowerInvariant();
            var factScore = ScoreText(factText, terms);
            if (factScore > 0)
            {
                totalScore += factScore;
                matchedFacts.Add(fact);
                if (!matchedFields.Contains("facts")) matchedFields.Add("facts");
            }
        }

        // 5. Question matching (low weight)
        foreach (var q in node.OpenQuestions)
        {
            var qScore = ScoreText((q ?? "").ToLowerInvariant(), terms) * 0.5;
            if (qScore > 0)
            {
                totalScore += qScore;
                if (!matchedFields.Contains("questions")) matchedFields.Add("questions");
            }
        }

        // 6. Fuzzy matching bonus (partial term matches)
        var fuzzyBonus = ComputeFuzzyBonus(allText, terms);
        totalScore += fuzzyBonus;

        // Normalize
        var maxPossibleScore = terms.Length * 8.0; // Max per term
        var normalizedScore = Math.Min(1.0, totalScore / maxPossibleScore);

        return (normalizedScore, matchedFacts, matchedFields);
    }

    private static double ScoreText(string text, string[] terms)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        double score = 0;
        foreach (var term in terms)
        {
            int count = 0, idx = 0;
            while ((idx = text.IndexOf(term, idx, StringComparison.Ordinal)) >= 0)
            {
                count++;
                idx += term.Length;
            }
            if (count > 0) score += 1.0 + Math.Log(count);
        }
        return score;
    }

    /// <summary>
    /// Compute bonus for fuzzy/partial matches.
    /// Helps with typos and partial words.
    /// </summary>
    private static double ComputeFuzzyBonus(string allText, string[] terms)
    {
        double bonus = 0;
        foreach (var term in terms)
        {
            if (term.Length < 4) continue;

            // Check if the first 3 characters appear as a prefix
            var prefix = term[..3];
            if (allText.Contains(prefix))
            {
                bonus += 0.2; // Small bonus for partial match
            }
        }
        return bonus;
    }

    /// <summary>
    /// Get type-based boost factor.
    /// Person/Project/Goal entities are more important in conversations.
    /// </summary>
    private static double GetTypeBoost(WikiNodeType type)
    {
        return type switch
        {
            WikiNodeType.Person => 1.3,
            WikiNodeType.Project => 1.2,
            WikiNodeType.Goal => 1.2,
            WikiNodeType.Decision => 1.15,
            WikiNodeType.Concept => 1.0,
            WikiNodeType.Document => 0.9,
            WikiNodeType.Receipt => 0.8,
            _ => 1.0
        };
    }

    private static string[] Tokenize(string query)
    {
        return query.ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 2)
            .Distinct()
            .ToArray();
    }

    public void Dispose()
    {
        if (!_disposed) { _index = null; _disposed = true; }
    }
}
