using Engram.Store.Salience;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for drift alert persistence.
/// Production requirement: save, load, status transitions, statistics.
/// </summary>
public class DriftAlertStoreTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new DriftAlertStore(_workspace.Paths);

        var alert = new DriftAlert
        {
            NodeId = "proj",
            Description = "Test contradiction",
            Severity = DriftSeverity.Medium,
            SourceEventIds = new List<string> { "evt-001" }
        };

        store.Save(alert);
        var loaded = store.LoadAll();

        Assert.Single(loaded);
        Assert.Equal("proj", loaded[0].NodeId);
        Assert.Equal(DriftSeverity.Medium, loaded[0].Severity);
    }

    [Fact]
    public void SaveBatch_SavesMultiple()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new DriftAlertStore(_workspace.Paths);

        var alerts = new List<DriftAlert>
        {
            new() { NodeId = "a", Description = "Alert A" },
            new() { NodeId = "b", Description = "Alert B" }
        };

        store.SaveBatch(alerts);

        Assert.Equal(2, store.LoadAll().Count);
    }

    [Fact]
    public void LoadPending_ReturnsOnlyPending()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new DriftAlertStore(_workspace.Paths);

        store.Save(new DriftAlert { NodeId = "a", Status = DriftAlertStatus.Pending });
        store.Save(new DriftAlert { NodeId = "b", Status = DriftAlertStatus.Dismissed });

        var pending = store.LoadPending();

        Assert.Single(pending);
        Assert.Equal("a", pending[0].NodeId);
    }

    [Fact]
    public void Dismiss_UpdatesStatus()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new DriftAlertStore(_workspace.Paths);

        var alert = new DriftAlert { NodeId = "a" };
        store.Save(alert);

        store.Dismiss(alert.AlertId);

        var loaded = store.LoadAll();
        Assert.Equal(DriftAlertStatus.Dismissed, loaded[0].Status);
        Assert.NotNull(loaded[0].ResolvedAt);
    }

    [Fact]
    public void Accept_UpdatesStatus()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new DriftAlertStore(_workspace.Paths);

        var alert = new DriftAlert { NodeId = "a" };
        store.Save(alert);

        store.Accept(alert.AlertId);

        Assert.Equal(DriftAlertStatus.Accepted, store.LoadAll()[0].Status);
    }

    [Fact]
    public void Convert_UpdatesStatusWithResolution()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new DriftAlertStore(_workspace.Paths);

        var alert = new DriftAlert { NodeId = "a" };
        store.Save(alert);

        store.Convert(alert.AlertId, "Updated wiki with new facts");

        var loaded = store.LoadAll();
        Assert.Equal(DriftAlertStatus.Converted, loaded[0].Status);
        Assert.Equal("Updated wiki with new facts", loaded[0].Resolution);
    }

    [Fact]
    public void GetStats_ReturnsCorrectCounts()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new DriftAlertStore(_workspace.Paths);

        store.Save(new DriftAlert { NodeId = "a", Status = DriftAlertStatus.Pending });
        store.Save(new DriftAlert { NodeId = "b", Status = DriftAlertStatus.Pending });
        store.Save(new DriftAlert { NodeId = "c", Status = DriftAlertStatus.Dismissed });
        store.Save(new DriftAlert { NodeId = "d", Status = DriftAlertStatus.Accepted });

        var stats = store.GetStats();

        Assert.Equal(4, stats.Total);
        Assert.Equal(2, stats.Pending);
        Assert.Equal(1, stats.Dismissed);
        Assert.Equal(1, stats.Accepted);
    }

    [Fact]
    public void LoadAll_EmptyStore_ReturnsEmpty()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new DriftAlertStore(_workspace.Paths);

        Assert.Empty(store.LoadAll());
    }

    [Fact]
    public void Save_UsesAtomicWrite()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new DriftAlertStore(_workspace.Paths);

        store.Save(new DriftAlert { NodeId = "a" });

        var path = Path.Combine(_workspace.Paths.Config, "drift_alerts.json");
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }
}
