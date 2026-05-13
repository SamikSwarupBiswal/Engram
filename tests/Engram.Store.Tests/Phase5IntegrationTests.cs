using Engram.Store.Search;
using Engram.Store.Wiki;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Integration tests for Phase 5 search + briefs.
/// Tests the full flow: wiki -> search -> results, wiki -> brief -> cited output.
/// </summary>
public class Phase5IntegrationTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void FullFlow_WikiToSearchToResults()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        var store = new WikiNodeStore(_workspace.Paths);

        // Create wiki nodes
        store.Save(CreateNode("engram", "Engram Project", WikiNodeType.Project,
            "A personal semantic operating layer for Windows"));
        store.Save(CreateNode("dotnet", ".NET Framework", WikiNodeType.Concept,
            "A cross-platform development framework"));
        store.Save(CreateNode("alice", "Alice Smith", WikiNodeType.Person,
            "Lead developer on Engram"));

        // Search
        var engine = new SearchEngine(store);
        var response = engine.Search("engram semantic");

        Assert.Single(response.Results);
        Assert.Equal("engram", response.Results[0].Node.NodeId);
        Assert.True(response.Results[0].Relevance > 0);
    }

    [Fact]
    public void FullFlow_WikiToBriefWithSources()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        var store = new WikiNodeStore(_workspace.Paths);

        // Create nodes with different ages
        var recent = CreateNode("recent", "Recent Work", WikiNodeType.Project, "Latest changes");
        recent.LastTouchedAt = DateTimeOffset.UtcNow.AddHours(-1);
        recent.OpenQuestions.Add("Is the API design correct?");
        store.Save(recent);

        var stale = CreateNode("stale", "Old Project", WikiNodeType.Project, "Not touched in a while");
        stale.LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-30);
        store.Save(stale);

        // Generate brief
        var generator = new BriefGenerator(store);
        var brief = generator.GenerateMorningBrief();

        Assert.Contains("Recent Work", brief.Content);
        Assert.Contains("Old Project", brief.Content);
        Assert.Contains("Is the API design correct?", brief.Content);
        Assert.Contains("[[recent]]", brief.Content);
        Assert.Equal(1, brief.RecentChanges);
        Assert.Equal(1, brief.StaleItems);
        Assert.Equal(1, brief.OpenQuestions);
    }

    [Fact]
    public void FullFlow_SearchAfterMetabolize()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        var nodeStore = new WikiNodeStore(_workspace.Paths);
        var metabolizer = new WikiMetabolizer(nodeStore);

        // Metabolize raw events into wiki
        var evt1 = TestEvents.Create(eventType: "file_change", text: "ProjectProposal.docx");
        var evt2 = TestEvents.Create(eventType: "clipboard", text: "Meeting notes about budget");

        metabolizer.ProcessEvent(evt1);
        metabolizer.ProcessEvent(evt2);

        // Search the wiki
        var engine = new SearchEngine(nodeStore);
        var response = engine.Search("proposal");

        Assert.Single(response.Results);
        Assert.Contains("ProjectProposal", response.Results[0].Node.Title);
    }

    [Fact]
    public void FullFlow_CaptureStatus_Integration()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        var status = new CaptureStatus(_workspace.Paths);

        // Simulate capture activity
        status.RecordEvent("file_watcher");
        status.RecordEvent("file_watcher");
        status.RecordEvent("clipboard");
        status.RecordDrop("rate_limited");

        Assert.Equal(3, status.CurrentState.TotalEventsCaptured);
        Assert.Equal(1, status.CurrentState.TotalEventsDropped);

        // Pause
        status.Pause();
        Assert.True(status.IsPaused);
        Assert.False(status.IsSourceEnabled("file_watcher"));

        // Resume
        status.Resume();
        Assert.True(status.IsSourceEnabled("file_watcher"));
    }

    [Fact]
    public void FullFlow_SearchRanking_ReflectsRelevance()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        var store = new WikiNodeStore(_workspace.Paths);

        // Node with "engram" in title + summary + facts
        var highNode = CreateNode("engram", "Engram", WikiNodeType.Project, "Engram is a semantic layer");
        highNode.Facts.Add(new WikiFact { Text = "Engram captures local events" });
        store.Save(highNode);

        // Node with "engram" only in a fact
        var lowNode = CreateNode("other", "Other Project", WikiNodeType.Project, "Something else");
        lowNode.Facts.Add(new WikiFact { Text = "Inspired by Engram" });
        store.Save(lowNode);

        var engine = new SearchEngine(store);
        var response = engine.Search("engram");

        Assert.Equal(2, response.Results.Count);
        Assert.Equal("engram", response.Results[0].Node.NodeId); // Higher relevance
        Assert.True(response.Results[0].Relevance > response.Results[1].Relevance);
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
