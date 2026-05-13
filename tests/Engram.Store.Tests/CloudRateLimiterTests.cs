using Engram.Store.Cloud;

namespace Engram.Store.Tests;

/// <summary>
/// Test contracts for CloudRateLimiter — derived from PRD Phase 8 requirements:
/// - Rate limits for cloud API calls (deliverable)
/// - Per-user requests-per-minute and requests-per-hour limits
/// </summary>
public class CloudRateLimiterTests
{
    // --- Basic rate limiting ---

    [Fact]
    public void Fresh_Limiter_Allows_Call()
    {
        var limiter = new CloudRateLimiter(maxPerMinute: 10, maxPerHour: 100);
        var result = limiter.CheckRateLimit();

        Assert.True(result.IsAllowed);
        Assert.Equal(0, result.CallsInLastMinute);
        Assert.Equal(0, result.CallsInLastHour);
    }

    [Fact]
    public void Call_Recorded_Appears_In_Count()
    {
        var limiter = new CloudRateLimiter(maxPerMinute: 10, maxPerHour: 100);
        limiter.RecordCall();

        Assert.Equal(1, limiter.TrackedCallCount);
    }

    [Fact]
    public void Per_Minute_Limit_Blocks_When_Exceeded()
    {
        var limiter = new CloudRateLimiter(maxPerMinute: 3, maxPerHour: 100);

        // Record 3 calls (at the limit)
        limiter.RecordCall();
        limiter.RecordCall();
        limiter.RecordCall();

        var result = limiter.CheckRateLimit();

        Assert.False(result.IsAllowed);
        Assert.Contains("minute", result.DenyReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, result.CallsInLastMinute);
    }

    [Fact]
    public void Per_Minute_Limit_Allows_Just_Below_Limit()
    {
        var limiter = new CloudRateLimiter(maxPerMinute: 3, maxPerHour: 100);

        limiter.RecordCall();
        limiter.RecordCall();

        var result = limiter.CheckRateLimit();

        Assert.True(result.IsAllowed);
        Assert.Equal(2, result.CallsInLastMinute);
    }

    [Fact]
    public void Per_Hour_Limit_Blocks_When_Exceeded()
    {
        var limiter = new CloudRateLimiter(maxPerMinute: 5, maxPerHour: 5);

        // Record 5 calls (at the hourly limit)
        for (int i = 0; i < 5; i++)
            limiter.RecordCall();

        var result = limiter.CheckRateLimit();

        Assert.False(result.IsAllowed);
        // Both per-minute and per-hour are 5 — per-minute triggers first
        Assert.Contains("Rate limit", result.DenyReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(5, result.CallsInLastHour);
    }

    // --- CheckAndRecord ---

    [Fact]
    public void CheckAndRecord_Records_On_Allow()
    {
        var limiter = new CloudRateLimiter(maxPerMinute: 5, maxPerHour: 100);

        var result = limiter.CheckAndRecord();

        Assert.True(result.IsAllowed);
        Assert.Equal(1, limiter.TrackedCallCount);
    }

    [Fact]
    public void CheckAndRecord_Does_Not_Record_On_Deny()
    {
        var limiter = new CloudRateLimiter(maxPerMinute: 1, maxPerHour: 100);

        limiter.RecordCall(); // At limit
        var result = limiter.CheckAndRecord(); // Should be denied

        Assert.False(result.IsAllowed);
        Assert.Equal(1, limiter.TrackedCallCount); // Still only 1
    }

    // --- Constructor validation ---

    [Fact]
    public void Constructor_Zero_PerMinute_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CloudRateLimiter(maxPerMinute: 0, maxPerHour: 100));
    }

    [Fact]
    public void Constructor_Zero_PerHour_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CloudRateLimiter(maxPerMinute: 10, maxPerHour: 0));
    }

    [Fact]
    public void Constructor_PerMinute_GreaterThan_PerHour_Throws()
    {
        Assert.Throws<ArgumentException>(() => new CloudRateLimiter(maxPerMinute: 100, maxPerHour: 10));
    }

    // --- Edge cases ---

    [Fact]
    public void Exactly_At_Per_Minute_Limit_Is_Blocked()
    {
        var limiter = new CloudRateLimiter(maxPerMinute: 5, maxPerHour: 100);

        for (int i = 0; i < 5; i++)
            limiter.RecordCall();

        var result = limiter.CheckRateLimit();
        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void Tracked_Count_Returns_Zero_When_Empty()
    {
        var limiter = new CloudRateLimiter(maxPerMinute: 10, maxPerHour: 100);
        Assert.Equal(0, limiter.TrackedCallCount);
    }
}
