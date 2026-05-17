using System.Text.Json;
using Engram.Store.Wiki;
using Engram.Store.Search;
using Engram.Store.Identity;
using Engram.Store.Salience;
using Engram.Store.Validation;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Edge case and production-scenario tests.
/// Tests real-world failure modes: empty inputs, large data, concurrent access, corrupted files.
/// </summary>
public class EdgeCaseTests : IDisposable
{
    private readonly string _tempDir;
    private readonly WorkspacePaths _paths;

    public EdgeCaseTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram-edge-" + Guid.NewGuid().ToString("N")[..8]);
        _paths = new WorkspacePaths(_tempDir);
        new WorkspaceInitializer().Initialize(_paths);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ─── Input Validation Edge Cases ───

    [Fact]
    public void Validate_EmptyEventId_Throws()
    {
        var evt = new RawEvent { EventId = "", EventType = "test", Source = "test" };
        Assert.Throws<EngramValidationException>(() => InputValidator.ValidateRawEvent(evt));
    }

    [Fact]
    public void Validate_PathTraversalInEventId_Throws()
    {
        var evt = new RawEvent { EventId = "../../../etc/passwd", EventType = "test", Source = "test" };
        Assert.Throws<EngramValidationException>(() => InputValidator.ValidateRawEvent(evt));
    }

    [Fact]
    public void Validate_LongEventId_Throws()
    {
        var evt = new RawEvent { EventId = new string('x', 300), EventType = "test", Source = "test" };
        Assert.Throws<EngramValidationException>(() => InputValidator.ValidateRawEvent(evt));
    }

    [Fact]
    public void Validate_NullEventId_Throws()
    {
        var evt = new RawEvent { EventId = null!, EventType = "test", Source = "test" };
        Assert.Throws<EngramValidationException>(() => InputValidator.ValidateRawEvent(evt));
    }

    [Fact]
    public void Validate_ForwardSlashInEventId_Throws()
    {
        var evt = new RawEvent { EventId = "test/path", EventType = "test", Source = "test" };
        Assert.Throws<EngramValidationException>(() => InputValidator.ValidateRawEvent(evt));
    }

    [Fact]
    public void Validate_NullByteInEventId_Throws()
    {
        // Null byte is invalid on ALL platforms
        var evt = new RawEvent { EventId = "test\0path", EventType = "test", Source = "test" };
        Assert.ThrowsAny<Exception>(() => InputValidator.ValidateRawEvent(evt));
    }

    [Fact]
    public void Validate_RootPathTraversal_Throws()
    {
        Assert.Throws<EngramValidationException>(() => new WorkspacePaths("/tmp/../../../etc"));
    }

    [Fact]
    public void Validate_EmptyRoot_Throws()
    {
        Assert.Throws<EngramValidationException>(() => new WorkspacePaths(""));
    }

