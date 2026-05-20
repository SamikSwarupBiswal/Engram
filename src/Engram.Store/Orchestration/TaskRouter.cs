using Engram.Store.Events;
using Engram.Store.Memory;
using Engram.Store.Search;
using Engram.Store.Salience;
using Engram.Store.Wiki;
using Engram.Store.Identity;
using Engram.Store.Agent;
using Engram.Store.Automation;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Orchestration;

/// <summary>
/// Central task router — the nervous system of Engram.
/// 
/// Takes classified intents and routes them to the appropriate subsystem,
/// assembles context, and returns structured responses.
/// 
/// This is what transforms the chat from a generic chatbot into
/// the command interface for the semantic operating system.
/// </summary>
public class TaskRouter
{
    private readonly IntentClassifier _intentClassifier;
    private readonly SemanticSearchEngine _searchEngine;
    private readonly WikiNodeStore _nodeStore;
    private readonly PromptAssembler _promptAssembler;
    private readonly IdentityStore _identityStore;
    private readonly SalienceScorer _salienceScorer;
    private readonly DriftDetector _driftDetector;
    private readonly IEventBus? _eventBus;
    private readonly ILogger<TaskRouter>? _logger;

    public TaskRouter(
        IntentClassifier intentClassifier,
        SemanticSearchEngine searchEngine,
        WikiNodeStore nodeStore,
        PromptAssembler promptAssembler,
        IdentityStore identityStore,
        SalienceScorer salienceScorer,
        DriftDetector driftDetector,
        IEventBus? eventBus = null,
        ILogger<TaskRouter>? logger = null)
    {
        _intentClassifier = intentClassifier;
        _searchEngine = searchEngine;
        _nodeStore = nodeStore;
        _promptAssembler = promptAssembler;
        _identityStore = identityStore;
        _salienceScorer = salienceScorer;
        _driftDetector = driftDetector;
        _eventBus = eventBus;
        _logger = logger;
    }

