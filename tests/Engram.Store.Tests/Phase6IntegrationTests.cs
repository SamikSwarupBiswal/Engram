using Engram.Store.Identity;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Integration tests for Phase 6 identity hardening.
/// Tests the full flow: discovery -> identity files -> intervention gating.
/// </summary>
public class Phase6IntegrationTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void FullFlow_Discovery_Identity_Gating()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new IdentityStore(_workspace.Paths);
        var sop = new DiscoverySOP(store);

        // Step 1: Run discovery
        var answers = new DiscoveryAnswers
        {
            DisplayName = "Samik",
            Goals = new List<string> { "Build Engram startup" },
            ComfortTriggers = new List<string> { "Automated testing" },
            RecurringAnxieties = new List<string> { "Missing deadlines" },
            Preferences = new List<string> { "Production-grade everything" },
            Priorities = new List<PriorityAnswer>
            {
                new() { Description = "Ship Engram", Category = PriorityCategory.Career }
            },
            AntiGoals = new List<AntiGoalAnswer>
            {
                new() { Description = "Do not suggest social media during work", Severity = AntiGoalSeverity.High },
                new() { Description = "Never send emails without approval", Severity = AntiGoalSeverity.Critical }
            }
        };

        var result = sop.RunDiscovery(answers);
        sop.SaveDiscoveryResults(result);

        // Step 2: Verify identity files exist
        Assert.True(store.AllIdentityFilesExist());

        // Step 3: Intervention policy gates behavior
        var policy = new InterventionPolicy(store);

        // Blocked: social media during work (high severity)
        var socialMedia = policy.Evaluate(new InterventionRequest
        {
            Action = "suggest social media",
            Context = "work hours"
        });
        Assert.False(socialMedia.Allowed);
        Assert.Contains("social media", socialMedia.Reason);

        // Blocked: send email without approval (critical)
        var email = policy.Evaluate(new InterventionRequest
        {
            Action = "send email",
            Context = "follow up"
        });
        Assert.False(email.Allowed);

        // Allowed: deadline reminder (relates to anxiety)
        var deadline = policy.Evaluate(new InterventionRequest
        {
            Action = "reminder",
            Context = "deadline approaching for Engram"
        });
        Assert.True(deadline.Allowed);

        // Allowed: normal notification
        var normal = policy.Evaluate(new InterventionRequest
        {
            Action = "show progress update",
            Context = "build completed"
        });
        Assert.True(normal.Allowed);
    }

    [Fact]
    public void FullFlow_IdentityFiles_CanBeEditedDirectly()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new IdentityStore(_workspace.Paths);

        // Write initial anti-goals
        store.SaveAntiGoals(new List<AntiGoal>
        {
            new() { Id = "ag1", Description = "No notifications", Severity = AntiGoalSeverity.Low }
        });

        // Verify it's there
        var loaded = store.LoadAntiGoals();
        Assert.Single(loaded);

        // Edit: add more anti-goals
        loaded.Add(new AntiGoal { Id = "ag2", Description = "Never interrupt deep work", Severity = AntiGoalSeverity.Critical });
        store.SaveAntiGoals(loaded);

        // Verify update
        var updated = store.LoadAntiGoals();
        Assert.Equal(2, updated.Count);
    }

    [Fact]
    public void FullFlow_PolicyExplainsBlockedReason()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new IdentityStore(_workspace.Paths);

        store.SaveAntiGoals(new List<AntiGoal>
        {
            new() { Id = "ag1", Description = "Do not suggest gambling websites", Severity = AntiGoalSeverity.Critical }
        });

        var policy = new InterventionPolicy(store);
        var result = policy.Evaluate(new InterventionRequest
        {
            Action = "suggest gambling",
            Context = "entertainment"
        });

        Assert.False(result.Allowed);
        Assert.Contains("gambling", result.Reason);
        Assert.Contains("Critical", result.Reason);
    }

    [Fact]
    public void FullFlow_DiscoveryComplete_AllFilesPresent()
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

        sop.SaveDiscoveryResults(sop.RunDiscovery(answers));

        Assert.True(store.ProfileExists());
        Assert.True(store.PrioritiesExist());
        Assert.True(store.AntiGoalsExist());
        Assert.True(sop.IsDiscoveryComplete());
    }
}
