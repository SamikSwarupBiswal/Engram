using Engram.Store.Identity;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for Discovery SOP.
/// Production requirement: extracts identity, writes files, confirmable.
/// </summary>
public class DiscoverySOPTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void RunDiscovery_ExtractsProfile()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new IdentityStore(_workspace.Paths);
        var sop = new DiscoverySOP(store);

        var answers = new DiscoveryAnswers
        {
            DisplayName = "Samik",
            Goals = new List<string> { "Build Engram", "Ship production software" },
            ComfortTriggers = new List<string> { "Automated tests", "Clear documentation" },
            RecurringAnxieties = new List<string> { "Missing deadlines" },
            Preferences = new List<string> { "Production-grade code" }
        };

        var result = sop.RunDiscovery(answers);

        Assert.Equal("Samik", result.Profile.DisplayName);
        Assert.Equal(2, result.Profile.Goals.Count);
        Assert.Equal(2, result.Profile.ComfortTriggers.Count);
    }

    [Fact]
    public void RunDiscovery_ExtractsPriorities()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new IdentityStore(_workspace.Paths);
        var sop = new DiscoverySOP(store);

        var answers = new DiscoveryAnswers
        {
            Priorities = new List<PriorityAnswer>
            {
                new() { Description = "Ship Engram", Category = PriorityCategory.Career },
                new() { Description = "Stay healthy", Category = PriorityCategory.Health }
            }
        };

        var result = sop.RunDiscovery(answers);

        Assert.Equal(2, result.Priorities.Count);
        Assert.Equal(PriorityCategory.Career, result.Priorities[0].Category);
    }

    [Fact]
    public void RunDiscovery_ExtractsAntiGoals()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new IdentityStore(_workspace.Paths);
        var sop = new DiscoverySOP(store);

        var answers = new DiscoveryAnswers
        {
            AntiGoals = new List<AntiGoalAnswer>
            {
                new() { Description = "No social media during work", Severity = AntiGoalSeverity.High },
                new() { Description = "Never send emails without approval", Severity = AntiGoalSeverity.Critical }
            }
        };

        var result = sop.RunDiscovery(answers);

        Assert.Equal(2, result.AntiGoals.Count);
        Assert.Equal(AntiGoalSeverity.Critical, result.AntiGoals[1].Severity);
    }

    [Fact]
    public void SaveDiscoveryResults_WritesAllFiles()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new IdentityStore(_workspace.Paths);
        var sop = new DiscoverySOP(store);

        var answers = new DiscoveryAnswers
        {
            DisplayName = "Test",
            Goals = new List<string> { "Goal 1" },
            Priorities = new List<PriorityAnswer> { new() { Description = "Priority 1", Category = PriorityCategory.Career } },
            AntiGoals = new List<AntiGoalAnswer> { new() { Description = "Anti-goal 1", Severity = AntiGoalSeverity.Medium } }
        };

        var result = sop.RunDiscovery(answers);
        sop.SaveDiscoveryResults(result);

        Assert.True(store.ProfileExists());
        Assert.True(store.PrioritiesExist());
        Assert.True(store.AntiGoalsExist());
        Assert.True(store.AllIdentityFilesExist());
    }

    [Fact]
    public void IsDiscoveryComplete_ReturnsFalse_BeforeDiscovery()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new IdentityStore(_workspace.Paths);
        var sop = new DiscoverySOP(store);

        Assert.False(sop.IsDiscoveryComplete());
    }

    [Fact]
    public void IsDiscoveryComplete_ReturnsTrue_AfterDiscovery()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new IdentityStore(_workspace.Paths);
        var sop = new DiscoverySOP(store);

        var answers = new DiscoveryAnswers
        {
            DisplayName = "Test",
            Goals = new List<string> { "Goal" },
            Priorities = new List<PriorityAnswer> { new() { Description = "P1", Category = PriorityCategory.Other } },
            AntiGoals = new List<AntiGoalAnswer> { new() { Description = "AG1", Severity = AntiGoalSeverity.Low } }
        };

        var result = sop.RunDiscovery(answers);
        sop.SaveDiscoveryResults(result);

        Assert.True(sop.IsDiscoveryComplete());
    }

    [Fact]
    public void RunDiscovery_SetsCompletedAt()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new IdentityStore(_workspace.Paths);
        var sop = new DiscoverySOP(store);

        var result = sop.RunDiscovery(new DiscoveryAnswers { DisplayName = "Test" });

        Assert.True(result.CompletedAt >= DateTimeOffset.UtcNow.AddMinutes(-1));
    }
}
