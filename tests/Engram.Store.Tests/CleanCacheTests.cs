using Engram.Store;
using Engram.Store.Cloud;

namespace Engram.Store.Tests;

/// <summary>
/// Test contracts for CleanCache — derived from PRD Phase 8 requirements:
/// - Semantic caching for common non-private research topics
/// - Private data never cached
/// - Eviction of old entries
/// </summary>
public class CleanCacheTests : IDisposable
{
    private readonly string _tempDir;

    public CleanCacheTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"engram_cache_{Guid.NewGuid():n}");
        Directory.CreateDirectory(_tempDir);
    }

    // --- Store and retrieve ---

    [Fact]
    public void Put_And_TryGet_Roundtrip()
    {
        using var cache = new CleanCache(_tempDir);

        var entry = CreateCacheEntry("key1", "response text");
        var stored = cache.Put("key1", entry, PrivacyClass.Public);
        Assert.True(stored);

        var found = cache.TryGet("key1", out var cached);
        Assert.True(found);
        Assert.NotNull(cached);
        Assert.Equal("response text", cached!.Response);
    }

    [Fact]
    public void TryGet_Returns_False_For_Missing_Key()
    {
        using var cache = new CleanCache(_tempDir);

        var found = cache.TryGet("nonexistent", out var entry);

        Assert.False(found);
        Assert.Null(entry);
    }

    // --- Hit counting ---

    [Fact]
    public void TryGet_Increments_Hit_Count()
    {
        using var cache = new CleanCache(_tempDir);

        cache.Put("key1", CreateCacheEntry("key1"), PrivacyClass.Public);

        cache.TryGet("key1", out _);
        cache.TryGet("key1", out _);
        cache.TryGet("key1", out var entry);

        Assert.Equal(3, entry!.HitCount);
    }

    [Fact]
    public void TryGet_Updates_Last_Hit_Timestamp()
    {
        using var cache = new CleanCache(_tempDir);

        cache.Put("key1", CreateCacheEntry("key1"), PrivacyClass.Public);

        var before = DateTimeOffset.UtcNow;
        cache.TryGet("key1", out var entry);

        Assert.NotNull(entry!.LastHitAt);
        Assert.True(entry.LastHitAt >= before);
    }

    // --- Private data rejection ---

    [Fact]
    public void Private_Data_Is_Never_Cached()
    {
        using var cache = new CleanCache(_tempDir);

        var entry = CreateCacheEntry("secret", "private data");
        var stored = cache.Put("secret", entry, PrivacyClass.Private);

        Assert.False(stored);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Sensitive_Data_Is_Never_Cached()
    {
        using var cache = new CleanCache(_tempDir);

        var entry = CreateCacheEntry("token", "api key data");
        var stored = cache.Put("token", entry, PrivacyClass.Sensitive);

        Assert.False(stored);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Internal_Data_Can_Be_Cached()
    {
        using var cache = new CleanCache(_tempDir);

        var entry = CreateCacheEntry("internal", "internal data");
        var stored = cache.Put("internal", entry, PrivacyClass.Internal);

        Assert.True(stored);
        Assert.Equal(1, cache.Count);
    }

    // --- Eviction ---

    [Fact]
    public void Cache_Evicts_Oldest_When_Full()
    {
        using var cache = new CleanCache(_tempDir, maxEntries: 3);

        cache.Put("a", CreateCacheEntry("a"), PrivacyClass.Public);
        Thread.Sleep(10); // Ensure different timestamps
        cache.Put("b", CreateCacheEntry("b"), PrivacyClass.Public);
        Thread.Sleep(10);
        cache.Put("c", CreateCacheEntry("c"), PrivacyClass.Public);

        // Cache is full (3/3). Adding 'd' should evict 'a' (oldest, no hits).
        cache.Put("d", CreateCacheEntry("d"), PrivacyClass.Public);

        Assert.Equal(3, cache.Count);
        Assert.False(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
        Assert.True(cache.TryGet("d", out _));
    }

    [Fact]
    public void EvictExpired_Removes_Stale_Entries()
    {
        // Create cache with very short TTL
        using var cache = new CleanCache(_tempDir);

        var expiredEntry = new CacheEntry
        {
            Key = "old",
            Response = "old response",
            Provider = "test",
            Model = "test",
            CostUsd = 0.01m,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-8), // 8 days ago
            TtlHours = 168 // 7 days
        };

        cache.Put("old", expiredEntry, PrivacyClass.Public);
        cache.Put("fresh", CreateCacheEntry("fresh"), PrivacyClass.Public);

        var evicted = cache.EvictExpired();

        Assert.Equal(1, evicted);
        Assert.False(cache.TryGet("old", out _));
        Assert.True(cache.TryGet("fresh", out _));
    }

    [Fact]
    public void TryGet_Returns_False_For_Expired_Entry()
    {
        using var cache = new CleanCache(_tempDir);

        var expiredEntry = new CacheEntry
        {
            Key = "expired",
            Response = "expired response",
            Provider = "test",
            Model = "test",
            CostUsd = 0.01m,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
            TtlHours = 1
        };

        cache.Put("expired", expiredEntry, PrivacyClass.Public);

        var found = cache.TryGet("expired", out _);
        Assert.False(found);
    }

    // --- Persistence ---

    [Fact]
    public void Cache_Persists_Across_Instances()
    {
        var entry = CreateCacheEntry("persist", "persisted response");

        using (var cache1 = new CleanCache(_tempDir))
        {
            cache1.Put("persist", entry, PrivacyClass.Public);
        }

        using (var cache2 = new CleanCache(_tempDir))
        {
            var found = cache2.TryGet("persist", out var cached);
            Assert.True(found);
            Assert.Equal("persisted response", cached!.Response);
        }
    }

    // --- Count ---

    [Fact]
    public void Count_Returns_Number_Of_Entries()
    {
        using var cache = new CleanCache(_tempDir);

        Assert.Equal(0, cache.Count);

        cache.Put("a", CreateCacheEntry("a"), PrivacyClass.Public);
        Assert.Equal(1, cache.Count);

        cache.Put("b", CreateCacheEntry("b"), PrivacyClass.Public);
        Assert.Equal(2, cache.Count);
    }

    // --- Constructor validation ---

    [Fact]
    public void Constructor_Null_Path_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CleanCache(null!));
    }

    // --- Disposed ---

    [Fact]
    public void TryGet_After_Dispose_Throws()
    {
        var cache = new CleanCache(_tempDir);
        cache.Dispose();

        Assert.Throws<ObjectDisposedException>(() => cache.TryGet("key", out _));
    }

    [Fact]
    public void Put_After_Dispose_Throws()
    {
        var cache = new CleanCache(_tempDir);
        cache.Dispose();

        Assert.Throws<ObjectDisposedException>(() => cache.Put("key", CreateCacheEntry("key"), PrivacyClass.Public));
    }

    // --- Helpers ---

    private static CacheEntry CreateCacheEntry(string key, string response = "cached response") => new()
    {
        Key = key,
        Response = response,
        Provider = "gemini-flash",
        Model = "gemini-3-flash",
        CostUsd = 0.001m,
        TtlHours = 168
    };

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
