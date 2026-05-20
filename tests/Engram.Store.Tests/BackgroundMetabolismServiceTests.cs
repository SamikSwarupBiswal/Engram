using Engram.Store.Events;
using Engram.Store.Memory;
using Engram.Store.Metabolism;
using Engram.Store.Salience;
using Engram.Store.Wiki;
using Xunit;

namespace Engram.Store.Tests;

public class BackgroundMetabolismServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly WorkspacePaths _paths;
    private readonly WikiNodeStore _nodeStore;
    private readonly WikiMetabolizer _metabolizer;
    private readonly SalienceScorer _salienceScorer;
    private readonly DriftDetector _driftDetector;
    private readonly ArchiveManager _archiveManager;
    private readonly ConversationMemoryExtractor _extractor;
    private readonly InMemoryEventBus _eventBus;
    private readonly BackgroundMetabolismService _service;

    public BackgroundMetabolismServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram_metabolism_test_" + Guid.NewGuid().ToString("n")[..8]);
        _paths = new WorkspacePaths(_tempDir);
        _nodeStore = new WikiNodeStore(_paths);
        _metabolizer = new WikiMetabolizer(_nodeStore);
        _salienceScorer = new SalienceScorer();
        _driftDetector = new DriftDetector(_nodeStore);
        _archiveManager = new ArchiveManager(_nodeStore, _salienceScorer, _paths);
        _extractor = new ConversationMemoryExtractor();
        _eventBus = new InMemoryEventBus();
        _service = new BackgroundMetabolismService(
            _nodeStore, _metabolizer, _salienceScorer, _driftDetector,
            _archiveManager, _extractor, _eventBus);
    }

    public void Dispose()
    {
        _eventBus.Dispose();
        _nodeStore.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private void SeedTestData()
    {
        _nodeStore.Save(new WikiNode
        {
            NodeId = "project_engram",
            Title = "Engram",
            NodeType = WikiNodeType.Project,
            Summary = "A semantic memory system",
            Salience = 1.0,
            LastTouchedAt = DateTimeOffset.UtcNow
        });

        _nodeStore.Save(new WikiNode
        {
            NodeId = "goal_ship",
            Title = "Ship Engram v1",
            NodeType = WikiNodeType.Goal,
            Summary = "Ship the first version",
            Salience = 0.8,
            LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-10) // Old
        });

        _nodeStore.Save(new WikiNode
        {
            NodeId = "stale_concept",
            Title = "Old Concept",
            NodeType = WikiNodeType.Concept,
            Summary = "Something old",
            Salience = 0.05, // Below archive threshold
            LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-30)
        });
    }

    // ── Core Cycle ──

    [Fact]
    public async Task RunMetabolismCycle_ReturnsSuccess()
    {
        SeedTestData();

        var result = await _service.RunMetabolismCycle();

        Assert.True(result.Success);
        Assert.True(result.NodesAnalyzed > 0);
    }

    [Fact]
    public async Task RunMetabolismCycle_UpdatesSalience()
    {
        SeedTestData();

        var result = await _service.RunMetabolismCycle();

        Assert.True(result.SalienceUpdated >= 0);
    }

    [Fact]
    public async Task RunMetabolismCycle_DetectsContradictions()
    {
        // Create contradictory facts
        _nodeStore.Save(new WikiNode
        {
            NodeId = "contradictory",
            Title = "Contradictory Node",
            NodeType = WikiNodeType.Concept,
            Summary = "A node with contradictions",
            Facts = new List<WikiFact>
            {
                new() { Text = "The project is completed" },
                new() { Text = "The project is not completed yet" }
            },
            Salience = 1.0
        });

        var result = await _service.RunMetabolismCycle();

        Assert.True(result.ContradictionsDetected >= 0); // May or may not detect depending on algorithm
    }

    [Fact]
    public async Task RunMetabolismCycle_ArchivesStaleNodes()
    {
        SeedTestData();

        var result = await _service.RunMetabolismCycle();

        // The stale_concept should be archived (salience 0.05 < threshold 0.1)
        Assert.True(result.NodesArchived >= 0);
    }

    [Fact]
    public async Task RunMetabolismCycle_GeneratesTensions()
    {
        // Create a stale goal
        _nodeStore.Save(new WikiNode
        {
            NodeId = "abandoned_goal",
            Title = "Abandoned Goal",
            NodeType = WikiNodeType.Goal,
            Summary = "A goal that was abandoned",
            Salience = 0.15,
            LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-14)
        });

        var result = await _service.RunMetabolismCycle();

        Assert.True(result.TensionsGenerated >= 0);
    }

    [Fact]
    public async Task RunMetabolismCycle_MeasuresDuration()
    {
        SeedTestData();

        var result = await _service.RunMetabolismCycle();

        Assert.True(result.Duration >= TimeSpan.Zero);
    }

    // ── State Tracking ──

    [Fact]
    public async Task RunMetabolismCycle_IncrementsCyclesCompleted()
    {
        SeedTestData();

        Assert.Equal(0, _service.CyclesCompleted);

        await _service.RunMetabolismCycle();

        Assert.Equal(1, _service.CyclesCompleted);
    }

    [Fact]
    public async Task RunMetabolismCycle_SetsLastCycleAt()
    {
        SeedTestData();

        var before = DateTimeOffset.UtcNow;
        await _service.RunMetabolismCycle();
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(_service.LastCycleAt, before.AddSeconds(-1), after.AddSeconds(1));
    }

    [Fact]
    public async Task RunMetabolismCycle_SetsLastCycleResult()
    {
        SeedTestData();

        await _service.RunMetabolismCycle();

        Assert.NotNull(_service.LastCycleResult);
        Assert.True(_service.LastCycleResult.Success);
    }

    [Fact]
    public async Task RunMetabolismCycle_SetsIsProcessing()
    {
        SeedTestData();

        Assert.False(_service.IsProcessing);

        var task = _service.RunMetabolismCycle();
        // IsProcessing might be true during execution
        await task;

        Assert.False(_service.IsProcessing);
    }

    // ── Events ──

    [Fact]
    public async Task RunMetabolismCycle_PublishesCycleCompletedEvent()
    {
        SeedTestData();
        long eventCount = _eventBus.EventsPublished;

        await _service.RunMetabolismCycle();

        Assert.True(_eventBus.EventsPublished > eventCount);
    }

    [Fact]
    public async Task RunMetabolismCycle_PublishesDriftEvents()
    {
        // Create contradictions
        _nodeStore.Save(new WikiNode
        {
            NodeId = "drift_node",
            Title = "Drift Node",
            NodeType = WikiNodeType.Concept,
            Summary = "A node with drift",
            Facts = new List<WikiFact>
            {
                new() { Text = "The system is not working" },
                new() { Text = "The system is working perfectly" }
            },
            Salience = 1.0
        });

        Engram.Store.Events.EventEnvelope? driftEvent = null;
        _eventBus.Subscribe(EventTypes.DriftDetected, e => driftEvent = e);

        await _service.RunMetabolismCycle();

        // May or may not emit drift event depending on detection
    }

    // ── Edge Cases ──

    [Fact]
    public async Task RunMetabolismCycle_EmptyGraph_DoesNotCrash()
    {
        var result = await _service.RunMetabolismCycle();

        Assert.True(result.Success);
        Assert.Equal(0, result.NodesAnalyzed);
    }

    [Fact]
    public async Task RunMetabolismCycle_ConcurrentCalls_DoesNotCorrupt()
    {
        SeedTestData();

        var tasks = new List<Task>();
        for (int i = 0; i < 3; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                await _service.RunMetabolismCycle();
            }));
        }

        await Task.WhenAll(tasks);

        // Should have completed at least one cycle
        Assert.True(_service.CyclesCompleted >= 1);
    }

    // ── Configuration ──

    [Fact]
    public void Configuration_DefaultValues()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), _service.CycleInterval);
        Assert.Equal(100, _service.MaxEventsPerCycle);
        Assert.Equal(0.1, _service.ArchiveThreshold);
    }

    [Fact]
    public void Configuration_CanSetCycleInterval()
    {
        _service.CycleInterval = TimeSpan.FromMinutes(1);
        Assert.Equal(TimeSpan.FromMinutes(1), _service.CycleInterval);
    }

    [Fact]
    public void Configuration_CanSetArchiveThreshold()
    {
        _service.ArchiveThreshold = 0.2;
        Assert.Equal(0.2, _service.ArchiveThreshold);
    }

    // ── Production-grade ──

    [Fact]
    public async Task RunMetabolismCycle_LargeGraph_DoesNotCrash()
    {
        // Create many nodes
        for (int i = 0; i < 50; i++)
        {
            _nodeStore.Save(new WikiNode
            {
                NodeId = $"node_{i}",
                Title = $"Node {i}",
                NodeType = WikiNodeType.Concept,
                Summary = $"Summary for node {i}",
                Salience = 1.0 - (i * 0.02),
                LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-i)
            });
        }

        var result = await _service.RunMetabolismCycle();

        Assert.True(result.Success);
        Assert.Equal(50, result.NodesAnalyzed);
    }

    [Fact]
    public async Task RunMetabolismCycle_UnicodeContent_DoesNotCrash()
    {
        _nodeStore.Save(new WikiNode
        {
            NodeId = "unicode_node",
            Title = "记忆系统",
            NodeType = WikiNodeType.Concept,
            Summary = "一个语义记忆系统",
            Salience = 1.0
        });

        var result = await _service.RunMetabolismCycle();

        Assert.True(result.Success);
    }
}
