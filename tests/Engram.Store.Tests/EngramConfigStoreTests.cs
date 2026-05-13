using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for config persistence.
/// </summary>
public class EngramConfigStoreTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void Load_CreatesDefaultConfig_WhenNoneExists()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        var store = new EngramConfigStore(_workspace.Paths);

        var config = store.Load();

        Assert.Equal("1.0.0", config.Version);
        Assert.False(config.ClipboardCaptureEnabled);
        Assert.False(config.ActiveWindowCaptureEnabled);
        Assert.False(config.FileWatcherEnabled);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        var store = new EngramConfigStore(_workspace.Paths);

        var config = new EngramConfig
        {
            Version = "2.0.0",
            ClipboardCaptureEnabled = true,
            ExcludedApps = new List<string> { "password_manager", "banking_app" }
        };

        store.Save(config);
        var loaded = store.Load();

        Assert.Equal("2.0.0", loaded.Version);
        Assert.True(loaded.ClipboardCaptureEnabled);
        Assert.Equal(2, loaded.ExcludedApps.Count);
        Assert.Contains("banking_app", loaded.ExcludedApps);
    }

    [Fact]
    public void Save_UsesAtomicWrite()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        var store = new EngramConfigStore(_workspace.Paths);

        store.Save(new EngramConfig());

        var configPath = Path.Combine(_workspace.Paths.Config, "engram.json");
        Assert.True(File.Exists(configPath));
        Assert.False(File.Exists(configPath + ".tmp"));
    }
}
