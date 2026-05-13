using Engram.Store.Salience;
using Engram.Store.Wiki;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Integration tests for Phase 7 salience + drift.
/// Tests the full flow: wiki -> salience decay -> archive, event -> drift -> alert -> resolve.
/// </summary>
public class Phase7IntegrationTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void FullFlow_SalienceDecay_Archive()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        var store = new WikiNodeStore(_workspace.Paths);
        var scorer = new SalienceScorer();
        var archiveManager = new ArchiveManager(store, scorer, _workspace.Paths);

        // Create fresh and stale nodes
        var fresh = CreateNode("fresh", "Fresh", WikiNodeType.Project);
        fresh.LastTouchedAt = DateTimeOffset.UtcNow;
        store.Save(fresh);

        var stale = CreateNode("stale", "Stale", WikiNodeType.Concept);
        stale.LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-200);
        store.Save(stale);

        // Verify salience
        Assert.True(scorer.Compute(fresh) > 0.9);
        Assert.True(scorer.Compute(stale) < 0.1);

        // Archive stale
        var archived = archiveManager.ArchiveStaleNodes();
        Assert.Single(archived);
        Assert.Equal("stale", archived[0]);

        // Verify
        Assert.Single(store.LoadAll()); // Only fresh remains
        Assert.Single(archiveManager.ListArchived()); // Stale in archive
    }

    [Fact]
    public void FullFlow_DriftDetection_Alert_Resolve()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        var nodeStore = new WikiNodeStore(_workspace.Paths);
        var alertStore = new DriftAlertStore(_workspace.Paths);
        var detector = new DriftDetector(nodeStore);

        // Wiki says project is building
        nodeStore.Save(CreateNode("proj", "Project", WikiNodeType.Project, "Building the new API for launch"));

        // Raw event says it's cancelled
        var evt = TestEvents.Create(text: "Building cancelled due to budget cuts");

        // Detect drift
        var alerts = detector.DetectDrift(evt);
        Assert.True(alerts.Count >= 1);

        // Save alerts
        alertStore.SaveBatch(alerts);

        // Verify pending
        Assert.Single(alertStore.LoadPending());

        // Verify alert was saved
        var allAlerts = alertStore.LoadAll();
        Assert.True(allAlerts.Count >= 1, "Expected at least 1 alert in store, got " + allAlerts.Count);

        // Resolve: accept and convert
        var alertId = allAlerts[0].AlertId;
        alertStore.Accept(alertId);
        alertStore.Convert(alertId, "Updated wiki: project cancelled");

        // Verify resolved
        Assert.Empty(alertStore.LoadPending());
        var stats = alertStore.GetStats();
        Assert.Equal(1, stats.Converted);
    }

    [Fact]
    public void FullFlow_SalienceRefresh_OnTouch()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        var store = new WikiNodeStore(_workspace.Paths);
        var scorer = new SalienceScorer();

        // Create old node
        var node = CreateNode("old", "Old", WikiNodeType.Concept);
        node.LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-60);
        store.Save(node);

        var oldSalience = scorer.Compute(node);
        Assert.True(oldSalience < 0.5);

        // Touch it (simulate update via metabolizer)
        node.LastTouchedAt = DateTimeOffset.UtcNow;
        node.Salience = 1.0;
        store.Save(node);

        var newSalience = scorer.Compute(store.Load("old")!);
        Assert.True(newSalience > 0.95);
    }

    [Fact]
    public void FullFlow_ArchiveRestore()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        var store = new WikiNodeStore(_workspace.Paths);
        var scorer = new SalienceScorer();
        var archiveManager = new ArchiveManager(store, scorer, _workspace.Paths);

        var node = CreateNode("restore_test", "Restore Test", WikiNodeType.Project);
        node.LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-200);
        store.Save(node);

        archiveManager.ArchiveNode(node);
        Assert.False(store.Exists("restore_test"));

        archiveManager.RestoreFromArchive("restore_test");

        var restored = store.Load("restore_test");
        Assert.NotNull(restored);
        Assert.Equal(1.0, restored!.Salience); // Reset
    }

    [Fact]
    public void FullFlow_DriftAlertDismissal()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        var nodeStore = new WikiNodeStore(_workspace.Paths);
        var alertStore = new DriftAlertStore(_workspace.Paths);
        var detector = new DriftDetector(nodeStore);

        nodeStore.Save(CreateNode("proj", "Project", WikiNodeType.Project));

        // False positive: event mentions "cancelled" but not about this project
        var evt = TestEvents.Create(text: "Cancelled my gym membership");
        var alerts = detector.DetectDrift(evt);

        if (alerts.Count > 0)
        {
            alertStore.Save(alerts[0]);
            alertStore.Dismiss(alerts[0].AlertId);

            Assert.Empty(alertStore.LoadPending());
            Assert.Equal(1, alertStore.GetStats().Dismissed);
        }
    }

    private WikiNode CreateNode(string id, string title, WikiNodeType type, string summary = "Test")
    {
        return new WikiNode
        {
            NodeId = id,
            Title = title,
            NodeType = type,
            Summary = summary,
            Salience = 1.0,
            Confidence = 1.0,
            LastTouchedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