    [Fact]
    public void Validate_NullRoot_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new WorkspacePaths(null!));
    }

    // ─── Content Hasher Edge Cases ───

    [Fact]
    public void Hash_DifferentEvents_ProduceDifferentHashes()
    {
        var hasher = new ContentHasher();
        var evt1 = new RawEvent { EventId = "a", EventType = "file", CapturedAt = DateTimeOffset.UtcNow, Source = "fw", Text = "hello" };
        var evt2 = new RawEvent { EventId = "b", EventType = "file", CapturedAt = DateTimeOffset.UtcNow, Source = "fw", Text = "world" };

        Assert.NotEqual(hasher.ComputeHash(evt1), hasher.ComputeHash(evt2));
    }

    [Fact]
    public void Hash_SameContent_DifferentEventId_SameHash()
    {
        var hasher = new ContentHasher();
        var time = DateTimeOffset.UtcNow;
        var evt1 = new RawEvent { EventId = "id-1", EventType = "file", CapturedAt = time, Source = "fw", Text = "same" };
        var evt2 = new RawEvent { EventId = "id-2", EventType = "file", CapturedAt = time, Source = "fw", Text = "same" };

        Assert.Equal(hasher.ComputeHash(evt1), hasher.ComputeHash(evt2));
    }

    [Fact]
    public void Hash_NullEvent_Throws()
    {
        var hasher = new ContentHasher();
        Assert.Throws<ArgumentNullException>(() => hasher.ComputeHash(null!));
    }

    // ─── Raw Event Writer Edge Cases ───

    [Fact]
    public void Writer_DuplicateDetection_Works()
    {
        var hasher = new ContentHasher();
        var writer = new RawEventWriter(_paths, hasher);
        var time = DateTimeOffset.UtcNow;

        var evt = new RawEvent
        {
            EventId = "dup-test-1",
            EventType = "file",
            CapturedAt = time,
            Source = "fw",
            Text = "duplicate content"
        };

        var r1 = writer.Write(evt);
        Assert.Equal(WriteOutcome.Created, r1.Outcome);

        // Same content, different ID — should be detected as duplicate
        var evt2 = new RawEvent
        {
            EventId = "dup-test-2",
            EventType = "file",
            CapturedAt = time,
            Source = "fw",
            Text = "duplicate content"
        };

        var r2 = writer.Write(evt2);
        Assert.Equal(WriteOutcome.Duplicate, r2.Outcome);
    }

    [Fact]
    public void Writer_NullEvent_Throws()
    {
        var hasher = new ContentHasher();
        var writer = new RawEventWriter(_paths, hasher);
        Assert.Throws<ArgumentNullException>(() => writer.Write(null!));
    }

    // ─── Search Engine Edge Cases ───

    [Fact]
    public void Search_EmptyQuery_ReturnsEmpty()
    {
        var store = new WikiNodeStore(_paths);
        var engine = new SearchEngine(store);
        var result = engine.Search("");
        Assert.Empty(result.Results);
    }

    [Fact]
    public void Search_NullQuery_ReturnsEmpty()
    {
        var store = new WikiNodeStore(_paths);
        var engine = new SearchEngine(store);
        var result = engine.Search(null!);
        Assert.Empty(result.Results);
    }

    [Fact]
    public void Search_WhitespaceQuery_ReturnsEmpty()
    {
        var store = new WikiNodeStore(_paths);
        var engine = new SearchEngine(store);
        var result = engine.Search("   ");
        Assert.Empty(result.Results);
    }

    [Fact]
    public void Search_MaxResults_RespectsLimit()
    {
        var store = new WikiNodeStore(_paths);
        for (int i = 0; i < 10; i++)
        {
            store.Save(new WikiNode
            {
                NodeId = $"node_{i}",
                Title = $"Node {i}",
                NodeType = WikiNodeType.Concept,
                Summary = "Common search term"
            });
        }

        var engine = new SearchEngine(store);
        var result = engine.Search("common search", maxResults: 3);
        Assert.True(result.Results.Count <= 3);
    }

    [Fact]
    public void Search_ScoreBetween0And1()
    {
        var store = new WikiNodeStore(_paths);
        store.Save(new WikiNode
        {
            NodeId = "score_test",
            Title = "Score Test Node",
            NodeType = WikiNodeType.Concept,
            Summary = "Testing score range"
        });

        var engine = new SearchEngine(store);
        var result = engine.Search("score test");
        foreach (var r in result.Results)
        {
            Assert.InRange(r.Relevance, 0.0, 1.0);
        }
    }

    // ─── Wiki Node Store Edge Cases ───

    [Fact]
    public void Wiki_LoadNonexistent_ReturnsNull()
    {
        var store = new WikiNodeStore(_paths);
        var node = store.Load("does_not_exist");
        Assert.Null(node);
    }

    [Fact]
    public void Wiki_DeleteNonexistent_ReturnsFalse()
    {
        var store = new WikiNodeStore(_paths);
        var result = store.Delete("does_not_exist");
        Assert.False(result);
    }

    [Fact]
    public void Wiki_DeleteExisting_ReturnsTrue()
    {
        var store = new WikiNodeStore(_paths);
        store.Save(new WikiNode { NodeId = "to_delete", Title = "Delete Me", NodeType = WikiNodeType.Concept });
        Assert.True(store.Exists("to_delete"));

        var deleted = store.Delete("to_delete");
        Assert.True(deleted);
        Assert.False(store.Exists("to_delete"));
    }

    [Fact]
    public void Wiki_DeleteThenLoad_ReturnsNull()
    {
        var store = new WikiNodeStore(_paths);
        store.Save(new WikiNode { NodeId = "del_load", Title = "Test", NodeType = WikiNodeType.Concept });
        store.Delete("del_load");
        Assert.Null(store.Load("del_load"));
    }

    [Fact]
    public void Wiki_SaveWithEmptyFacts_Works()
    {
        var store = new WikiNodeStore(_paths);
        var node = new WikiNode
        {
            NodeId = "empty_facts",
            Title = "No Facts",
            NodeType = WikiNodeType.Concept,
            Facts = new List<WikiFact>()
        };

        store.Save(node);
        var loaded = store.Load("empty_facts");
        Assert.NotNull(loaded);
        Assert.Empty(loaded!.Facts);
    }

    [Fact]
    public void Wiki_SaveWithSpecialChars_Works()
    {
        var store = new WikiNodeStore(_paths);
        var node = new WikiNode
        {
            NodeId = "special_chars",
            Title = "Ünïcödé Tëst 日本語",
            NodeType = WikiNodeType.Concept,
            Summary = "Testing special characters: <>&\"'"
        };

        store.Save(node);
        var loaded = store.Load("special_chars");
        Assert.NotNull(loaded);
        Assert.Equal("Ünïcödé Tëst 日本語", loaded!.Title);
    }

    // ─── Config Edge Cases ───

    [Fact]
    public void Config_DefaultValues_AreSafe()
    {
        var store = new EngramConfigStore(_paths);
        var config = store.Load();

        Assert.False(config.ClipboardCaptureEnabled);
        Assert.False(config.ActiveWindowCaptureEnabled);
        Assert.False(config.FileWatcherEnabled);
        Assert.False(config.CloudEnabled);
        Assert.Equal(TierLevel.Free, config.Tier);
        Assert.Equal(1.00m, config.DailyBudgetUsd);
        Assert.Equal(25.00m, config.MonthlyBudgetUsd);
        Assert.Equal(0.50m, config.PerCallLimitUsd);
    }

    // ─── WAL Edge Cases ───

    [Fact]
    public void WAL_NoFile_ReturnsEmptyUncommitted()
    {
        var wal = new WriteAheadLog(_paths.Raw);
        var uncommitted = wal.GetUncommittedWrites();
        Assert.Empty(uncommitted);
    }

    [Fact]
    public void WAL_Clear_Idempotent()
    {
        var wal = new WriteAheadLog(_paths.Raw);
        wal.Clear();
        wal.Clear(); // should not throw
        Assert.Empty(wal.GetUncommittedWrites());
    }

    // ─── Hash Index Edge Cases ───

    [Fact]
    public void HashIndex_EmptyByDefault()
    {
        var index = new HashIndex(_paths.Raw);
        Assert.Equal(0, index.Count);
        Assert.False(index.TryGet("anything", out _));
    }

    [Fact]
    public void HashIndex_AddAndRetrieve()
    {
        var index = new HashIndex(_paths.Raw);
        index.Add("hash123", "/path/to/file.json");
        Assert.True(index.TryGet("hash123", out var path));
        Assert.Equal("/path/to/file.json", path);
    }

    // ─── FileLock Edge Cases ───

    [Fact]
    public void FileLock_AcquireAndRelease()
    {
        var lockPath = Path.Combine(_tempDir, "test.lock");
        var fileLock = FileLock.Acquire(lockPath);
        // Lock acquired successfully
        Assert.NotNull(fileLock);
        fileLock.Dispose();
        // Should not throw after dispose
        var ex = Record.Exception(() => fileLock.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void FileLock_DoubleDispose_DoesNotThrow()
    {
        var lockPath = Path.Combine(_tempDir, "test2.lock");
        var fileLock = FileLock.Acquire(lockPath);
        fileLock.Dispose();
        var ex = Record.Exception(() => fileLock.Dispose());
        Assert.Null(ex);
    }

    // ─── Workspace Initializer Edge Cases ───

    [Fact]
    public void Init_CleansOrphanedTempFiles()
    {
        // Create an old .tmp file
        var tmpFile = Path.Combine(_paths.Raw, "old.tmp");
        File.WriteAllText(tmpFile, "orphaned");
        File.SetLastWriteTimeUtc(tmpFile, DateTime.UtcNow.AddHours(-2));

        var init = new WorkspaceInitializer();
        var cleaned = init.CleanupOrphanedTempFiles(_paths);

        Assert.Equal(1, cleaned);
        Assert.False(File.Exists(tmpFile));
    }

    [Fact]
    public void Init_KeepsRecentTempFiles()
    {
        var tmpFile = Path.Combine(_paths.Raw, "recent.tmp");
        File.WriteAllText(tmpFile, "recent");

        var init = new WorkspaceInitializer();
        var cleaned = init.CleanupOrphanedTempFiles(_paths);

        Assert.Equal(0, cleaned);
        Assert.True(File.Exists(tmpFile));
    }

    // ─── Drift Alert Edge Cases ───

    [Fact]
    public void Drift_ResolveAlert()
    {
        var store = new DriftAlertStore(_paths);
        var alert = new DriftAlert { Description = "Test alert" };
        store.Save(alert);

        store.Accept(alert.AlertId);
        var loaded = store.LoadAll();
        Assert.Single(loaded);
        Assert.Equal(DriftAlertStatus.Accepted, loaded[0].Status);
    }

    [Fact]
    public void Drift_DismissAlert()
    {
        var store = new DriftAlertStore(_paths);
        var alert = new DriftAlert { Description = "False positive" };
        store.Save(alert);

        store.Dismiss(alert.AlertId);
        var loaded = store.LoadAll();
        Assert.Single(loaded);
        Assert.Equal(DriftAlertStatus.Dismissed, loaded[0].Status);
    }

    // ─── Identity Edge Cases ───

    [Fact]
    public void Identity_SaveAndLoadPreservesAllFields()
    {
        var store = new IdentityStore(_paths);
        var profile = new UserProfile
        {
            DisplayName = "Test User",
            Goals = new List<string> { "Goal 1", "Goal 2" },
            ComfortTriggers = new List<string> { "Trigger 1" },
            RecurringAnxieties = new List<string> { "Anxiety 1", "Anxiety 2" },
            Preferences = new List<string> { "Pref 1" }
        };

        store.SaveProfile(profile);
        var loaded = store.LoadProfile();

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.Goals.Count);
        Assert.Single(loaded.ComfortTriggers);
        Assert.Equal(2, loaded.RecurringAnxieties.Count);
        Assert.Single(loaded.Preferences);
    }

    // ─── Salience Edge Cases ───

    [Fact]
    public void Salience_NewNode_StartsAt1()
    {
        var scorer = new SalienceScorer();
        var node = new WikiNode
        {
            NodeId = "salience_test",
            Title = "Test",
            NodeType = WikiNodeType.Concept,
            LastTouchedAt = DateTimeOffset.UtcNow
        };

        var score = scorer.Compute(node);
        Assert.Equal(1.0, score, 2);
    }

    [Fact]
    public void Salience_DecaysOverTime()
    {
        var scorer = new SalienceScorer();
        var node = new WikiNode
        {
            NodeId = "decay_test",
            Title = "Old Node",
            NodeType = WikiNodeType.Concept,
            LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-31)
        };

        var score = scorer.Compute(node);
        Assert.True(score < 0.5, $"Expected decayed score < 0.5, got {score}");
    }

    [Fact]
    public void Salience_TouchResetsTo1()
    {
        var scorer = new SalienceScorer();
        var node = new WikiNode
        {
            NodeId = "touch_test",
            Title = "Touched",
            NodeType = WikiNodeType.Concept,
            LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-100)
        };

        var before = scorer.Compute(node);
        node.LastTouchedAt = DateTimeOffset.UtcNow;
        var after = scorer.Compute(node);

        Assert.True(before < after);
        Assert.Equal(1.0, after, 2);
    }

    // ─── JSON Serialization ───

    [Fact]
    public void RawEvent_SerializesToSnakeCase()
    {
        var evt = new RawEvent
        {
            EventId = "test-json",
            EventType = "file_change",
            CapturedAt = DateTimeOffset.UtcNow,
            Source = "file_watcher",
            PrivacyClass = "private"
        };

        var json = JsonSerializer.Serialize(evt, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        Assert.Contains("event_id", json);
        Assert.Contains("event_type", json);
        Assert.Contains("captured_at", json);
        Assert.Contains("privacy_class", json);
        Assert.DoesNotContain("EventId", json);
    }

    // ─── Replay Edge Cases ───

    [Fact]
    public void Replay_EmptyDirectory_ReturnsEmpty()
    {
        var enumerator = new ReplayEnumerator(_paths);
        var events = enumerator.EnumerateAll();
        Assert.Empty(events);
    }

    [Fact]
    public void Replay_Pagination_Works()
    {
        var hasher = new ContentHasher();
        var writer = new RawEventWriter(_paths, hasher);

        for (int i = 0; i < 5; i++)
        {
            writer.Write(new RawEvent
            {
                EventId = $"page-{i}",
                EventType = "test",
                CapturedAt = DateTimeOffset.UtcNow.AddMinutes(i),
                Source = "test",
                Text = $"Event {i}"
            });
        }

        var enumerator = new ReplayEnumerator(_paths);
        var page1 = enumerator.Enumerate(new ReplayQuery { Offset = 0, Limit = 2 });
        var page2 = enumerator.Enumerate(new ReplayQuery { Offset = 2, Limit = 2 });

        Assert.Equal(2, page1.Count);
        Assert.Equal(2, page2.Count);
        Assert.NotEqual(page1[0].EventId, page2[0].EventId);
    }
}
