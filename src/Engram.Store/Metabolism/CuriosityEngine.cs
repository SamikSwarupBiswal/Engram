using Engram.Store.Wiki;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Metabolism;

/// <summary>
/// Curiosity layer — the organism must explore, ask, wonder, hypothesize.
/// 
/// NOT only:
/// - diagnose
/// - intervene
/// - correct
/// 
/// The organism needs gentle curiosity:
/// - "I noticed you've been working on X. What's that about?"
/// - "You mentioned Y a while ago. Still interested?"
/// - "I see a pattern in your work. Curious about it?"
/// 
/// This makes the organism feel like a companion, not a critic.
/// </summary>
public class CuriosityEngine
{
    private readonly WikiNodeStore _nodeStore;
    private readonly ContradictionHistoryStore _historyStore;
    private readonly MomentumDetector _momentumDetector;
    private readonly ILogger<CuriosityEngine>? _logger;

    /// <summary>Maximum curiosity prompts per cycle.</summary>
    public int MaxCuriosityPrompts { get; set; } = 2;

    public CuriosityEngine(
        WikiNodeStore nodeStore,
        ContradictionHistoryStore historyStore,
        MomentumDetector momentumDetector,
        ILogger<CuriosityEngine>? logger = null)
    {
        _nodeStore = nodeStore;
        _historyStore = historyStore;
        _momentumDetector = momentumDetector;
        _logger = logger;
    }

    /// <summary>
    /// Generate curiosity prompts based on current state.
    /// </summary>
    public List<CuriosityPrompt> GenerateCuriosityPrompts()
    {
        var prompts = new List<CuriosityPrompt>();

        prompts.AddRange(ExploreNewActivity());
        prompts.AddRange(RevisitOldInterests());
        prompts.AddRange(HypothesizePatterns());
        prompts.AddRange(CelebrateMomentum());

        // Take top prompts by relevance
        return prompts
            .OrderByDescending(p => p.Relevance)
            .Take(MaxCuriosityPrompts)
            .ToList();
    }

    /// <summary>
    /// Explore new activity — ask about recent work.
    /// </summary>
    private List<CuriosityPrompt> ExploreNewActivity()
    {
        var prompts = new List<CuriosityPrompt>();
        var nodes = _nodeStore.LoadAll();

        // Find recently created nodes (new activity)
        var recentNodes = nodes.Where(n =>
            (DateTimeOffset.UtcNow - n.CreatedAt).TotalDays < 2 &&
            n.NodeType != WikiNodeType.Concept)
            .ToList();

        foreach (var node in recentNodes.Take(2))
        {
            prompts.Add(new CuriosityPrompt
            {
                Type = CuriosityType.Exploration,
                Prompt = $"I noticed you've been working on '{node.Title}'. What's that about?",
                Relevance = 0.8,
                SourceNodeId = node.NodeId,
                Tone = CuriosityTone.Interested
            });
        }

        return prompts;
    }

    /// <summary>
    /// Revisit old interests — gentle reminder of past goals.
    /// </summary>
    private List<CuriosityPrompt> RevisitOldInterests()
    {
        var prompts = new List<CuriosityPrompt>();
        var nodes = _nodeStore.LoadAll();

        // Find goals that haven't been touched in a while (but not abandoned)
        var oldGoals = nodes.Where(n =>
            n.NodeType == WikiNodeType.Goal &&
            n.Salience > 0.2 && // Still relevant
            (DateTimeOffset.UtcNow - n.LastTouchedAt).TotalDays > 14 &&
            (DateTimeOffset.UtcNow - n.LastTouchedAt).TotalDays < 60)
            .ToList();

        foreach (var goal in oldGoals.Take(1))
        {
            var daysSinceTouch = (DateTimeOffset.UtcNow - goal.LastTouchedAt).TotalDays;
            prompts.Add(new CuriosityPrompt
            {
                Type = CuriosityType.Revisitation,
                Prompt = $"You mentioned '{goal.Title}' a while ago ({daysSinceTouch:F0} days). Still interested?",
                Relevance = 0.6,
                SourceNodeId = goal.NodeId,
                Tone = CuriosityTone.Gentle
            });
        }

        return prompts;
    }

    /// <summary>
    /// Hypothesize patterns — observe and wonder.
    /// </summary>
    private List<CuriosityPrompt> HypothesizePatterns()
    {
        var prompts = new List<CuriosityPrompt>();
        var nodes = _nodeStore.LoadAll();

        // Find related nodes that might form a pattern
        var highActivity = nodes.Where(n =>
            n.Salience > 0.5 &&
            n.NodeType == WikiNodeType.Concept)
            .OrderByDescending(n => n.Salience)
            .ToList();

        if (highActivity.Count >= 3)
        {
            var topics = highActivity.Take(3).Select(n => n.Title).ToList();
            prompts.Add(new CuriosityPrompt
            {
                Type = CuriosityType.Hypothesis,
                Prompt = $"I see a pattern in your recent work: {string.Join(", ", topics)}. I'm curious about the connection.",
                Relevance = 0.7,
                Tone = CuriosityTone.Speculative
            });
        }

        return prompts;
    }

    /// <summary>
    /// Celebrate momentum — acknowledge positive progress.
    /// </summary>
    private List<CuriosityPrompt> CelebrateMomentum()
    {
        var prompts = new List<CuriosityPrompt>();
        var momentum = _momentumDetector.ComputeMomentumScore();

        if (momentum.HasMomentum && momentum.Score > 0.6)
        {
            prompts.Add(new CuriosityPrompt
            {
                Type = CuriosityType.Celebration,
                Prompt = "You've been making good progress lately. What's driving the momentum?",
                Relevance = 0.9,
                Tone = CuriosityTone.Encouraging
            });
        }

        return prompts;
    }

    /// <summary>
    /// Check if curiosity should be suppressed (too many interventions active).
    /// </summary>
    public bool ShouldSuppressCuriosity()
    {
        var active = _historyStore.LoadActive();
        var severeCount = active.Count(c =>
            c.CurrentSeverity == ContradictionSeverity.High ||
            c.CurrentSeverity == ContradictionSeverity.Critical);

        // Suppress curiosity if system is overwhelmed with severe contradictions
        return severeCount > 5;
    }
}

/// <summary>
/// A curiosity prompt — gentle exploration.
/// </summary>
public class CuriosityPrompt
{
    public CuriosityType Type { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public double Relevance { get; set; }
    public string? SourceNodeId { get; set; }
    public CuriosityTone Tone { get; set; }
}

public enum CuriosityType
{
    Exploration,    // Ask about new activity
    Revisitation,   // Revisit old interests
    Hypothesis,     // Observe and wonder
    Celebration     // Acknowledge progress
}

public enum CuriosityTone
{
    Interested,    // "What's that about?"
    Gentle,        // "Still interested?"
    Speculative,   // "I'm curious about..."
    Encouraging    // "Good progress!"
}
