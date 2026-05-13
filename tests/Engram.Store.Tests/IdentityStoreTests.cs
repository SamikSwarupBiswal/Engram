using Engram.Store.Identity;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for identity file persistence.
/// Production requirement: round-trip, atomic writes, existence checks.
/// </summary>
public class IdentityStoreTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void SaveProfile_ThenLoad_RoundTrips()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new IdentityStore(_workspace.Paths);

        var profile = new UserProfile
        {
            DisplayName = "Samik",
            Goals = new List<string> { "Build a successful startup", "Ship production software" },
            ComfortTriggers = new List<string> { "Clear progress indicators", "Automated testing" },
            RecurringAnxieties = new List<string> { "Missing deadlines", "Technical debt accumulation" },
            Preferences = new List<string> { "Concise communication", "Production-grade code" }
        };

        store.SaveProfile(profile);
        var loaded = store.LoadProfile();

        Assert.NotNull(loaded);
        Assert.Equal("Samik", loaded!.DisplayName);
        Assert.Equal(2, loaded.Goals.Count);
        Assert.Contains("Build a successful startup", loaded.Goals);
        Assert.Equal(2, loaded.ComfortTriggers.Count);
        Assert.Equal(2, loaded.RecurringAnxieties.Count);
        Assert.Equal(2, loaded.Preferences.Count);
    }

    [Fact]
    public void SavePriorities_ThenLoad_RoundTrips()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new IdentityStore(_workspace.Paths);

        var priorities = new List<Priority>
        {
            new() { Id = "p1", Description = "Ship Engram MVP", Category = PriorityCategory.Career, Confidence = 0.9 },
            new() { Id = "p2", Description = "Stay healthy", Category = PriorityCategory.Health, Confidence = 1.0 }
        };

        store.SavePriorities(priorities);
        var loaded = store.LoadPriorities();

        Assert.Equal(2, loaded.Count);
        Assert.Equal("p1", loaded[0].Id);
        Assert.Equal(PriorityCategory.Career, loaded[0].Category);
    }

    [Fact]
    public void SaveAntiGoals_ThenLoad_RoundTrips()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new IdentityStore(_workspace.Paths);

        var antiGoals = new List<AntiGoal>
        {
            new() { Id = "ag1", Description = "Do not suggest social media during work", Severity = AntiGoalSeverity.High },
            new() { Id = "ag2", Description = "Never send emails without approval", Severity = AntiGoalSeverity.Critical }
        };

        store.SaveAntiGoals(antiGoals);
        var loaded = store.LoadAntiGoals();

        Assert.Equal(2, loaded.Count);
        Assert.Equal(AntiGoalSeverity.High, loaded[0].Severity);
        Assert.Equal(AntiGoalSeverity.Critical, loaded[1].Severity);
    }

    [Fact]
    public void LoadProfile_ReturnsNull_WhenNotExists()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new IdentityStore(_workspace.Paths);

        Assert.Null(store.LoadProfile());
    }

    [Fact]
    public void LoadPriorities_ReturnsEmpty_WhenNotExists()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new IdentityStore(_workspace.Paths);

        Assert.Empty(store.LoadPriorities());
    }

    [Fact]
    public void SaveProfile_UsesAtomicWrite()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new IdentityStore(_workspace.Paths);

        store.SaveProfile(new UserProfile { DisplayName = "Test" });

        var path = Path.Combine(_workspace.Paths.Wiki, "user_identity.md");
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void AllIdentityFilesExist_ReturnsFalse_WhenNoneExist()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new IdentityStore(_workspace.Paths);

        Assert.False(store.AllIdentityFilesExist());
    }

    [Fact]
    public void AllIdentityFilesExist_ReturnsTrue_WhenAllExist()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new IdentityStore(_workspace.Paths);

        store.SaveProfile(new UserProfile { DisplayName = "Test" });
        store.SavePriorities(new List<Priority> { new() { Id = "p1", Description = "Test" } });
        store.SaveAntiGoals(new List<AntiGoal> { new() { Id = "ag1", Description = "Test" } });

        Assert.True(store.AllIdentityFilesExist());
    }

    [Fact]
    public void SaveProfile_CreatesWikiDirectory()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new IdentityStore(_workspace.Paths);

        store.SaveProfile(new UserProfile { DisplayName = "Test" });

        Assert.True(Directory.Exists(_workspace.Paths.Wiki));
    }

    [Fact]
    public void SaveProfile_UpdatesTimestamp()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new IdentityStore(_workspace.Paths);

        var profile = new UserProfile { DisplayName = "Test" };
        store.SaveProfile(profile);
        var loaded = store.LoadProfile();

        Assert.True(loaded!.LastUpdatedAt >= DateTimeOffset.UtcNow.AddMinutes(-1));
    }
}
