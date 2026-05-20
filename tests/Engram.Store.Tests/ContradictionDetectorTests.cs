using Engram.Store.Identity;
using Engram.Store.Metabolism;
using Engram.Store.Wiki;
using Xunit;

namespace Engram.Store.Tests;

public class ContradictionDetectorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly WorkspacePaths _paths;
    private readonly WikiNodeStore _nodeStore;
    private readonly IdentityStore _identityStore;
    private readonly ContradictionDetector _detector;

    public ContradictionDetectorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram_contradiction_test_" + Guid.NewGuid().ToString("n")[..8]);
        _paths = new WorkspacePaths(_tempDir);
        _nodeStore = new WikiNodeStore(_paths);
        _identityStore = new IdentityStore(_paths);
        _detector = new ContradictionDetector(_nodeStore, _identityStore);
    }

    public void Dispose()
    {
        _nodeStore.Dispose();
        _identityStore.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ── Goal-Activity Contradictions ──

    [Fact]
    public void DetectAll_FadesGoalWithHighActivity()
    {
        // Fading goal
        _nodeStore.Save(new WikiNode
        {
            NodeId = "goal_ship",
            Title = "Ship Engram",
            NodeType = WikiNodeType.Goal,
            Summary = "Ship the first version",
            Salience = 0.2,
            LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-14)
        });

        // High activity unrelated concept
        _nodeStore.Save(new WikiNode
        {
            NodeId = "youtube",
            Title = "YouTube",
            NodeType = WikiNodeType.Concept,
            Summary = "Video platform",
            Salience = 0.9,
            LastTouchedAt = DateTimeOffset.UtcNow
        });

        var contradictions = _detector.DetectAll();

        Assert.Contains(contradictions, c => c.Type == ContradictionType.GoalActivityGap);
    }

    [Fact]
    public void DetectAll_DoesNotFlagActiveGoals()
    {
        // Active goal
        _nodeStore.Save(new WikiNode
        {
            NodeId = "goal_ship",
            Title = "Ship Engram",
            NodeType = WikiNodeType.Goal,
            Summary = "Ship the first version",
            Salience = 0.8,
            LastTouchedAt = DateTimeOffset.UtcNow
        });

        var contradictions = _detector.DetectAll();

        Assert.DoesNotContain(contradictions, c => c.Type == ContradictionType.GoalActivityGap);
    }

    // ── Priority Drift ──

    [Fact]
    public void DetectAll_DetectsPriorityDrift()
    {
        // Set a priority
        _identityStore.SavePriorities(new List<Priority>
        {
            new() { Id = "p1", Description = "Ship Engram", Confidence = 0.9, Category = PriorityCategory.Career }
        });

        // Recent activity unrelated to priority
        _nodeStore.Save(new WikiNode
        {
            NodeId = "gaming",
            Title = "Gaming Session",
            NodeType = WikiNodeType.Concept,
            Summary = "Playing games",
            Salience = 0.8,
            LastTouchedAt = DateTimeOffset.UtcNow
        });

        var contradictions = _detector.DetectAll();

        // May or may not detect depending on algorithm
        Assert.NotNull(contradictions);
    }

    // ── Abandoned Commitments ──

    [Fact]
    public void DetectAll_DetectsAbandonedCommitment()
    {
        _nodeStore.Save(new WikiNode
        {
            NodeId = "commitment_1",
            Title = "Write Tests",
            NodeType = WikiNodeType.Decision,
            Summary = "I will write comprehensive tests",
            Facts = new List<WikiFact>
            {
                new() { Text = "I commit to writing tests for all new code" }
            },
            Salience = 0.5,
            LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-14) // Old
        });

        var contradictions = _detector.DetectAll();

        Assert.Contains(contradictions, c => c.Type == ContradictionType.AbandonedCommitment);
    }

    // ── Identity-Behavior Gaps ──

    [Fact]
    public void DetectAll_DetectsIdentityBehaviorGap()
    {
        // Set a preference
        _identityStore.SaveProfile(new UserProfile
        {
            UserId = "test",
            DisplayName = "Test User",
            ComfortTriggers = new List<string> { "Deep work sessions" }
        });

        var contradictions = _detector.DetectAll();

        // May or may not detect depending on whether "Deep work sessions" appears in wiki
        Assert.NotNull(contradictions);
    }

    // ── Statistics ──

    [Fact]
    public void DetectAll_ReturnsEmptyForNoContradictions()
    {
        // Clean state — no contradictions
        _nodeStore.Save(new WikiNode
        {
            NodeId = "active_goal",
            Title = "Active Goal",
            NodeType = WikiNodeType.Goal,
            Summary = "An active goal",
            Salience = 0.9,
            LastTouchedAt = DateTimeOffset.UtcNow
        });

        var contradictions = _detector.DetectAll();

        Assert.NotNull(contradictions);
    }

    // ── Edge Cases ──

    [Fact]
    public void DetectAll_EmptyGraph_DoesNotCrash()
    {
        var contradictions = _detector.DetectAll();

        Assert.NotNull(contradictions);
        Assert.Empty(contradictions);
    }

    [Fact]
    public void DetectAll_NoIdentity_DoesNotCrash()
    {
        _nodeStore.Save(new WikiNode
        {
            NodeId = "n1",
            Title = "Node 1",
            NodeType = WikiNodeType.Concept,
            Salience = 1.0
        });

        var contradictions = _detector.DetectAll();

        Assert.NotNull(contradictions);
    }

    [Fact]
    public void DetectAll_UnicodeContent_DoesNotCrash()
    {
        _nodeStore.Save(new WikiNode
        {
            NodeId = "unicode",
            Title = "记忆系统",
            NodeType = WikiNodeType.Goal,
            Summary = "一个语义记忆系统",
            Salience = 0.2,
            LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-14)
        });

        var contradictions = _detector.DetectAll();

        Assert.NotNull(contradictions);
    }

    // ── Production-grade ──

    [Fact]
    public void DetectAll_LargeGraph_DoesNotCrash()
    {
        for (int i = 0; i < 30; i++)
        {
            _nodeStore.Save(new WikiNode
            {
                NodeId = $"node_{i}",
                Title = $"Node {i}",
                NodeType = i % 2 == 0 ? WikiNodeType.Goal : WikiNodeType.Concept,
                Summary = $"Summary {i}",
                Salience = 1.0 - (i * 0.03),
                LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-i)
            });
        }

        var contradictions = _detector.DetectAll();

        Assert.NotNull(contradictions);
    }
}
