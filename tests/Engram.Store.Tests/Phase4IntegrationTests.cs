using Engram.Store.Wiki;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Integration tests for Phase 4 wiki memory.
/// Tests the full flow: raw event -> metabolize -> wiki node -> index.
/// </summary>
public class Phase4IntegrationTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void FullFlow_RawEvent_WikiNode_Index()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new WikiNodeStore(_workspace.Paths);
        var metabolizer = new WikiMetabolizer(store);
        var indexer = new IndexGenerator(store);

        // Write a raw event
        var writer = new RawEventWriter(_workspace.Paths, new ContentHasher());
        var evt = TestEvents.Create(
            eventType: "file_change",
            source: "file_watcher",
            text: "ProjectProposal.docx");
        writer.Write(evt);

        // Metabolize into wiki
        var affected = metabolizer.ProcessEvent(evt);
        Assert.Single(affected);

        // Generate index
        var index = indexer.Generate();
        Assert.Contains("ProjectProposal", index);
        Assert.Contains("[[", index);

        // Verify wiki node has source link
        var node = store.Load(affected[0]);
        Assert.NotNull(node);
        Assert.Single(node!.Facts[0].Sources);
        Assert.Equal(evt.EventId, node.Facts[0].Sources[0].EventId);
    }

    [Fact]
    public void FullFlow_Merge_NoDuplicates()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new WikiNodeStore(_workspace.Paths);
        var metabolizer = new WikiMetabolizer(store);

        // Two events about the same document
        var evt1 = TestEvents.Create(eventType: "file_change", text: "report.pdf created");
        var evt2 = TestEvents.Create(eventType: "file_change", text: "report.pdf updated");

        metabolizer.ProcessEvent(evt1);
        metabolizer.ProcessEvent(evt2);

        var all = store.LoadAll();
        Assert.Single(all); // One wiki node, not two
        Assert.Equal(2, all[0].Facts.Count); // Two facts
        Assert.Equal(2, all[0].Facts[0].Sources.Count + all[0].Facts[1].Sources.Count); // Two source refs
    }

    [Fact]
    public void FullFlow_SourceAttribution()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new WikiNodeStore(_workspace.Paths);
        var metabolizer = new WikiMetabolizer(store);

        var evt = TestEvents.Create(eventType: "clipboard", text: "important meeting notes");
        metabolizer.ProcessEvent(evt);

        var node = store.LoadAll()[0];
        Assert.NotEmpty(node.Facts[0].Sources);
        Assert.Equal(evt.EventId, node.Facts[0].Sources[0].EventId);
        Assert.Equal(evt.Source, node.Facts[0].Sources[0].Source);
    }

    [Fact]
    public void FullFlow_MultipleEventTypes_MultipleNodes()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new WikiNodeStore(_workspace.Paths);
        var metabolizer = new WikiMetabolizer(store);
        var indexer = new IndexGenerator(store);

        metabolizer.ProcessEvent(TestEvents.Create(eventType: "file_change", text: "budget.xlsx"));
        metabolizer.ProcessEvent(TestEvents.Create(eventType: "clipboard", text: "research notes on AI"));
        metabolizer.ProcessEvent(TestEvents.Create(eventType: "email", text: "Meeting with investor"));

        var all = store.LoadAll();
        Assert.True(all.Count >= 2); // At least 2 different nodes

        var index = indexer.Generate();
        Assert.Contains("Document", index);
    }

    [Fact]
    public void FullFlow_SerializationRoundTrip_PreservesEverything()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        using var store = new WikiNodeStore(_workspace.Paths);

        var node = new WikiNode
        {
            NodeId = "roundtrip_test",
            Title = "Roundtrip Test",
            NodeType = WikiNodeType.Project,
            Summary = "Testing full roundtrip",
            Salience = 0.75,
            Confidence = 0.90,
            Facts = new List<WikiFact>
            {
                new WikiFact
                {
                    Text = "Fact with source",
                    Sources = new List<WikiSourceReference>
                    {
                        new() { EventId = "evt-rt-001", CapturedAt = DateTimeOffset.UtcNow, Source = "test" }
                    }
                }
            },
            OpenQuestions = new List<string> { "Does this work?" },
            Links = new List<string> { "other_node" }
        };

        store.Save(node);
        var loaded = store.Load("roundtrip_test");

        Assert.NotNull(loaded);
        Assert.Equal("Roundtrip Test", loaded!.Title);
        Assert.Equal(WikiNodeType.Project, loaded.NodeType);
        Assert.Equal(0.75, loaded.Salience);
        Assert.Single(loaded.Facts);
        Assert.Single(loaded.Facts[0].Sources);
        Assert.Equal("evt-rt-001", loaded.Facts[0].Sources[0].EventId);
        Assert.Single(loaded.OpenQuestions);
        Assert.Single(loaded.Links);
    }
}
