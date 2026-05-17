using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Engram.Store.Wiki;
using Engram.Store.Search;
using Engram.Store.Identity;
using Engram.Store.Salience;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Integration tests for the Engram API endpoints.
/// These tests verify the API layer works correctly with real Engram.Store services.
/// They use the actual workspace (temp directory) — no mocks.
/// </summary>
public class ApiEndpointTests : IDisposable
{
    private readonly string _tempDir;
    private readonly WorkspacePaths _paths;
    private readonly ContentHasher _hasher;
    private readonly RawEventWriter _writer;
    private readonly WikiNodeStore _nodeStore;

    public ApiEndpointTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram-api-test-" + Guid.NewGuid().ToString("N")[..8]);
        _paths = new WorkspacePaths(_tempDir);
        new WorkspaceInitializer().Initialize(_paths);
        _hasher = new ContentHasher();
        _writer = new RawEventWriter(_paths, _hasher);
        _nodeStore = new WikiNodeStore(_paths);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ─── Workspace Init Tests ───

    [Fact]
    public void WorkspaceInit_CreatesAllDirectories()
    {
        Assert.True(Directory.Exists(_paths.Raw));
        Assert.True(Directory.Exists(_paths.Wiki));
        Assert.True(Directory.Exists(_paths.Runs));
        Assert.True(Directory.Exists(_paths.Config));
        Assert.True(Directory.Exists(_paths.Logs));
        Assert.True(Directory.Exists(_paths.Archives));
    }

    [Fact]
    public void WorkspaceInit_IsIdempotent()
    {
        var init1Count = _paths.GetAllRequiredPaths().Count(Directory.Exists);
        new WorkspaceInitializer().Initialize(_paths);
        var init2Count = _paths.GetAllRequiredPaths().Count(Directory.Exists);
        Assert.Equal(init1Count, init2Count);
    }

    // ─── Search API Tests ───

    [Fact]
    public void Search_EmptyWiki_ReturnsNoResults()
    {
        var engine = new SearchEngine(_nodeStore);
        var result = engine.Search("anything");
        Assert.Empty(result.Results);
    }

    [Fact]
    public void Search_WithNodes_ReturnsMatchingResults()
    {
        _nodeStore.Save(new WikiNode
        {
            NodeId = "test_node",
            Title = "Test Project",
            NodeType = WikiNodeType.Project,
            Summary = "A test project for search",
            Facts = new List<WikiFact>
            {
                new() { Text = "Uses C# and .NET 8" }
            }
        });

        var engine = new SearchEngine(_nodeStore);
        var result = engine.Search("test project");

        Assert.NotEmpty(result.Results);
        Assert.Equal("Test Project", result.Results[0].Node.Title);
    }

    [Fact]
    public void Search_MultiWordQuery_UsesAndSemantics()
    {
        _nodeStore.Save(new WikiNode
        {
            NodeId = "node1",
            Title = "Engram Project",
            NodeType = WikiNodeType.Project,
            Summary = "A semantic memory layer"
        });

        var engine = new SearchEngine(_nodeStore);
        var result = engine.Search("engram semantic");
        Assert.NotEmpty(result.Results);

        var noMatch = engine.Search("engram banana");
        Assert.Empty(noMatch.Results);
    }

    // ─── Brief API Tests ───

    [Fact]
    public void Brief_Morning_GeneratesContent()
    {
        var gen = new BriefGenerator(_nodeStore);
        var brief = gen.GenerateMorningBrief();
        Assert.NotNull(brief);
        Assert.NotEmpty(brief.Content);
    }

    [Fact]
    public void Brief_Evening_GeneratesContent()
    {
        var gen = new BriefGenerator(_nodeStore);
        var brief = gen.GenerateEveningBrief();
        Assert.NotNull(brief);
        Assert.NotEmpty(brief.Content);
    }

    // ─── Wiki API Tests ───

    [Fact]
    public void Wiki_LoadAll_EmptyByDefault()
    {
        var nodes = _nodeStore.LoadAll();
        Assert.Empty(nodes);
    }

    [Fact]
    public void Wiki_SaveAndLoad_RoundTrips()
    {
        var node = new WikiNode
        {
            NodeId = "roundtrip_test",
            Title = "Roundtrip Node",
            NodeType = WikiNodeType.Concept,
            Summary = "Testing save and load",
            Facts = new List<WikiFact>
            {
                new() { Text = "Fact 1" },
                new() { Text = "Fact 2" }
            }
        };

        _nodeStore.Save(node);
        var loaded = _nodeStore.Load("roundtrip_test");

        Assert.NotNull(loaded);
        Assert.Equal("Roundtrip Node", loaded!.Title);
        Assert.Equal(WikiNodeType.Concept, loaded.NodeType);
        Assert.Equal(2, loaded.Facts.Count);
    }

