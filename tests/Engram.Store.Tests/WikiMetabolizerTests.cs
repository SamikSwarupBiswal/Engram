using Engram.Store.Wiki;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for raw-to-wiki metabolism.
/// Production requirement: merge (no duplicates), source linking, entity extraction.
/// </summary>
public class WikiMetabolizerTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void ProcessEvent_CreatesNewNode()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new WikiNodeStore(_workspace.Paths);
        var metabolizer = new WikiMetabolizer(store);

        var evt = TestEvents.Create(eventType: "file_change", text: "report.pdf");
        var affected = metabolizer.ProcessEvent(evt);

        Assert.Single(affected);
        Assert.True(store.Exists(affected[0]));
    }

    [Fact]
    public void ProcessEvent_SameEventTwice_MergesNotDuplicates()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new WikiNodeStore(_workspace.Paths);
        var metabolizer = new WikiMetabolizer(store);

        var evt = TestEvents.Create(eventType: "file_change", text: "report.pdf");
        evt.CapturedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        metabolizer.ProcessEvent(evt);
        metabolizer.ProcessEvent(evt); // Same event again

        var all = store.LoadAll();
        Assert.Single(all); // One node, not two
    }

    [Fact]
    public void ProcessEvent_MergesFacts_SameNode()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new WikiNodeStore(_workspace.Paths);
        var metabolizer = new WikiMetabolizer(store);

        var evt1 = TestEvents.Create(eventType: "file_change", text: "report.pdf version 1");
        var evt2 = TestEvents.Create(eventType: "file_change", text: "report.pdf version 2");

        metabolizer.ProcessEvent(evt1);
        metabolizer.ProcessEvent(evt2);

        // Both facts should be in the same node (same document title)
        var all = store.LoadAll();
        Assert.Single(all);
        Assert.Equal(2, all[0].Facts.Count);
    }

    [Fact]
    public void ProcessEvent_LinksSourceEvents()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new WikiNodeStore(_workspace.Paths);
        var metabolizer = new WikiMetabolizer(store);

        var evt = TestEvents.Create(eventType: "file_change", text: "important_doc.pdf");
        metabolizer.ProcessEvent(evt);

        var all = store.LoadAll();
        Assert.Single(all);
        Assert.Single(all[0].Facts[0].Sources);
        Assert.Equal(evt.EventId, all[0].Facts[0].Sources[0].EventId);
    }

    [Fact]
    public void ProcessEvent_DifferentTypes_CreateDifferentNodes()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new WikiNodeStore(_workspace.Paths);
        var metabolizer = new WikiMetabolizer(store);

        var fileEvt = TestEvents.Create(eventType: "file_change", text: "report.pdf");
        var clipEvt = TestEvents.Create(eventType: "clipboard", text: "meeting notes from standup");

        metabolizer.ProcessEvent(fileEvt);
        metabolizer.ProcessEvent(clipEvt);

        var all = store.LoadAll();
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public void ProcessEvent_ResetsSalienceOnUpdate()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new WikiNodeStore(_workspace.Paths);
        var metabolizer = new WikiMetabolizer(store);

        var evt = TestEvents.Create(eventType: "file_change", text: "report.pdf");
        metabolizer.ProcessEvent(evt);

        // Manually decay salience
        var node = store.LoadAll()[0];
        node.Salience = 0.3;
        store.Save(node);

        // Process new event for same node
        var evt2 = TestEvents.Create(eventType: "file_change", text: "report.pdf updated");
        metabolizer.ProcessEvent(evt2);

        var updated = store.LoadAll()[0];
        Assert.Equal(1.0, updated.Salience); // Reset on update
    }

    [Fact]
    public void ProcessEvents_BatchProcess_MultipleEvents()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new WikiNodeStore(_workspace.Paths);
        var metabolizer = new WikiMetabolizer(store);

        var events = new List<RawEvent>
        {
            TestEvents.Create(eventType: "file_change", text: "doc_a.pdf"),
            TestEvents.Create(eventType: "file_change", text: "doc_b.pdf"),
            TestEvents.Create(eventType: "clipboard", text: "random clipboard text")
        };

        var affected = metabolizer.ProcessEvents(events);

        Assert.True(affected.Count >= 2); // At least 2 unique nodes
    }

    [Fact]
    public void ProcessEvent_UpdatesLastTouchedAt()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new WikiNodeStore(_workspace.Paths);
        var metabolizer = new WikiMetabolizer(store);

        var evt = TestEvents.Create(eventType: "file_change", text: "report.pdf");
        metabolizer.ProcessEvent(evt);

        var node = store.LoadAll()[0];
        Assert.True(node.LastTouchedAt >= DateTimeOffset.UtcNow.AddMinutes(-1));
    }
}
