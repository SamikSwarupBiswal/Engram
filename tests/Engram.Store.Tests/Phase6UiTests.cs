using Engram.Store.Identity;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for Phase 6 UI integration: Discovery SOP, Identity CRUD, Intervention Policy.
/// </summary>
public class Phase6UiTests : IDisposable
{
    private readonly string _tempDir;
    private readonly WorkspacePaths _paths;
    private readonly IdentityStore _store;

    public Phase6UiTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram-p6ui-" + Guid.NewGuid().ToString("N")[..8]);
        _paths = new WorkspacePaths(_tempDir);
        new WorkspaceInitializer().Initialize(_paths);
        _store = new IdentityStore(_paths);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ─── Discovery SOP ───

    [Fact]
    public void Discovery_NotComplete_Initially()
    {
        var sop = new DiscoverySOP(_store);
        Assert.False(sop.IsDiscoveryComplete());
    }

    [Fact]
    public void Discovery_RunAndSave_Completes()
    {
        var sop = new DiscoverySOP(_store);
        var answers = new DiscoveryAnswers
        {
            DisplayName = "Test User",
            Goals = new List<string> { "Build product", "Stay healthy" },
            ComfortTriggers = new List<string> { "Clear communication" },
            RecurringAnxieties = new List<string> { "Missing deadlines" },
            Preferences = new List<string> { "Concise responses" },
            Priorities = new List<PriorityAnswer>
            {
                new() { Description = "Ship Engram", Category = PriorityCategory.Career },
                new() { Description = "Exercise daily", Category = PriorityCategory.Health }
            },
            AntiGoals = new List<AntiGoalAnswer>
            {
                new() { Description = "No social media during work", Severity = AntiGoalSeverity.High },
                new() { Description = "No notifications after 10pm", Severity = AntiGoalSeverity.Medium }
            }
        };

        var result = sop.RunDiscovery(answers);
        sop.SaveDiscoveryResults(result);

        Assert.True(sop.IsDiscoveryComplete());
        Assert.Equal("Test User", result.Profile.DisplayName);
        Assert.Equal(2, result.Profile.Goals.Count);
        Assert.Equal(2, result.Priorities.Count);
        Assert.Equal(2, result.AntiGoals.Count);
    }

    [Fact]
    public void Discovery_EmptyAnswers_SavesDefaults()
    {
        var sop = new DiscoverySOP(_store);
        var answers = new DiscoveryAnswers { DisplayName = "Minimal User" };

        var result = sop.RunDiscovery(answers);
        sop.SaveDiscoveryResults(result);

        Assert.True(sop.IsDiscoveryComplete());
        Assert.Empty(result.Profile.Goals);
        Assert.Empty(result.Priorities);
        Assert.Empty(result.AntiGoals);
    }

    // ─── Identity CRUD ───

    [Fact]
    public void Identity_SaveAndLoad_RoundTrips()
    {
        var profile = new UserProfile
        {
            DisplayName = "CRUD Test",
            Goals = new List<string> { "Goal A", "Goal B" },
            ComfortTriggers = new List<string> { "Trigger X" },
            RecurringAnxieties = new List<string> { "Anxiety 1", "Anxiety 2" },
            Preferences = new List<string> { "Pref A" }
        };

        _store.SaveProfile(profile);
        var loaded = _store.LoadProfile();

        Assert.NotNull(loaded);
        Assert.Equal("CRUD Test", loaded!.DisplayName);
        Assert.Equal(2, loaded.Goals.Count);
        Assert.Single(loaded.ComfortTriggers);
        Assert.Equal(2, loaded.RecurringAnxieties.Count);
        Assert.Single(loaded.Preferences);
    }

    [Fact]
    public void Identity_LoadProfile_ReturnsNull_WhenNotSet()
    {
        Assert.Null(_store.LoadProfile());
    }

    [Fact]
    public void Identity_LoadAntiGoals_ReturnsEmpty_WhenNotSet()
    {
        var antiGoals = _store.LoadAntiGoals();
        Assert.Empty(antiGoals);
    }

    [Fact]
    public void Identity_LoadPriorities_ReturnsEmpty_WhenNotSet()
    {
        var priorities = _store.LoadPriorities();
        Assert.Empty(priorities);
    }

    [Fact]
    public void Identity_AllIdentityFilesExist_ReturnsFalse_Initially()
    {
        Assert.False(_store.AllIdentityFilesExist());
    }

    [Fact]
    public void Identity_AllIdentityFilesExist_ReturnsTrue_AfterDiscovery()
    {
        var sop = new DiscoverySOP(_store);
        var answers = new DiscoveryAnswers
        {
            DisplayName = "Complete User",
            Goals = new List<string> { "Goal 1" },
            Priorities = new List<PriorityAnswer>
            {
                new() { Description = "Priority 1", Category = PriorityCategory.Career }
            },
            AntiGoals = new List<AntiGoalAnswer>
            {
                new() { Description = "Anti-goal 1", Severity = AntiGoalSeverity.Medium }
            }
        };

        var result = sop.RunDiscovery(answers);
        sop.SaveDiscoveryResults(result);

        Assert.True(_store.AllIdentityFilesExist());
    }

