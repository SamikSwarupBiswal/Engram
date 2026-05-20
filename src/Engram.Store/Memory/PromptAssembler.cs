using System.Text;
using Engram.Store.Identity;
using Engram.Store.Metabolism;
using Engram.Store.Search;
using Engram.Store.Wiki;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Memory;

/// <summary>
/// Retrieval-augmented prompt assembly.
/// 
/// Instead of dumping top 5 salient nodes (current behavior),
/// this queries the wiki memory graph for context RELEVANT to the user's message.
/// 
/// Pipeline:
///   user message → query search engine → retrieve relevant wiki nodes → 
///   combine with identity context → assemble system prompt → send to Phi
/// 
/// THIS changes behavior:
///   Without retrieval: Phi behaves generic.
///   With retrieval: Engram behaves personal.
/// </summary>
public class PromptAssembler
{
    private readonly IdentityStore _identityStore;
    private readonly WikiNodeStore _nodeStore;
    private readonly SearchEngine _searchEngine;
    private readonly RetrievalBudgetManager _budgetManager;
    private readonly ILogger<PromptAssembler>? _logger;

    /// <summary>Maximum tokens for the system prompt (conservative for Phi-4-mini 4096 context).</summary>
    public int MaxSystemPromptTokens { get; set; } = 800;

    /// <summary>Maximum wiki nodes to include in context.</summary>
    public int MaxWikiNodes { get; set; } = 8;

    public PromptAssembler(
        IdentityStore identityStore,
        WikiNodeStore nodeStore,
        SearchEngine searchEngine,
        RetrievalBudgetManager? budgetManager = null,
        ILogger<PromptAssembler>? logger = null)
    {
        _identityStore = identityStore;
        _nodeStore = nodeStore;
        _searchEngine = searchEngine;
        _budgetManager = budgetManager ?? new RetrievalBudgetManager();
        _logger = logger;
    }

    /// <summary>
    /// Assemble a context-aware system prompt for the given user message.
    /// Retrieves relevant wiki nodes based on the message content.
    /// </summary>
    public string AssemblePrompt(string userMessage)
    {
        var sb = new StringBuilder();

        // Core identity
        sb.Append("You are Engram, a personal semantic memory assistant. ");
        sb.Append("You help the user remember decisions, track goals, and recall their digital life. ");
        sb.Append("Be concise, direct, and personal. Refer to the user by name when natural.\n");

        // User profile context
        AppendUserContext(sb);

        // Anti-goals (hard constraints)
        AppendAntiGoals(sb);

        // Retrieval-augmented wiki context
        AppendRelevantWikiContext(sb, userMessage);

        // Timestamp
        sb.Append($"\nDate: {DateTime.Now:yyyy-MM-dd HH:mm}");

        var prompt = sb.ToString();
        _logger?.LogDebug("Assembled system prompt: {Length} chars", prompt.Length);
        return prompt;
    }

    /// <summary>
    /// Assemble prompt with explicit wiki context (for testing or manual override).
    /// </summary>
    public string AssemblePrompt(string userMessage, IEnumerable<WikiNode> contextNodes)
    {
        var sb = new StringBuilder();

        sb.Append("You are Engram, a personal semantic memory assistant. ");
        sb.Append("You help the user remember decisions, track goals, and recall their digital life. ");
        sb.Append("Be concise, direct, and personal. Refer to the user by name when natural.\n");

        AppendUserContext(sb);
        AppendAntiGoals(sb);

        // Use provided context nodes
        sb.Append("\nRelevant memory:\n");
        foreach (var node in contextNodes.Take(MaxWikiNodes))
        {
            sb.Append($"- [{node.NodeType}] {node.Title}: {node.Summary}");
            if (node.Facts.Count > 0)
            {
                var topFacts = node.Facts.OrderByDescending(f => f.LastConfirmedAt).Take(2);
                sb.Append($" ({string.Join("; ", topFacts.Select(f => f.Text))})");
            }
            sb.Append('\n');
        }

        sb.Append($"\nDate: {DateTime.Now:yyyy-MM-dd HH:mm}");

        return sb.ToString();
    }

    private void AppendUserContext(StringBuilder sb)
    {
        try
        {
            var profile = _identityStore.LoadProfile();
            if (profile == null) return;

            sb.Append($"\nUser: {profile.DisplayName}");

            if (profile.Goals?.Count > 0)
                sb.Append($"\nGoals: {string.Join("; ", profile.Goals.Take(5))}");

            if (profile.ComfortTriggers?.Count > 0)
                sb.Append($"\nPreferences: {string.Join("; ", profile.ComfortTriggers.Take(3))}");

            if (profile.RecurringAnxieties?.Count > 0)
                sb.Append($"\nConcerns: {string.Join("; ", profile.RecurringAnxieties.Take(3))}");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load user profile for prompt");
        }
    }

    private void AppendAntiGoals(StringBuilder sb)
    {
        try
        {
            var antiGoals = _identityStore.LoadAntiGoals();
            if (antiGoals?.Count > 0)
            {
                sb.Append($"\nAvoid: {string.Join("; ", antiGoals.Take(3).Select(a => a.Description))}");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load anti-goals for prompt");
        }
    }

    private void AppendRelevantWikiContext(StringBuilder sb, string userMessage)
    {
        try
        {
            // Step 1: Try semantic search for query-relevant nodes
            var searchResults = _searchEngine.Search(userMessage, MaxWikiNodes);
            var relevantNodes = searchResults.Results
                .Where(r => r.Relevance > 0.1)
                .Select(r => r.Node)
                .ToList();

            // Step 2: If search returns few results, supplement with high-salience nodes
            if (relevantNodes.Count < 3)
            {
                var salientNodes = _nodeStore.LoadAll()
                    .OrderByDescending(n => n.Salience)
                    .Take(MaxWikiNodes - relevantNodes.Count)
                    .Where(n => !relevantNodes.Any(r => r.NodeId == n.NodeId))
                    .ToList();

                relevantNodes.AddRange(salientNodes);
            }

            if (relevantNodes.Count == 0) return;

            sb.Append("\nRelevant memory:\n");
            foreach (var node in relevantNodes.Take(MaxWikiNodes))
            {
                sb.Append($"- [{node.NodeType}] {node.Title}: {Truncate(node.Summary, 100)}");
                if (node.Facts.Count > 0)
                {
                    var topFact = node.Facts.OrderByDescending(f => f.LastConfirmedAt).First();
                    sb.Append($" ({Truncate(topFact.Text, 80)})");
                }
                sb.Append('\n');
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to retrieve wiki context for prompt");

            // Fallback: use top salient nodes
            try
            {
                var fallbackNodes = _nodeStore.LoadAll()
                    .OrderByDescending(n => n.Salience)
                    .Take(5)
                    .ToList();

                if (fallbackNodes.Count > 0)
                {
                    sb.Append("\nRecent memory: ");
                    sb.Append(string.Join("; ", fallbackNodes.Select(n => $"{n.Title} ({n.NodeType})")));
                }
            }
            catch { }
        }
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}