    /// <summary>
    /// Route a user message through the appropriate subsystem pipeline.
    /// Returns a TaskResult with the response and any side effects.
    /// </summary>
    public async Task<TaskResult> RouteAsync(string userMessage, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Step 1: Classify intent
        var intent = _intentClassifier.Classify(userMessage);

        _logger?.LogInformation("Intent: {Intent} ({Confidence:F2}) from '{Message}'",
            intent.Intent, intent.Confidence, Truncate(userMessage, 50));

        // Step 2: Route to appropriate handler
        TaskResult result;
        try
        {
            result = intent.Intent switch
            {
                IntentType.MemoryQuery => await HandleMemoryQuery(intent, ct),
                IntentType.TimelineQuery => await HandleTimelineQuery(intent, ct),
                IntentType.DriftAnalysis => await HandleDriftAnalysis(intent, ct),
                IntentType.StateSynthesis => await HandleStateSynthesis(intent, ct),
                IntentType.ResearchTask => await HandleResearchTask(intent, ct),
                IntentType.AutomationTask => await HandleAutomationTask(intent, ct),
                IntentType.Conversational => await HandleConversational(intent, ct),
                _ => await HandleConversational(intent, ct)
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Task routing failed for intent {Intent}", intent.Intent);
            result = new TaskResult
            {
                Success = false,
                ErrorMessage = $"Task routing failed: {ex.Message}",
                Intent = intent.Intent
            };
        }

        result.Duration = sw.Elapsed;
        result.Intent = intent.Intent;
        result.IntentConfidence = intent.Confidence;

        // Step 3: Publish event
        _eventBus?.Publish(new EventEnvelope
        {
            EventType = "task.routed",
            Source = "task_router",
            Payload = new
            {
                Intent = intent.Intent.ToString(),
                Confidence = intent.Confidence,
                Success = result.Success,
                Duration = result.Duration.TotalMilliseconds
            }
        });

        return result;
    }

    /// <summary>
    /// Get a system prompt tailored to the detected intent.
    /// This is where the LLM stops being generic and starts being Engram.
    /// </summary>
    public string GetContextualSystemPrompt(IntentResult intent)
    {
        return intent.Intent switch
        {
            IntentType.MemoryQuery => BuildMemoryQueryPrompt(intent),
            IntentType.TimelineQuery => BuildTimelineQueryPrompt(intent),
            IntentType.DriftAnalysis => BuildDriftAnalysisPrompt(intent),
            IntentType.StateSynthesis => BuildStateSynthesisPrompt(intent),
            IntentType.ResearchTask => BuildResearchTaskPrompt(intent),
            IntentType.AutomationTask => BuildAutomationTaskPrompt(intent),
            IntentType.Conversational => _promptAssembler.AssemblePrompt(intent.OriginalMessage),
            _ => _promptAssembler.AssemblePrompt(intent.OriginalMessage)
        };
    }

    // ── Intent Handlers ──

    private async Task<TaskResult> HandleMemoryQuery(IntentResult intent, CancellationToken ct)
    {
        var subject = intent.ExtractedEntities.GetValueOrDefault("query_subject", intent.OriginalMessage);

        // Search wiki for relevant nodes
        var searchResults = _searchEngine.Search(subject, 10);
        var relevantNodes = searchResults.Results
            .Where(r => r.Relevance > 0.1)
            .Select(r => r.Node)
            .ToList();

        // Build context from retrieved nodes
        var contextBuilder = new System.Text.StringBuilder();
        contextBuilder.AppendLine("You are Engram, querying your memory graph.");
        contextBuilder.AppendLine($"The user asks: {intent.OriginalMessage}");
        contextBuilder.AppendLine();
        contextBuilder.AppendLine("Relevant memory nodes:");

        foreach (var node in relevantNodes.Take(8))
        {
            contextBuilder.AppendLine($"- [{node.NodeType}] {node.Title}: {node.Summary}");
            foreach (var fact in node.Facts.Take(3))
            {
                contextBuilder.AppendLine($"  • {fact.Text}");
            }
        }

        if (relevantNodes.Count == 0)
        {
            contextBuilder.AppendLine("No specific memory nodes found. Answer from general knowledge if appropriate, but acknowledge the gap.");
        }

        return new TaskResult
        {
            Success = true,
            SystemPrompt = contextBuilder.ToString(),
            RetrievedNodes = relevantNodes.Select(n => n.NodeId).ToList(),
            ResponseHint = "Synthesize a contextual answer from the retrieved memory nodes."
        };
    }

    private async Task<TaskResult> HandleTimelineQuery(IntentResult intent, CancellationToken ct)
    {
        // Get user's recent activity from timeline
        var profile = _identityStore.LoadProfile();
        var nodes = _nodeStore.LoadAll()
            .OrderByDescending(n => n.LastTouchedAt)
            .Take(15)
            .ToList();

        var contextBuilder = new System.Text.StringBuilder();
        contextBuilder.AppendLine("You are Engram, analyzing the user's recent activity timeline.");
        contextBuilder.AppendLine($"The user asks: {intent.OriginalMessage}");
        contextBuilder.AppendLine();
        contextBuilder.AppendLine("Recent activity (ordered by recency):");

        foreach (var node in nodes)
        {
            var age = DateTimeOffset.UtcNow - node.LastTouchedAt;
            var ageStr = age.TotalDays >= 1 ? $"{age.TotalDays:F0}d ago" : $"{age.TotalHours:F0}h ago";
            contextBuilder.AppendLine($"- [{node.NodeType}] {node.Title} (last touched: {ageStr}, salience: {node.Salience:F2})");
        }

        return new TaskResult
        {
            Success = true,
            SystemPrompt = contextBuilder.ToString(),
            RetrievedNodes = nodes.Select(n => n.NodeId).ToList(),
            ResponseHint = "Synthesize a timeline summary from the activity data."
        };
    }

    private async Task<TaskResult> HandleDriftAnalysis(IntentResult intent, CancellationToken ct)
    {
        // Get goals vs observed behavior
        var profile = _identityStore.LoadProfile();
        var goals = profile?.Goals ?? new List<string>();
        var nodes = _nodeStore.LoadAll();

        // Find nodes with low salience that were once important
        var staleNodes = nodes
            .Where(n => n.Salience < 0.3 && n.NodeType == WikiNodeType.Goal)
            .OrderBy(n => n.Salience)
            .ToList();

        // Find high-salience activity nodes
        var activeNodes = nodes
            .Where(n => n.Salience > 0.7)
            .OrderByDescending(n => n.Salience)
            .Take(10)
            .ToList();

        var contextBuilder = new System.Text.StringBuilder();
        contextBuilder.AppendLine("You are Engram, performing drift analysis.");
        contextBuilder.AppendLine($"The user asks: {intent.OriginalMessage}");
        contextBuilder.AppendLine();
        contextBuilder.AppendLine("=== DECLARED GOALS ===");
        foreach (var goal in goals)
        {
            contextBuilder.AppendLine($"- {goal}");
        }

        contextBuilder.AppendLine();
        contextBuilder.AppendLine("=== STALE GOALS (low salience — potentially abandoned) ===");
        foreach (var node in staleNodes)
        {
            contextBuilder.AppendLine($"- [{node.NodeType}] {node.Title} (salience: {node.Salience:F2})");
        }

        contextBuilder.AppendLine();
        contextBuilder.AppendLine("=== HIGH ACTIVITY (what the user is actually focused on) ===");
        foreach (var node in activeNodes)
        {
            contextBuilder.AppendLine($"- [{node.NodeType}] {node.Title} (salience: {node.Salience:F2})");
        }

        contextBuilder.AppendLine();
        contextBuilder.AppendLine("Analyze the gap between declared goals and observed behavior. Be honest but constructive.");

        return new TaskResult
        {
            Success = true,
            SystemPrompt = contextBuilder.ToString(),
            ResponseHint = "Provide an honest drift analysis comparing goals to observed activity."
        };
    }

    private async Task<TaskResult> HandleStateSynthesis(IntentResult intent, CancellationToken ct)
    {
        // Synthesize current state from all sources
        var profile = _identityStore.LoadProfile();
        var nodes = _nodeStore.LoadAll();
        var priorities = _identityStore.LoadPriorities();

        // Group by type and salience
        var projects = nodes.Where(n => n.NodeType == WikiNodeType.Project).OrderByDescending(n => n.Salience).ToList();
        var goals = nodes.Where(n => n.NodeType == WikiNodeType.Goal).OrderByDescending(n => n.Salience).ToList();
        var people = nodes.Where(n => n.NodeType == WikiNodeType.Person).OrderByDescending(n => n.Salience).ToList();

        var contextBuilder = new System.Text.StringBuilder();
        contextBuilder.AppendLine("You are Engram, synthesizing the user's current state.");
        contextBuilder.AppendLine($"The user asks: {intent.OriginalMessage}");
        contextBuilder.AppendLine();
        contextBuilder.AppendLine("=== ACTIVE PROJECTS ===");
        foreach (var p in projects.Take(5))
        {
            contextBuilder.AppendLine($"- {p.Title}: {p.Summary} (salience: {p.Salience:F2})");
        }

        contextBuilder.AppendLine();
        contextBuilder.AppendLine("=== ACTIVE GOALS ===");
        foreach (var g in goals.Take(5))
        {
            contextBuilder.AppendLine($"- {g.Title}: {g.Summary} (salience: {g.Salience:F2})");
        }

        if (priorities.Count > 0)
        {
            contextBuilder.AppendLine();
            contextBuilder.AppendLine("=== DECLARED PRIORITIES ===");
            foreach (var p in priorities.Take(5))
            {
                contextBuilder.AppendLine($"- {p.Description} (confidence: {p.Confidence:F1})");
            }
        }

        contextBuilder.AppendLine();
        contextBuilder.AppendLine("Provide a synthesized overview of what matters most right now.");

        return new TaskResult
        {
            Success = true,
            SystemPrompt = contextBuilder.ToString(),
            ResponseHint = "Synthesize a prioritized overview of the user's current state."
        };
    }

    private async Task<TaskResult> HandleResearchTask(IntentResult intent, CancellationToken ct)
    {
        var topic = intent.ExtractedEntities.GetValueOrDefault("research_topic", intent.OriginalMessage);

        // Search existing knowledge first
        var searchResults = _searchEngine.Search(topic, 5);
        var existingNodes = searchResults.Results
            .Where(r => r.Relevance > 0.2)
            .Select(r => r.Node)
            .ToList();

        var contextBuilder = new System.Text.StringBuilder();
        contextBuilder.AppendLine("You are Engram's research interface.");
        contextBuilder.AppendLine($"The user requests: {intent.OriginalMessage}");
        contextBuilder.AppendLine();
        contextBuilder.AppendLine("=== EXISTING KNOWLEDGE ===");
        if (existingNodes.Count > 0)
        {
            foreach (var node in existingNodes)
            {
                contextBuilder.AppendLine($"- [{node.NodeType}] {node.Title}: {node.Summary}");
            }
        }
        else
        {
            contextBuilder.AppendLine("No existing knowledge found on this topic.");
        }

        contextBuilder.AppendLine();
        contextBuilder.AppendLine("Provide a research summary. If you need to look up current information, acknowledge that and suggest what to research.");

        return new TaskResult
        {
            Success = true,
            SystemPrompt = contextBuilder.ToString(),
            RetrievedNodes = existingNodes.Select(n => n.NodeId).ToList(),
            ResponseHint = "Provide a research summary based on existing knowledge and general expertise."
        };
    }

    private async Task<TaskResult> HandleAutomationTask(IntentResult intent, CancellationToken ct)
    {
        var target = intent.ExtractedEntities.GetValueOrDefault("automation_target", intent.OriginalMessage);

        var contextBuilder = new System.Text.StringBuilder();
        contextBuilder.AppendLine("You are Engram's automation interface.");
        contextBuilder.AppendLine($"The user requests: {intent.OriginalMessage}");
        contextBuilder.AppendLine();
        contextBuilder.AppendLine("This is an automation request. Provide a clear, actionable response.");
        contextBuilder.AppendLine("If the action can be performed, describe what you would do.");
        contextBuilder.AppendLine("If verification is needed, explain what to check.");

        return new TaskResult
        {
            Success = true,
            SystemPrompt = contextBuilder.ToString(),
            ResponseHint = "Provide an actionable automation response."
        };
    }

    private async Task<TaskResult> HandleConversational(IntentResult intent, CancellationToken ct)
    {
        // Use the standard prompt assembler for general conversation
        var systemPrompt = _promptAssembler.AssemblePrompt(intent.OriginalMessage);

        return new TaskResult
        {
            Success = true,
            SystemPrompt = systemPrompt,
            ResponseHint = "Respond conversationally with Engram's personality."
        };
    }

    // ── Prompt Builders ──

    private string BuildMemoryQueryPrompt(IntentResult intent)
    {
        return _promptAssembler.AssemblePrompt(intent.OriginalMessage);
    }

    private string BuildTimelineQueryPrompt(IntentResult intent)
    {
        return _promptAssembler.AssemblePrompt(intent.OriginalMessage);
    }

    private string BuildDriftAnalysisPrompt(IntentResult intent)
    {
        return _promptAssembler.AssemblePrompt(intent.OriginalMessage);
    }

    private string BuildStateSynthesisPrompt(IntentResult intent)
    {
        return _promptAssembler.AssemblePrompt(intent.OriginalMessage);
    }

    private string BuildResearchTaskPrompt(IntentResult intent)
    {
        return _promptAssembler.AssemblePrompt(intent.OriginalMessage);
    }

    private string BuildAutomationTaskPrompt(IntentResult intent)
    {
        return _promptAssembler.AssemblePrompt(intent.OriginalMessage);
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}

/// <summary>
/// Result of task routing.
/// Contains the response, system prompt, and any side effects.
/// </summary>
public class TaskResult
{
    /// <summary>Whether the routing succeeded.</summary>
    public bool Success { get; set; }

    /// <summary>The intent type that was routed.</summary>
    public IntentType Intent { get; set; }

    /// <summary>Confidence in the intent classification.</summary>
    public double IntentConfidence { get; set; }

    /// <summary>
    /// The system prompt assembled for this specific intent.
    /// This is what gets injected before the LLM reasoning.
    /// </summary>
    public string SystemPrompt { get; set; } = string.Empty;

    /// <summary>
    /// Hint for the LLM about how to respond.
    /// </summary>
    public string ResponseHint { get; set; } = string.Empty;

    /// <summary>
    /// IDs of wiki nodes that were retrieved for context.
    /// </summary>
    public List<string> RetrievedNodes { get; set; } = new();

    /// <summary>Error message if routing failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>How long the routing took.</summary>
    public TimeSpan Duration { get; set; }
}
