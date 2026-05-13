using Engram.Store.Salience;
using Engram.Store.Wiki;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for drift detection.
/// Production requirement: keyword contradiction, status change detection.
/// </summary>
public class DriftDetectorTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void DetectDrift_NoContradiction_ReturnsEmpty()
    {
        var (store, detector) = CreateStack();
        store.Save(CreateNode("proj", "Project A", WikiNodeType.Project, "Building the new API"));

        var evt = TestEvents.Create(text: "Working on Project A today");

        var alerts = detector.DetectDrift(evt);

        Assert.Empty(alerts);
    }

    [Fact]
    public void DetectDrift_KeywordContradiction_Detected()
    {
        var (store, detector) = CreateStack();
        store.Save(CreateNode("proj", "Project A", WikiNodeType.Project, "Building the new API for launch"));

        var evt = TestEvents.Create(text: "Project A launch cancelled due to budget");

        var alerts = detector.DetectDrift(evt);

        Assert.Single(alerts);
        Assert.Contains("contradiction", alerts[0].Description.ToLower());
    }

    [Fact]
    public void DetectDrift_StatusChange_CompletedVsBlocked()
    {
        var (store, detector) = CreateStack();
        store.Save(CreateNode("proj", "Project A", WikiNodeType.Project, "Project is blocked by dependency"));

        var evt = TestEvents.Create(text: "Project A completed successfully");

        var alerts = detector.DetectDrift(evt);

        Assert.Single(alerts);
        Assert.Contains("Status conflict", alerts[0].Description);
        Assert.Equal(DriftSeverity.High, alerts[0].Severity);
    }

    [Fact]
    public void DetectDrift_StatusChange_BlockedVsCompleted()
    {
        var (store, detector) = CreateStack();
        store.Save(CreateNode("proj", "Project A", WikiNodeType.Project, "Project completed and shipped"));

        var evt = TestEvents.Create(text: "Project A is now blocked by legal review");

        var alerts = detector.DetectDrift(evt);

        Assert.Single(alerts);
        Assert.Contains("Status conflict", alerts[0].Description);
    }

    [Fact]
    public void DetectDrift_MultipleAlerts_MultipleContradictions()
    {
        var (store, detector) = CreateStack();
        store.Save(CreateNode("proj", "Project A", WikiNodeType.Project, "Building the API for launch"));

        var evt = TestEvents.Create(text: "Project A launch cancelled and blocked by management");

        var alerts = detector.DetectDrift(evt);

        Assert.True(alerts.Count >= 1);
    }

    [Fact]
    public void DetectDrift_LinksSourceEvent()
    {
        var (store, detector) = CreateStack();
        store.Save(CreateNode("proj", "Project A", WikiNodeType.Project, "Building the new API for launch"));

        var evt = TestEvents.Create(text: "Project A launch cancelled");
        var alerts = detector.DetectDrift(evt);

        Assert.Single(alerts);
        Assert.Contains(evt.EventId, alerts[0].SourceEventIds);
    }

    [Fact]
    public void DetectDrift_EmptyText_ReturnsEmpty()
    {
        var (store, detector) = CreateStack();
        store.Save(CreateNode("proj", "Project A", WikiNodeType.Project, "Building the API"));

        var evt = TestEvents.Create(text: "");

        var alerts = detector.DetectDrift(evt);

        Assert.Empty(alerts);
    }

    [Fact]
    public void DetectDrift_EmptyWiki_ReturnsEmpty()
    {
        var (_, detector) = CreateStack();

        var evt = TestEvents.Create(text: "Something cancelled something else");

        var alerts = detector.DetectDrift(evt);

        Assert.Empty(alerts);
    }

    [Fact]
    public void DetectDriftBatch_MultipleEvents()
    {
        var (store, detector) = CreateStack();
        store.Save(CreateNode("a", "Project A", WikiNodeType.Project, "Building API for launch"));

        var events = new List<RawEvent>
        {
            TestEvents.Create(text: "Project A launch cancelled"),
            TestEvents.Create(text: "Normal event with no contradiction")
        };

        var alerts = detector.DetectDriftBatch(events);

        Assert.True(alerts.Count >= 1);
    }

    private (WikiNodeStore store, DriftDetector detector) CreateStack()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        var store = new WikiNodeStore(_workspace.Paths);
        var detector = new DriftDetector(store);
        return (store, detector);
    }

    private WikiNode CreateNode(string id, string title, WikiNodeType type, string summary)
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