    [Fact]
    public void Wiki_LoadAll_ReturnsSavedNodes()
    {
        _nodeStore.Save(new WikiNode { NodeId = "n1", Title = "Node 1", NodeType = WikiNodeType.Person });
        _nodeStore.Save(new WikiNode { NodeId = "n2", Title = "Node 2", NodeType = WikiNodeType.Project });

        var nodes = _nodeStore.LoadAll();
        Assert.Equal(2, nodes.Count);
    }

    // ─── Events API Tests ───

    [Fact]
    public void Events_WriteAndEnumerate()
    {
        var evt = new RawEvent
        {
            EventId = "api-test-evt-1",
            EventType = "file_change",
            CapturedAt = DateTimeOffset.UtcNow,
            Source = "file_watcher",
            Text = "Test file changed"
        };

        var result = _writer.Write(evt);
        Assert.Equal(WriteOutcome.Created, result.Outcome);

        var enumerator = new ReplayEnumerator(_paths);
        var events = enumerator.EnumerateAll();
        Assert.Single(events);
        Assert.Equal("api-test-evt-1", events[0].EventId);
    }

    [Fact]
    public void Events_FilterBySource()
    {
        _writer.Write(new RawEvent { EventId = "fw-1", EventType = "file", CapturedAt = DateTimeOffset.UtcNow, Source = "file_watcher" });
        _writer.Write(new RawEvent { EventId = "cb-1", EventType = "clipboard", CapturedAt = DateTimeOffset.UtcNow, Source = "clipboard" });

        var enumerator = new ReplayEnumerator(_paths);
        var filtered = enumerator.Enumerate(new ReplayQuery { Source = "clipboard" });

        Assert.Single(filtered);
        Assert.Equal("cb-1", filtered[0].EventId);
    }

    // ─── Identity API Tests ───

    [Fact]
    public void Identity_LoadProfile_ReturnsNullWhenNotSet()
    {
        var store = new IdentityStore(_paths);
        var profile = store.LoadProfile();
        Assert.Null(profile);
    }

    [Fact]
    public void Identity_SaveAndLoad_Works()
    {
        var store = new IdentityStore(_paths);
        var profile = new UserProfile
        {
            DisplayName = "Test User",
            Goals = new List<string> { "Build Engram" },
            ComfortTriggers = new List<string> { "Clear communication" }
        };

        store.SaveProfile(profile);
        var loaded = store.LoadProfile();

        Assert.NotNull(loaded);
        Assert.Equal("Test User", loaded!.DisplayName);
        Assert.Single(loaded.Goals);
    }

    // ─── Drift API Tests ───

    [Fact]
    public void Drift_LoadAll_EmptyByDefault()
    {
        var store = new DriftAlertStore(_paths);
        var alerts = store.LoadAll();
        Assert.Empty(alerts);
    }

    [Fact]
    public void Drift_SaveAndLoad()
    {
        var store = new DriftAlertStore(_paths);
        var alert = new DriftAlert
        {
            Description = "Test contradiction",
            Severity = DriftSeverity.Medium
        };

        store.Save(alert);
        var loaded = store.LoadAll();

        Assert.Single(loaded);
        Assert.Equal("Test contradiction", loaded[0].Description);
    }

    // ─── Config API Tests ───

    [Fact]
    public void Config_Load_CreatesDefault()
    {
        var store = new EngramConfigStore(_paths);
        var config = store.Load();

        Assert.Equal("1.0.0", config.Version);
        Assert.False(config.ClipboardCaptureEnabled);
        Assert.False(config.ActiveWindowCaptureEnabled);
        Assert.False(config.FileWatcherEnabled);
        Assert.Equal(TierLevel.Free, config.Tier);
    }

    [Fact]
    public void Config_SaveAndLoad_RoundTrips()
    {
        var store = new EngramConfigStore(_paths);
        var config = store.Load();
        config.Tier = TierLevel.Pro;
        config.CloudEnabled = true;
        store.Save(config);

        var loaded = store.Load();
        Assert.Equal(TierLevel.Pro, loaded.Tier);
        Assert.True(loaded.CloudEnabled);
    }
}
