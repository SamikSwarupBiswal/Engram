using Engram.Store.Search;
using Engram.Store.Wiki;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for brief generator.
/// Production requirement: morning/evening briefs with source citations.
/// </summary>
public class BriefGeneratorTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void MorningBrief_EmptyWiki_NoErrors()
    {
        var gen = CreateGenerator();
        var brief = gen.GenerateMorningBrief();

        Assert.Contains("Morning Brief", brief.Content);
        Assert.Equal(BriefType.Morning, brief.Type);
    }

    [Fact]
    public void MorningBrief_IncludesRecentChanges()
    {
        var store = CreateStore();
        var node = CreateNode("recent", "Recent Node", WikiNodeType.Project);
        node.LastTouchedAt = DateTimeOffset.UtcNow.AddHours(-2);
        store.Save(node);

        var gen = new BriefGenerator(store);
        var brief = gen.GenerateMorningBrief();

        Assert.Contains("Recent Node", brief.Content);
        Assert.Equal(1, brief.RecentChanges);
    }

    [Fact]
    public void MorningBrief_IncludesStaleItems()
    {
        var store = CreateStore();
        var node = CreateNode("stale", "Stale Node", WikiNodeType.Concept);
        node.LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-30);
        store.Save(node);

        var gen = new BriefGenerator(store);
        var brief = gen.GenerateMorningBrief();

        Assert.Contains("Stale Node", brief.Content);
        Assert.Equal(1, brief.StaleItems);
    }

    [Fact]
    public void MorningBrief_IncludesOpenQuestions()
    {
        var store = CreateStore();
        var node = CreateNode("questions", "Questions Node", WikiNodeType.Project);
        node.OpenQuestions.Add("Is this the right approach?");
        store.Save(node);

        var gen = new BriefGenerator(store);
        var brief = gen.GenerateMorningBrief();

        Assert.Contains("Is this the right approach?", brief.Content);
        Assert.Equal(1, brief.OpenQuestions);
    }

    [Fact]
    public void MorningBrief_IncludesWikiLinks()
    {
        var store = CreateStore();
        store.Save(CreateNode("link_test", "Link Test", WikiNodeType.Concept));

        var gen = new BriefGenerator(store);
        var brief = gen.GenerateMorningBrief();

        Assert.Contains("[[link_test]]", brief.Content);
    }

    [Fact]
    public void EveningBrief_IncludesTodayActivity()
    {
        var store = CreateStore();
        var node = CreateNode("today", "Today Node", WikiNodeType.Project);
        node.LastTouchedAt = DateTimeOffset.UtcNow;
        store.Save(node);

        var gen = new BriefGenerator(store);
        var brief = gen.GenerateEveningBrief();

        Assert.Contains("Today Node", brief.Content);
        Assert.Equal(BriefType.Evening, brief.Type);
    }

    [Fact]
    public void EveningBrief_IncludesPendingQuestions()
    {
        var store = CreateStore();
        var node = CreateNode("pending", "Pending Node", WikiNodeType.Project);
        node.OpenQuestions.Add("What about the API?");
        store.Save(node);

        var gen = new BriefGenerator(store);
        var brief = gen.GenerateEveningBrief();

        Assert.Contains("What about the API?", brief.Content);
    }

    [Fact]
    public void EveningBrief_IncludesLowSalience()
    {
        var store = CreateStore();
        var node = CreateNode("fading", "Fading Node", WikiNodeType.Concept);
        node.Salience = 0.2;
        store.Save(node);

        var gen = new BriefGenerator(store);
        var brief = gen.GenerateEveningBrief();

        Assert.Contains("Fading Node", brief.Content);
        Assert.Contains("0.20", brief.Content);
    }

    [Fact]
    public void SaveBrief_WritesToWikiPath()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        var store = new WikiNodeStore(_workspace.Paths);
        var gen = new BriefGenerator(store);

        var brief = gen.GenerateMorningBrief();
        gen.SaveBrief(brief, _workspace.Paths.Wiki);

        var path = Path.Combine(_workspace.Paths.Wiki, "brief_morning.md");
        Assert.True(File.Exists(path));
        Assert.Contains("Morning Brief", File.ReadAllText(path));
    }

    [Fact]
    public void SaveBrief_UsesAtomicWrite()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        var store = new WikiNodeStore(_workspace.Paths);
        var gen = new BriefGenerator(store);

        var brief = gen.GenerateEveningBrief();
        gen.SaveBrief(brief, _workspace.Paths.Wiki);

        var path = Path.Combine(_workspace.Paths.Wiki, "brief_evening.md");
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void GeneratedAt_IsSet()
    {
        var gen = CreateGenerator();
        var brief = gen.GenerateMorningBrief();

        Assert.True(brief.GeneratedAt >= DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    private WikiNodeStore CreateStore()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        return new WikiNodeStore(_workspace.Paths);
    }

    private BriefGenerator CreateGenerator()
    {
        return new BriefGenerator(CreateStore());
    }

    private WikiNode CreateNode(string id, string title, WikiNodeType type)
    {
        return new WikiNode
        {
            NodeId = id,
            Title = title,
            NodeType = type,
            Summary = $"Test {type}",
            Salience = 1.0,
            Confidence = 1.0,
            LastTouchedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
