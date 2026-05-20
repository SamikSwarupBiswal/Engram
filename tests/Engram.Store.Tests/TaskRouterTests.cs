using Engram.Store.Orchestration;
using Engram.Store.Search;
using Engram.Store.Salience;
using Engram.Store.Wiki;
using Engram.Store.Identity;
using Engram.Store.Events;
using Engram.Store.Memory;
using Xunit;

namespace Engram.Store.Tests;

public class TaskRouterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly WorkspacePaths _paths;
    private readonly WikiNodeStore _nodeStore;
    private readonly IdentityStore _identityStore;
    private readonly SearchEngine _basicSearchEngine;
    private readonly SemanticSearchEngine _semanticSearchEngine;
    private readonly PromptAssembler _promptAssembler;
    private readonly IntentClassifier _intentClassifier;
    private readonly SalienceScorer _salienceScorer;
    private readonly DriftDetector _driftDetector;
    private readonly InMemoryEventBus _eventBus;
    private readonly TaskRouter _router;

    public TaskRouterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram_router_test_" + Guid.NewGuid().ToString("n")[..8]);
        _paths = new WorkspacePaths(_tempDir);
        _nodeStore = new WikiNodeStore(_paths);
        _identityStore = new IdentityStore(_paths);
        _basicSearchEngine = new SearchEngine(_nodeStore);
        _semanticSearchEngine = new SemanticSearchEngine(_nodeStore);
        _promptAssembler = new PromptAssembler(_identityStore, _nodeStore, _basicSearchEngine);
        _intentClassifier = new IntentClassifier();
        _salienceScorer = new SalienceScorer();
        _driftDetector = new DriftDetector(_nodeStore);
        _eventBus = new InMemoryEventBus();
        _router = new TaskRouter(
            _intentClassifier, _semanticSearchEngine, _nodeStore, _promptAssembler,
            _identityStore, _salienceScorer, _driftDetector, _eventBus);
    }

    public void Dispose()
    {
        _eventBus.Dispose();
        _semanticSearchEngine.Dispose();
        _basicSearchEngine.Dispose();
        _nodeStore.Dispose();
        _identityStore.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private void SeedTestData()
    {
        _identityStore.SaveProfile(new UserProfile
        {
            UserId = "test",
            DisplayName = "Samik",
            Goals = new List<string> { "Build Engram", "Ship v1" }
        });

        _nodeStore.Save(new WikiNode
        {
            NodeId = "project_engram",
            Title = "Engram",
            NodeType = WikiNodeType.Project,
            Summary = "A semantic memory system",
            Facts = new List<WikiFact> { new() { Text = "Built with .NET 8" } },
            Salience = 1.0
        });

        _nodeStore.Save(new WikiNode
        {
            NodeId = "person_alex",
            Title = "Alex",
            NodeType = WikiNodeType.Person,
            Summary = "Friend and colleague",
            Salience = 0.8
        });

        _basicSearchEngine.InvalidateIndex();
        _semanticSearchEngine.InvalidateIndex();
    }

    // ── Core Routing ──

    [Fact]
    public async Task RouteAsync_MemoryQuery_ReturnsSuccess()
    {
        SeedTestData();

        var result = await _router.RouteAsync("What do you know about Engram?");

        Assert.True(result.Success);
        Assert.Equal(IntentType.MemoryQuery, result.Intent);
        Assert.NotEmpty(result.SystemPrompt);
    }

    [Fact]
    public async Task RouteAsync_TimelineQuery_ReturnsSuccess()
    {
        SeedTestData();

        var result = await _router.RouteAsync("What was I doing today?");

        Assert.True(result.Success);
        Assert.Equal(IntentType.TimelineQuery, result.Intent);
    }

    [Fact]
    public async Task RouteAsync_DriftAnalysis_ReturnsSuccess()
    {
        SeedTestData();

        var result = await _router.RouteAsync("Am I making progress?");

        Assert.True(result.Success);
        Assert.Equal(IntentType.DriftAnalysis, result.Intent);
    }

    [Fact]
    public async Task RouteAsync_StateSynthesis_ReturnsSuccess()
    {
        SeedTestData();

        var result = await _router.RouteAsync("What matters most to me?");

        Assert.True(result.Success);
        Assert.Equal(IntentType.StateSynthesis, result.Intent);
    }

    [Fact]
    public async Task RouteAsync_ResearchTask_ReturnsSuccess()
    {
        SeedTestData();

        var result = await _router.RouteAsync("Find the best GPUs under $500");

        Assert.True(result.Success);
        Assert.Equal(IntentType.ResearchTask, result.Intent);
    }

    [Fact]
    public async Task RouteAsync_AutomationTask_ReturnsSuccess()
    {
        SeedTestData();

        var result = await _router.RouteAsync("Open VSCode");

        Assert.True(result.Success);
        Assert.Equal(IntentType.AutomationTask, result.Intent);
    }

    [Fact]
    public async Task RouteAsync_Conversational_ReturnsSuccess()
    {
        SeedTestData();

        var result = await _router.RouteAsync("Hello");

        Assert.True(result.Success);
        Assert.Equal(IntentType.Conversational, result.Intent);
    }

    // ── System Prompt Quality ──

    [Fact]
    public async Task RouteAsync_MemoryQuery_PromptContainsRetrievedNodes()
    {
        SeedTestData();

        var result = await _router.RouteAsync("What do you know about Engram?");

        Assert.Contains("Engram", result.SystemPrompt);
        Assert.NotEmpty(result.RetrievedNodes);
    }

    [Fact]
    public async Task RouteAsync_TimelineQuery_PromptContainsActivity()
    {
        SeedTestData();

        var result = await _router.RouteAsync("What was I doing today?");

        Assert.Contains("activity", result.SystemPrompt.ToLowerInvariant());
    }

    [Fact]
    public async Task RouteAsync_DriftAnalysis_PromptContainsGoals()
    {
        SeedTestData();

        var result = await _router.RouteAsync("Am I making progress?");

        Assert.Contains("GOAL", result.SystemPrompt.ToUpperInvariant());
    }

    [Fact]
    public async Task RouteAsync_StateSynthesis_PromptContainsProjects()
    {
        SeedTestData();

        var result = await _router.RouteAsync("What matters most?");

        Assert.Contains("PROJECT", result.SystemPrompt.ToUpperInvariant());
    }

    // ── Events ──

    [Fact]
    public async Task RouteAsync_PublishesEvent()
    {
        SeedTestData();
        long eventCount = _eventBus.EventsPublished;

        await _router.RouteAsync("Hello");

        Assert.True(_eventBus.EventsPublished > eventCount);
    }

    [Fact]
    public async Task RouteAsync_PublishesTaskRoutedEvent()
    {
        SeedTestData();
        Engram.Store.Events.EventEnvelope? received = null;
        _eventBus.Subscribe("task.routed", e => received = e);

        await _router.RouteAsync("Hello");

        Assert.NotNull(received);
    }

    // ── Edge Cases ──

    [Fact]
    public async Task RouteAsync_EmptyMessage_DoesNotCrash()
    {
        var result = await _router.RouteAsync("");
        Assert.NotNull(result);
    }

    [Fact]
    public async Task RouteAsync_NullMessage_DoesNotCrash()
    {
        var result = await _router.RouteAsync(null!);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task RouteAsync_VeryLongMessage_DoesNotCrash()
    {
        var longMsg = string.Join(" ", Enumerable.Repeat("What do you know about Engram?", 100));
        var result = await _router.RouteAsync(longMsg);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task RouteAsync_UnicodeMessage_DoesNotCrash()
    {
        var result = await _router.RouteAsync("告诉我关于记忆系统的事情");
        Assert.NotNull(result);
    }

    // ── Contextual System Prompt ──

    [Fact]
    public void GetContextualSystemPrompt_MemoryQuery_ReturnsPrompt()
    {
        var intent = new IntentResult
        {
            Intent = IntentType.MemoryQuery,
            Confidence = 0.8,
            OriginalMessage = "What do you know about Engram?"
        };

        var prompt = _router.GetContextualSystemPrompt(intent);

        Assert.NotEmpty(prompt);
        Assert.Contains("Engram", prompt);
    }

    [Fact]
    public void GetContextualSystemPrompt_Conversational_ReturnsPrompt()
    {
        var intent = new IntentResult
        {
            Intent = IntentType.Conversational,
            Confidence = 0.5,
            OriginalMessage = "Hello"
        };

        var prompt = _router.GetContextualSystemPrompt(intent);

        Assert.NotEmpty(prompt);
    }

    // ── Production-grade ──

    [Fact]
    public async Task RouteAsync_ConcurrentRouting_DoNotCorrupt()
    {
        SeedTestData();

        var tasks = new List<Task>();
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                var result = await _router.RouteAsync($"What do you know about project {Guid.NewGuid()}?");
                Assert.NotNull(result);
            }));
        }

        await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task RouteAsync_MeasuresDuration()
    {
        SeedTestData();

        var result = await _router.RouteAsync("Hello");

        Assert.True(result.Duration >= TimeSpan.Zero);
    }
}
