using Engram.Store.Events;
using Engram.Store.Identity;
using Engram.Store.Metabolism;
using Xunit;

namespace Engram.Store.Tests;

public class InterventionGeneratorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly WorkspacePaths _paths;
    private readonly IdentityStore _identityStore;
    private readonly InMemoryEventBus _eventBus;
    private readonly InterventionGenerator _generator;

    public InterventionGeneratorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram_intervention_test_" + Guid.NewGuid().ToString("n")[..8]);
        _paths = new WorkspacePaths(_tempDir);
        _identityStore = new IdentityStore(_paths);
        _eventBus = new InMemoryEventBus();
        _generator = new InterventionGenerator(_identityStore, _eventBus);
    }

    public void Dispose()
    {
        _eventBus.Dispose();
        _identityStore.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void GenerateInterventions_FromContradictions_ReturnsInterventions()
    {
        var contradictions = new List<BehavioralContradiction>
        {
            new()
            {
                Type = ContradictionType.GoalActivityGap,
                Severity = ContradictionSeverity.High,
                Description = "Goal fading",
                DeclaredIntent = "Ship Engram",
                ObservedBehavior = "High activity on YouTube",
                RelatedNodeIds = new List<string> { "goal_1" }
            }
        };

        var interventions = _generator.GenerateInterventions(contradictions);

        Assert.NotEmpty(interventions);
        Assert.Contains(interventions, i => i.Type == InterventionType.GoalDrift);
    }

    [Fact]
    public void GenerateInterventions_LowSeverity_BelowThreshold()
    {
        var contradictions = new List<BehavioralContradiction>
        {
            new()
            {
                Type = ContradictionType.IdentityBehaviorGap,
                Severity = ContradictionSeverity.Low,
                Description = "Minor gap",
                DeclaredIntent = "Preference",
                ObservedBehavior = "No activity"
            }
        };

        _generator.Threshold = InterventionThreshold.Medium;
        var interventions = _generator.GenerateInterventions(contradictions);

        Assert.Empty(interventions);
    }

    [Fact]
    public void GenerateInterventions_PublishesEvents()
    {
        var contradictions = new List<BehavioralContradiction>
        {
            new()
            {
                Type = ContradictionType.PriorityDrift,
                Severity = ContradictionSeverity.Medium,
                Description = "Priority drift",
                DeclaredIntent = "Ship product",
                ObservedBehavior = "Gaming"
            }
        };

        long eventsBefore = _eventBus.EventsPublished;
        _generator.GenerateInterventions(contradictions);

        Assert.True(_eventBus.EventsPublished > eventsBefore);
    }

    [Fact]
    public void GenerateInterventions_EmptyList_ReturnsEmpty()
    {
        var interventions = _generator.GenerateInterventions(new List<BehavioralContradiction>());
        Assert.Empty(interventions);
    }

    [Fact]
    public void GenerateFromTensions_HighSeverity_ReturnsInterventions()
    {
        var tensions = new List<TensionReport>
        {
            new()
            {
                Source = "abandoned_goal",
                Description = "Goal abandoned",
                Severity = Salience.DriftSeverity.High
            }
        };

        var interventions = _generator.GenerateFromTensions(tensions);

        Assert.NotEmpty(interventions);
    }

    [Fact]
    public void GenerateFromTensions_LowSeverity_ReturnsEmpty()
    {
        var tensions = new List<TensionReport>
        {
            new()
            {
                Source = "minor",
                Description = "Minor issue",
                Severity = Salience.DriftSeverity.Low
            }
        };

        var interventions = _generator.GenerateFromTensions(tensions);

        Assert.Empty(interventions);
    }

    [Fact]
    public void Configuration_DefaultThreshold()
    {
        Assert.Equal(InterventionThreshold.Medium, _generator.Threshold);
    }

    [Fact]
    public void Configuration_CanSetThreshold()
    {
        _generator.Threshold = InterventionThreshold.High;
        Assert.Equal(InterventionThreshold.High, _generator.Threshold);
    }

    [Fact]
    public void Intervention_HasMessage()
    {
        var contradictions = new List<BehavioralContradiction>
        {
            new()
            {
                Type = ContradictionType.GoalActivityGap,
                Severity = ContradictionSeverity.High,
                Description = "Goal fading",
                DeclaredIntent = "Ship Engram",
                ObservedBehavior = "High activity elsewhere"
            }
        };

        var interventions = _generator.GenerateInterventions(contradictions);

        Assert.NotEmpty(interventions);
        Assert.NotEmpty(interventions[0].Message);
    }

    [Fact]
    public void Intervention_HasTimestamp()
    {
        var contradictions = new List<BehavioralContradiction>
        {
            new()
            {
                Type = ContradictionType.PriorityDrift,
                Severity = ContradictionSeverity.Medium,
                Description = "Drift",
                DeclaredIntent = "Priority",
                ObservedBehavior = "Other"
            }
        };

        var before = DateTimeOffset.UtcNow;
        var interventions = _generator.GenerateInterventions(contradictions);
        var after = DateTimeOffset.UtcNow;

        Assert.NotEmpty(interventions);
        Assert.InRange(interventions[0].GeneratedAt, before.AddSeconds(-1), after.AddSeconds(1));
    }
}
