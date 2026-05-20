using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Orchestration;

/// <summary>
/// Classifies user intent from natural language messages.
/// This is the first stage of the task routing pipeline.
/// 
/// The chat is NOT a generic conversation — it's the intent interface
/// into the semantic operating system. This classifier determines
/// which subsystem should handle each user message.
/// </summary>
public partial class IntentClassifier
{
    private readonly ILogger<IntentClassifier>? _logger;

    public IntentClassifier(ILogger<IntentClassifier>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Classify a user message into an intent category.
    /// Returns the intent type, confidence, and extracted entities.
    /// </summary>
    public IntentResult Classify(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return new IntentResult { Intent = IntentType.Conversational, Confidence = 0.5 };

        var message = userMessage.Trim();
        var lower = message.ToLowerInvariant();

        // Score each intent type
        var scores = new Dictionary<IntentType, double>();

        scores[IntentType.MemoryQuery] = ScoreMemoryQuery(lower, message);
        scores[IntentType.TimelineQuery] = ScoreTimelineQuery(lower, message);
        scores[IntentType.DriftAnalysis] = ScoreDriftAnalysis(lower, message);
        scores[IntentType.ResearchTask] = ScoreResearchTask(lower, message);
        scores[IntentType.AutomationTask] = ScoreAutomationTask(lower, message);
        scores[IntentType.StateSynthesis] = ScoreStateSynthesis(lower, message);
        scores[IntentType.Conversational] = 0.3; // Base score for conversational

        // Find the highest scoring intent
        var best = scores.OrderByDescending(kv => kv.Value).First();
        var confidence = Math.Min(1.0, best.Value);

        // If confidence is too low, fall back to conversational
        if (confidence < 0.4)
        {
            best = new KeyValuePair<IntentType, double>(IntentType.Conversational, 0.5);
            confidence = 0.5;
        }

        var result = new IntentResult
        {
            Intent = best.Key,
            Confidence = confidence,
            OriginalMessage = message,
            ExtractedEntities = ExtractEntities(message, best.Key)
        };

        _logger?.LogDebug("Intent classified: {Intent} ({Confidence:F2}) from '{Message}'",
            result.Intent, result.Confidence, Truncate(message, 50));

        return result;
    }

    // ── Memory Query Scoring ──
    // "What do you know about...", "Remember when...", "Tell me about..."

    [GeneratedRegex(@"(?:what do you know|what do you remember|tell me about|recall|what is|what are|who is|who are|explain|describe)\s+(?:about\s+)?(.+?)(?:\?|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex MemoryQueryPattern();

    [GeneratedRegex(@"(?:remember when|do you remember|you mentioned|we discussed|we talked about)\s+(.+?)(?:\?|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex MemoryRecallPattern();

    private double ScoreMemoryQuery(string lower, string original)
    {
        double score = 0;

        // Direct memory query patterns
        if (MemoryQueryPattern().IsMatch(original)) score += 0.7;
        if (MemoryRecallPattern().IsMatch(original)) score += 0.8;

        // Keyword signals
        if (lower.Contains("know about") || lower.Contains("remember")) score += 0.5;
        if (lower.Contains("what do you") || lower.Contains("who is")) score += 0.3;
        if (lower.Contains("tell me about") || lower.Contains("explain")) score += 0.4;
        if (lower.Contains("recall") || lower.Contains("mentioned")) score += 0.5;

        // Entity references (proper nouns, project names)
        if (ContainsEntityReference(lower)) score += 0.2;

        return Math.Min(1.0, score);
    }

    // ── Timeline Query Scoring ──
    // "What was I doing...", "Show me my activity...", "What happened..."

    [GeneratedRegex(@"(?:what was i doing|what have i been|what did i do|show me my|my activity|my history)\s+(.+?)(?:\?|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex TimelineQueryPattern();

    [GeneratedRegex(@"(?:what happened|what changed|what's new|recent|lately|today|yesterday|this week|last week)\s*(.+)?(?:\?|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex TimelineEventPattern();

    private double ScoreTimelineQuery(string lower, string original)
    {
        double score = 0;

        if (TimelineQueryPattern().IsMatch(original)) score += 0.8;
        if (TimelineEventPattern().IsMatch(original)) score += 0.6;

        // Time-related keywords
        if (lower.Contains("what was i doing") || lower.Contains("what have i been")) score += 0.7;
        if (lower.Contains("activity") || lower.Contains("history")) score += 0.5;
        if (lower.Contains("today") || lower.Contains("yesterday")) score += 0.4;
        if (lower.Contains("this week") || lower.Contains("last week")) score += 0.4;
        if (lower.Contains("recent") || lower.Contains("lately")) score += 0.4;
        if (lower.Contains("what happened") || lower.Contains("what changed")) score += 0.5;

        return Math.Min(1.0, score);
    }

    // ── Drift Analysis Scoring ──
    // "Am I making progress?", "Why am I stuck?", "What's blocking me?"

    [GeneratedRegex(@"(?:am i making progress|am i stuck|why am i not|what's blocking|what's holding|am i on track|how am i doing)\s*(.+)?(?:\?|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DriftAnalysisPattern();

    private double ScoreDriftAnalysis(string lower, string original)
    {
        double score = 0;

        if (DriftAnalysisPattern().IsMatch(original)) score += 0.8;

        // Progress/drift keywords
        if (lower.Contains("progress") || lower.Contains("stuck")) score += 0.6;
        if (lower.Contains("blocking") || lower.Contains("holding me back")) score += 0.7;
        if (lower.Contains("on track") || lower.Contains("falling behind")) score += 0.6;
        if (lower.Contains("drift") || lower.Contains("contradiction")) score += 0.7;
        if (lower.Contains("why am i not") || lower.Contains("why can't i")) score += 0.5;
        if (lower.Contains("procrastinating") || lower.Contains("avoiding")) score += 0.6;

        return Math.Min(1.0, score);
    }

    // ── Research Task Scoring ──
    // "Find the best...", "Research...", "Look up...", "Compare..."

    [GeneratedRegex(@"(?:find|search|research|look up|compare|investigate|analyze|review)\s+(?:the\s+)?(?:best|top|cheapest|fastest|most)?\s*(.+?)(?:\?|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ResearchTaskPattern();

    [GeneratedRegex(@"(?:what is the best|what are the best|recommend|suggest|which should i)\s+(.+?)(?:\?|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ResearchRecommendPattern();

    private double ScoreResearchTask(string lower, string original)
    {
        double score = 0;

        if (ResearchTaskPattern().IsMatch(original)) score += 0.7;
        if (ResearchRecommendPattern().IsMatch(original)) score += 0.7;

        // Research keywords
        if (lower.Contains("research") || lower.Contains("find the best")) score += 0.7;
        if (lower.Contains("compare") || lower.Contains("look up")) score += 0.6;
        if (lower.Contains("investigate") || lower.Contains("analyze")) score += 0.5;
        if (lower.Contains("what is the best") || lower.Contains("recommend")) score += 0.6;
        if (lower.Contains("summarize") || lower.Contains("review")) score += 0.4;

        // Specific research domains
        if (lower.Contains("gpu") || lower.Contains("cpu") || lower.Contains("laptop")) score += 0.3;
        if (lower.Contains("framework") || lower.Contains("library") || lower.Contains("tool")) score += 0.3;

        return Math.Min(1.0, score);
    }

    // ── Automation Task Scoring ──
    // "Open...", "Run...", "Create...", "Execute...", "Deploy..."

    [GeneratedRegex(@"(?:open|run|launch|start|execute|create|deploy|build|install|setup|configure|send|write|delete|move|copy|rename)\s+(.+?)(?:\?|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex AutomationTaskPattern();

    [GeneratedRegex(@"(?:can you|please|go ahead)\s+(?:open|run|launch|start|execute|create|deploy|build|install|setup|configure|send|write|delete|move|copy|rename)\s+(.+?)(?:\?|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex AutomationPolitePattern();

    private double ScoreAutomationTask(string lower, string original)
    {
        double score = 0;

        if (AutomationTaskPattern().IsMatch(original)) score += 0.6;
        if (AutomationPolitePattern().IsMatch(original)) score += 0.7;

        // Automation keywords
        if (lower.StartsWith("open ") || lower.StartsWith("run ")) score += 0.7;
        if (lower.StartsWith("create ") || lower.StartsWith("deploy ")) score += 0.6;
        if (lower.StartsWith("install ") || lower.StartsWith("setup ")) score += 0.6;
        if (lower.StartsWith("send ") || lower.StartsWith("write ")) score += 0.5;
        if (lower.Contains("github") && (lower.Contains("repo") || lower.Contains("pr"))) score += 0.6;
        if (lower.Contains("automate") || lower.Contains("script")) score += 0.5;

        // Imperative mood detection (starts with verb)
        var firstWord = lower.Split(' ').FirstOrDefault() ?? "";
        var imperativeVerbs = new HashSet<string> { "open", "run", "launch", "start", "execute", "create", "deploy", "build", "install", "setup", "configure", "send", "write", "delete", "move", "copy", "rename", "make", "set", "get", "fetch", "download", "upload", "push", "pull", "commit", "merge" };
        if (imperativeVerbs.Contains(firstWord)) score += 0.5;

        return Math.Min(1.0, score);
    }

    // ── State Synthesis Scoring ──
    // "What projects matter most?", "What should I focus on?", "What's my priority?"

    [GeneratedRegex(@"(?:what projects|what matters|what should i focus|what's my priority|what's important|what needs attention)\s*(.+)?(?:\?|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex StateSynthesisPattern();

    private double ScoreStateSynthesis(string lower, string original)
    {
        double score = 0;

        if (StateSynthesisPattern().IsMatch(original)) score += 0.8;

        // Synthesis keywords
        if (lower.Contains("what matters") || lower.Contains("what's important")) score += 0.7;
        if (lower.Contains("focus on") || lower.Contains("priority")) score += 0.6;
        if (lower.Contains("what should i") || lower.Contains("what needs")) score += 0.5;
        if (lower.Contains("overview") || lower.Contains("summary of my")) score += 0.5;
        if (lower.Contains("status") || lower.Contains("where do i stand")) score += 0.5;

        return Math.Min(1.0, score);
    }

    // ── Entity Extraction ──

    private static Dictionary<string, string> ExtractEntities(string message, IntentType intent)
    {
        var entities = new Dictionary<string, string>();

        switch (intent)
        {
            case IntentType.MemoryQuery:
                var memMatch = MemoryQueryPattern().Match(message);
                if (memMatch.Success && memMatch.Groups.Count > 1)
                    entities["query_subject"] = memMatch.Groups[1].Value.Trim();
                break;

            case IntentType.TimelineQuery:
                var timeMatch = TimelineQueryPattern().Match(message);
                if (timeMatch.Success && timeMatch.Groups.Count > 1)
                    entities["time_range"] = timeMatch.Groups[1].Value.Trim();
                break;

            case IntentType.ResearchTask:
                var resMatch = ResearchTaskPattern().Match(message);
                if (resMatch.Success && resMatch.Groups.Count > 1)
                    entities["research_topic"] = resMatch.Groups[1].Value.Trim();
                break;

            case IntentType.AutomationTask:
                var autoMatch = AutomationTaskPattern().Match(message);
                if (autoMatch.Success && autoMatch.Groups.Count > 1)
                    entities["automation_target"] = autoMatch.Groups[1].Value.Trim();
                break;
        }

        return entities;
    }

    private static bool ContainsEntityReference(string lower)
    {
        // Check for proper noun patterns or known entity names
        return lower.Contains("engram") || lower.Contains("samik") ||
               lower.Contains("phi") || lower.Contains("llama") ||
               lower.Contains("vulkan") || lower.Contains(".net");
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}

/// <summary>
/// The type of intent detected in the user's message.
/// Each intent type routes to a different subsystem.
/// </summary>
public enum IntentType
{
    /// <summary>User wants to retrieve stored memory/knowledge.</summary>
    MemoryQuery,

    /// <summary>User wants to see their activity history.</summary>
    TimelineQuery,

    /// <summary>User wants analysis of progress/drift/contradictions.</summary>
    DriftAnalysis,

    /// <summary>User wants research conducted on a topic.</summary>
    ResearchTask,

    /// <summary>User wants an action executed (open, run, create, etc.).</summary>
    AutomationTask,

    /// <summary>User wants synthesis of their current state/priorities.</summary>
    StateSynthesis,

    /// <summary>General conversation — no specific subsystem needed.</summary>
    Conversational
}

/// <summary>
/// Result of intent classification.
/// Contains the detected intent, confidence, and extracted entities.
/// </summary>
public class IntentResult
{
    /// <summary>The detected intent type.</summary>
    public IntentType Intent { get; set; }

    /// <summary>Confidence in the classification (0.0 to 1.0).</summary>
    public double Confidence { get; set; }

    /// <summary>The original user message.</summary>
    public string OriginalMessage { get; set; } = string.Empty;

    /// <summary>Extracted entities relevant to the intent.</summary>
    public Dictionary<string, string> ExtractedEntities { get; set; } = new();
}
