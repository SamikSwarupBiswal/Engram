using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Memory;

/// <summary>
/// Extracts structured memory candidates from chat conversations.
/// Uses regex + heuristic patterns — no LLM dependency.
/// Targets: PERSON, PROJECT, GOAL, DECISION, PREFERENCE, ANXIETY, TASK.
/// </summary>
public partial class ConversationMemoryExtractor
{
    private readonly ILogger<ConversationMemoryExtractor>? _logger;

    public ConversationMemoryExtractor(ILogger<ConversationMemoryExtractor>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Extract memory candidates from a conversation exchange.
    /// Call after each chat response with the user message and assistant response.
    /// </summary>
    public IReadOnlyList<ConversationMemoryCandidate> Extract(string userMessage, string assistantResponse)
    {
        var candidates = new List<ConversationMemoryCandidate>();

        if (string.IsNullOrWhiteSpace(userMessage))
            return candidates;

        var now = DateTimeOffset.UtcNow;

        // Extract from user message (primary source)
        candidates.AddRange(ExtractPersonMentions(userMessage, now));
        candidates.AddRange(ExtractProjectMentions(userMessage, now));
        candidates.AddRange(ExtractGoalMentions(userMessage, now));
        candidates.AddRange(ExtractDecisionMentions(userMessage, now));
        candidates.AddRange(ExtractPreferenceMentions(userMessage, now));
        candidates.AddRange(ExtractAnxietyMentions(userMessage, now));
        candidates.AddRange(ExtractTaskMentions(userMessage, now));

        // Also extract from assistant response (it may summarize user's statements)
        candidates.AddRange(ExtractProjectMentions(assistantResponse, now));
        candidates.AddRange(ExtractGoalMentions(assistantResponse, now));
        candidates.AddRange(ExtractDecisionMentions(assistantResponse, now));

        // Deduplicate by title
        var deduplicated = candidates
            .GroupBy(c => c.Title.ToLowerInvariant())
            .Select(g => g.OrderByDescending(c => c.Confidence).First())
            .ToList();

        _logger?.LogDebug("Extracted {Count} memory candidates from conversation", deduplicated.Count);
        return deduplicated;
    }

    // ── Person Extraction ──

    [GeneratedRegex(@"(?:my (?:friend|colleague|boss|manager|partner|wife|husband|brother|sister|mom|dad|mother|father|neighbor|roommate|teammate|classmate|mentor|student))\s+(\w+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PersonPattern();

    [GeneratedRegex(@"(?:meet(?:ing)?|call(?:ed)?|talk(?:ed)?|chat(?:ted)?)\s+(?:with\s+)?(\w+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PersonInteractionPattern();

    [GeneratedRegex(@"(\w+)\s+(?:told|said|mentioned|suggested|asked|recommended)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PersonQuotePattern();

    private IReadOnlyList<ConversationMemoryCandidate> ExtractPersonMentions(string text, DateTimeOffset now)
    {
        var results = new List<ConversationMemoryCandidate>();

        foreach (Match match in PersonPattern().Matches(text))
        {
            var name = match.Groups[1].Value;
            if (IsCommonWord(name)) continue;

            results.Add(new ConversationMemoryCandidate
            {
                MemoryType = MemoryType.Person,
                Title = name,
                Fact = $"Mentioned in conversation: {match.Value}",
                Confidence = 0.85,
                SourceMessage = Truncate(text, 200),
                SourceRole = "user",
                CapturedAt = now
            });
        }

        foreach (Match match in PersonInteractionPattern().Matches(text))
        {
            var name = match.Groups[1].Value;
            if (IsCommonWord(name)) continue;

            results.Add(new ConversationMemoryCandidate
            {
                MemoryType = MemoryType.Person,
                Title = name,
                Fact = $"Interaction mentioned: {match.Value}",
                Confidence = 0.75,
                SourceMessage = Truncate(text, 200),
                SourceRole = "user",
                CapturedAt = now
            });
        }

        return results;
    }

    // ── Project Extraction ──

    [GeneratedRegex(@"(?:i'?m\s+(?:building|developing|working\s+on|creating|designing|launching|coding|implementing))\s+(.+?)(?:\.|,|;|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ProjectBuildPattern();

    [GeneratedRegex(@"(?:my\s+project|the\s+project|our\s+project)\s+(.+?)(?:\.|,|;|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ProjectRefPattern();

    [GeneratedRegex(@"(?:project\s+(?:called|named)\s+)(\w+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ProjectNamePattern();

    private IReadOnlyList<ConversationMemoryCandidate> ExtractProjectMentions(string text, DateTimeOffset now)
    {
        var results = new List<ConversationMemoryCandidate>();

        foreach (Match match in ProjectBuildPattern().Matches(text))
        {
            var projectName = match.Groups[1].Value.Trim();
            if (projectName.Length < 2 || projectName.Length > 100) continue;

            results.Add(new ConversationMemoryCandidate
            {
                MemoryType = MemoryType.Project,
                Title = projectName,
                Fact = $"Project: {match.Value.Trim()}",
                Confidence = 0.9,
                SourceMessage = Truncate(text, 200),
                SourceRole = "user",
                CapturedAt = now
            });
        }

        foreach (Match match in ProjectRefPattern().Matches(text))
        {
            var projectName = match.Groups[1].Value.Trim();
            if (projectName.Length < 2 || projectName.Length > 100) continue;

            results.Add(new ConversationMemoryCandidate
            {
                MemoryType = MemoryType.Project,
                Title = projectName,
                Fact = $"Referenced: {match.Value.Trim()}",
                Confidence = 0.7,
                SourceMessage = Truncate(text, 200),
                SourceRole = "user",
                CapturedAt = now
            });
        }

        foreach (Match match in ProjectNamePattern().Matches(text))
        {
            var projectName = match.Groups[1].Value.Trim();
            results.Add(new ConversationMemoryCandidate
            {
                MemoryType = MemoryType.Project,
                Title = projectName,
                Fact = $"Project named: {projectName}",
                Confidence = 0.85,
                SourceMessage = Truncate(text, 200),
                SourceRole = "user",
                CapturedAt = now
            });
        }

        return results;
    }

    // ── Goal Extraction ──

    [GeneratedRegex(@"(?:i\s+want\s+to|i\s+wish\s+to|my\s+goal\s+is|i\s+aim\s+to|i\s+hope\s+to|i\s+plan\s+to)\s+(.+?)(?:\.|,|;|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex GoalPattern();

    [GeneratedRegex(@"(?:i'?m\s+(?:trying|striving|working)\s+to)\s+(.+?)(?:\.|,|;|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex GoalStrivingPattern();

    private IReadOnlyList<ConversationMemoryCandidate> ExtractGoalMentions(string text, DateTimeOffset now)
    {
        var results = new List<ConversationMemoryCandidate>();

        foreach (Match match in GoalPattern().Matches(text))
        {
            var goal = match.Groups[1].Value.Trim();
            if (goal.Length < 3 || goal.Length > 200) continue;

            results.Add(new ConversationMemoryCandidate
            {
                MemoryType = MemoryType.Goal,
                Title = Truncate(goal, 60),
                Fact = $"Goal: {goal}",
                Confidence = 0.85,
                SourceMessage = Truncate(text, 200),
                SourceRole = "user",
                CapturedAt = now
            });
        }

        foreach (Match match in GoalStrivingPattern().Matches(text))
        {
            var goal = match.Groups[1].Value.Trim();
            if (goal.Length < 3 || goal.Length > 200) continue;

            results.Add(new ConversationMemoryCandidate
            {
                MemoryType = MemoryType.Goal,
                Title = Truncate(goal, 60),
                Fact = $"Working towards: {goal}",
                Confidence = 0.8,
                SourceMessage = Truncate(text, 200),
                SourceRole = "user",
                CapturedAt = now
            });
        }

        return results;
    }

    // ── Decision Extraction ──

    [GeneratedRegex(@"(?:i\s+decided\s+to|i\s+chose\s+to|i'?m\s+going\s+with|i\s+picked|i\s+opted\s+(?:for|to))\s+(.+?)(?:\.|,|;|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DecisionPattern();

    [GeneratedRegex(@"(?:decided|chosen|picked|selected)\s+(?:to\s+)?(.+?)(?:\.|,|;|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DecisionPastPattern();

    private IReadOnlyList<ConversationMemoryCandidate> ExtractDecisionMentions(string text, DateTimeOffset now)
    {
        var results = new List<ConversationMemoryCandidate>();

        foreach (Match match in DecisionPattern().Matches(text))
        {
            var decision = match.Groups[1].Value.Trim();
            if (decision.Length < 3 || decision.Length > 200) continue;

            results.Add(new ConversationMemoryCandidate
            {
                MemoryType = MemoryType.Decision,
                Title = Truncate(decision, 60),
                Fact = $"Decision: {decision}",
                Confidence = 0.85,
                SourceMessage = Truncate(text, 200),
                SourceRole = "user",
                CapturedAt = now
            });
        }

        foreach (Match match in DecisionPastPattern().Matches(text))
        {
            var decision = match.Groups[1].Value.Trim();
            if (decision.Length < 3 || decision.Length > 200) continue;

            results.Add(new ConversationMemoryCandidate
            {
                MemoryType = MemoryType.Decision,
                Title = Truncate(decision, 60),
                Fact = $"Decision made: {decision}",
                Confidence = 0.7,
                SourceMessage = Truncate(text, 200),
                SourceRole = "user",
                CapturedAt = now
            });
        }

        return results;
    }

    // ── Preference Extraction ──

    [GeneratedRegex(@"(?:i\s+prefer|i\s+like|i\s+love|i\s+enjoy|i\s+favor|i'?m\s+(?:a\s+)?(?:big\s+)?fan\s+of)\s+(.+?)(?:\.|,|;|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PreferenceLikePattern();

    [GeneratedRegex(@"(?:i\s+don'?t\s+like|i\s+hate|i\s+dislike|i\s+can'?t\s+stand|i\s+avoid)\s+(.+?)(?:\.|,|;|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PreferenceDislikePattern();

    [GeneratedRegex(@"(?:i\s+prefer)\s+(.+?)(?:\.|,|;|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PreferencePreferPattern();

    private IReadOnlyList<ConversationMemoryCandidate> ExtractPreferenceMentions(string text, DateTimeOffset now)
    {
        var results = new List<ConversationMemoryCandidate>();

        foreach (Match match in PreferenceLikePattern().Matches(text))
        {
            var pref = match.Groups[1].Value.Trim();
            if (pref.Length < 2 || pref.Length > 150) continue;

            results.Add(new ConversationMemoryCandidate
            {
                MemoryType = MemoryType.Preference,
                Title = Truncate(pref, 60),
                Fact = $"Likes: {pref}",
                Confidence = 0.8,
                SourceMessage = Truncate(text, 200),
                SourceRole = "user",
                CapturedAt = now
            });
        }

        foreach (Match match in PreferenceDislikePattern().Matches(text))
        {
            var pref = match.Groups[1].Value.Trim();
            if (pref.Length < 2 || pref.Length > 150) continue;

            results.Add(new ConversationMemoryCandidate
            {
                MemoryType = MemoryType.Preference,
                Title = Truncate(pref, 60),
                Fact = $"Dislikes: {pref}",
                Confidence = 0.8,
                SourceMessage = Truncate(text, 200),
                SourceRole = "user",
                CapturedAt = now
            });
        }

        foreach (Match match in PreferencePreferPattern().Matches(text))
        {
            var pref = match.Groups[1].Value.Trim();
            if (pref.Length < 2 || pref.Length > 150) continue;

            results.Add(new ConversationMemoryCandidate
            {
                MemoryType = MemoryType.Preference,
                Title = Truncate(pref, 60),
                Fact = $"Prefers: {pref}",
                Confidence = 0.85,
                SourceMessage = Truncate(text, 200),
                SourceRole = "user",
                CapturedAt = now
            });
        }

        return results;
    }

    // ── Anxiety Extraction ──

    [GeneratedRegex(@"(?:i'?m\s+(?:worried|concerned|anxious|nervous|stressed|scared|afraid|frightened)\s+(?:about|of|that))\s+(.+?)(?:\.|,|;|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex AnxietyPattern();

    [GeneratedRegex(@"(?:what\s+if|i'?m\s+not\s+sure\s+(?:about|if)|i'?m\s+uncertain)\s+(.+?)(?:\.|,|;|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex AnxietyUncertainPattern();

    private IReadOnlyList<ConversationMemoryCandidate> ExtractAnxietyMentions(string text, DateTimeOffset now)
    {
        var results = new List<ConversationMemoryCandidate>();

        foreach (Match match in AnxietyPattern().Matches(text))
        {
            var anxiety = match.Groups[1].Value.Trim();
            if (anxiety.Length < 3 || anxiety.Length > 200) continue;

            results.Add(new ConversationMemoryCandidate
            {
                MemoryType = MemoryType.Anxiety,
                Title = Truncate(anxiety, 60),
                Fact = $"Anxiety: {anxiety}",
                Confidence = 0.85,
                SourceMessage = Truncate(text, 200),
                SourceRole = "user",
                CapturedAt = now
            });
        }

        foreach (Match match in AnxietyUncertainPattern().Matches(text))
        {
            var anxiety = match.Groups[1].Value.Trim();
            if (anxiety.Length < 3 || anxiety.Length > 200) continue;

            results.Add(new ConversationMemoryCandidate
            {
                MemoryType = MemoryType.Anxiety,
                Title = Truncate(anxiety, 60),
                Fact = $"Uncertainty: {anxiety}",
                Confidence = 0.6,
                SourceMessage = Truncate(text, 200),
                SourceRole = "user",
                CapturedAt = now
            });
        }

        return results;
    }

    // ── Task Extraction ──

    [GeneratedRegex(@"(?:i\s+(?:need|have)\s+to|i\s+(?:must|should)|i'?m\s+(?:going\s+to|planning\s+to|about\s+to))\s+(.+?)(?:\.|,|;|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex TaskPattern();

    [GeneratedRegex(@"(?:todo|to-do|task):\s*(.+?)(?:\.|,|;|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex TaskExplicitPattern();

    private IReadOnlyList<ConversationMemoryCandidate> ExtractTaskMentions(string text, DateTimeOffset now)
    {
        var results = new List<ConversationMemoryCandidate>();

        foreach (Match match in TaskPattern().Matches(text))
        {
            var task = match.Groups[1].Value.Trim();
            if (task.Length < 3 || task.Length > 200) continue;

            results.Add(new ConversationMemoryCandidate
            {
                MemoryType = MemoryType.Task,
                Title = Truncate(task, 60),
                Fact = $"Task: {task}",
                Confidence = 0.75,
                SourceMessage = Truncate(text, 200),
                SourceRole = "user",
                CapturedAt = now
            });
        }

        foreach (Match match in TaskExplicitPattern().Matches(text))
        {
            var task = match.Groups[1].Value.Trim();
            if (task.Length < 3 || task.Length > 200) continue;

            results.Add(new ConversationMemoryCandidate
            {
                MemoryType = MemoryType.Task,
                Title = Truncate(task, 60),
                Fact = $"Explicit task: {task}",
                Confidence = 0.9,
                SourceMessage = Truncate(text, 200),
                SourceRole = "user",
                CapturedAt = now
            });
        }

        return results;
    }

    // ── Helpers ──

    private static bool IsCommonWord(string word)
    {
        var common = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "this", "that", "these", "those", "it", "its",
            "he", "she", "they", "we", "you", "me", "him", "her", "them", "us",
            "my", "your", "his", "our", "their", "what", "which", "who", "whom",
            "here", "there", "where", "when", "how", "why", "all", "each", "every",
            "both", "few", "more", "most", "other", "some", "such", "no", "not",
            "only", "own", "same", "so", "than", "too", "very", "just", "because",
            "but", "and", "or", "if", "while", "about", "above", "after", "before",
            "between", "into", "through", "during", "until", "against", "with",
            "from", "for", "on", "off", "over", "under", "again", "further",
            "then", "once", "also", "still", "already", "well", "back", "even",
            "now", "new", "old", "first", "last", "long", "great", "little",
            "own", "right", "big", "high", "different", "small", "large",
            "next", "early", "young", "important", "public", "bad", "same"
        };
        return common.Contains(word) || word.Length <= 2;
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}