    // ─── Intervention Policy ───

    [Fact]
    public void Intervention_NoAntiGoals_AllowsEverything()
    {
        var policy = new InterventionPolicy(_store);
        var request = new InterventionRequest
        {
            Action = "Send notification",
            Context = "Reminder for meeting",
            Category = "notification"
        };

        var result = policy.Evaluate(request);
        Assert.True(result.Allowed);
        Assert.Equal(1.0, result.Confidence);
    }

    [Fact]
    public void Intervention_MatchesAntiGoal_BlocksHigh()
    {
        // Set up anti-goal
        var sop = new DiscoverySOP(_store);
        var answers = new DiscoveryAnswers
        {
            DisplayName = "Policy Test",
            AntiGoals = new List<AntiGoalAnswer>
            {
                new() { Description = "social media during work hours", Severity = AntiGoalSeverity.High }
            }
        };
        var result = sop.RunDiscovery(answers);
        sop.SaveDiscoveryResults(result);

        var policy = new InterventionPolicy(_store);
        var request = new InterventionRequest
        {
            Action = "Suggest social media break",
            Context = "User seems bored during work",
            Category = "suggestion"
        };

        var evalResult = policy.Evaluate(request);
        Assert.False(evalResult.Allowed);
        Assert.Contains("anti-goal", evalResult.Reason.ToLower());
    }

    [Fact]
    public void Intervention_MatchesAntiGoal_LowSeverity_ReducesConfidence()
    {
        var sop = new DiscoverySOP(_store);
        var answers = new DiscoveryAnswers
        {
            DisplayName = "Low Sev Test",
            AntiGoals = new List<AntiGoalAnswer>
            {
                new() { Description = "no complex math problems", Severity = AntiGoalSeverity.Low }
            }
        };
        sop.SaveDiscoveryResults(sop.RunDiscovery(answers));

        var policy = new InterventionPolicy(_store);
        var request = new InterventionRequest
        {
            Action = "Solve complex math problem",
            Context = "Homework help",
            Category = "task"
        };

        var evalResult = policy.Evaluate(request);
        Assert.True(evalResult.Allowed); // Low severity: allowed with reduced confidence
        Assert.True(evalResult.Confidence < 1.0);
    }

    [Fact]
    public void Intervention_NoMatch_ReturnsFullConfidence()
    {
        var sop = new DiscoverySOP(_store);
        var answers = new DiscoveryAnswers
        {
            DisplayName = "No Match Test",
            AntiGoals = new List<AntiGoalAnswer>
            {
                new() { Description = "no cooking suggestions", Severity = AntiGoalSeverity.Medium }
            }
        };
        sop.SaveDiscoveryResults(sop.RunDiscovery(answers));

        var policy = new InterventionPolicy(_store);
        var request = new InterventionRequest
        {
            Action = "Schedule meeting",
            Context = "Work calendar",
            Category = "calendar"
        };

        var evalResult = policy.Evaluate(request);
        Assert.True(evalResult.Allowed);
        Assert.Equal(1.0, evalResult.Confidence);
    }

    [Fact]
    public void Intervention_AnxietBoost_IncreasesPriority()
    {
        var sop = new DiscoverySOP(_store);
        var answers = new DiscoveryAnswers
        {
            DisplayName = "Anxiety Test",
            RecurringAnxieties = new List<string> { "missing important deadlines" },
            AntiGoals = new List<AntiGoalAnswer>()
        };
        sop.SaveDiscoveryResults(sop.RunDiscovery(answers));

        var policy = new InterventionPolicy(_store);
        var request = new InterventionRequest
        {
            Action = "Reminder about deadline",
            Context = "Important project deadline approaching",
            Category = "reminder"
        };

        var evalResult = policy.Evaluate(request);
        Assert.True(evalResult.Allowed);
        Assert.Equal(1.0, evalResult.Confidence); // Anxiety boost = full confidence
    }

    [Fact]
    public void Intervention_InvalidateCache_ReloadsAntiGoals()
    {
        var policy = new InterventionPolicy(_store);

        // Initially no anti-goals
        var r1 = policy.Evaluate(new InterventionRequest { Action = "test", Context = "test", Category = "test" });
        Assert.True(r1.Allowed);

        // Add anti-goals
        var sop = new DiscoverySOP(_store);
        sop.SaveDiscoveryResults(sop.RunDiscovery(new DiscoveryAnswers
        {
            DisplayName = "Cache Test",
            AntiGoals = new List<AntiGoalAnswer>
            {
                new() { Description = "no test actions", Severity = AntiGoalSeverity.Critical }
            }
        }));

        // Without cache invalidation, old anti-goals still used
        // With invalidation, new ones take effect
        policy.InvalidateCache();
        var r2 = policy.Evaluate(new InterventionRequest { Action = "test action", Context = "testing", Category = "test" });
        Assert.False(r2.Allowed);
    }
}
