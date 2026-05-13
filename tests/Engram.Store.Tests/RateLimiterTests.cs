using Engram.Store.Capture;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for token bucket rate limiter.
/// Production requirement: prevent event floods from overwhelming the system.
/// </summary>
public class RateLimiterTests
{
    [Fact]
    public void TryAcquire_AllowsBurstUpToMax()
    {
        var limiter = new RateLimiter(maxTokens: 10, refillRatePerSecond: 1);

        for (int i = 0; i < 10; i++)
            Assert.True(limiter.TryAcquire());

        Assert.Equal(10, limiter.PassedCount);
        Assert.Equal(0, limiter.DroppedCount);
    }

    [Fact]
    public void TryAcquire_DropsExcessBeyondMax()
    {
        var limiter = new RateLimiter(maxTokens: 5, refillRatePerSecond: 1);

        for (int i = 0; i < 5; i++)
            limiter.TryAcquire();

        // 6th should be dropped (refill rate is 1/sec, so no refill in <1ms)
        Assert.False(limiter.TryAcquire());
        Assert.Equal(1, limiter.DroppedCount);
    }

    [Fact]
    public void TryAcquire_RefillsOverTime()
    {
        // Use very slow refill to avoid timing issues
        var limiter = new RateLimiter(maxTokens: 2, refillRatePerSecond: 100);

        limiter.TryAcquire();
        limiter.TryAcquire();
        Assert.False(limiter.TryAcquire()); // Exhausted

        // Wait for 1 token to refill (100/sec = 10ms per token)
        Thread.Sleep(50);

        Assert.True(limiter.TryAcquire());
    }

    [Fact]
    public void Reset_RestoresAllTokens()
    {
        var limiter = new RateLimiter(maxTokens: 5, refillRatePerSecond: 0); // No auto-refill

        for (int i = 0; i < 5; i++)
            limiter.TryAcquire();

        Assert.False(limiter.TryAcquire());
        Assert.Equal(1, limiter.DroppedCount);

        limiter.Reset();

        Assert.Equal(0, limiter.DroppedCount);
        Assert.Equal(0, limiter.PassedCount);

        Assert.True(limiter.TryAcquire());
        Assert.Equal(1, limiter.PassedCount);
    }

    [Fact]
    public void ConcurrentAccess_IsThreadSafe()
    {
        // Use 0 refill rate so tokens don't get refilled during test
        var limiter = new RateLimiter(maxTokens: 1000, refillRatePerSecond: 0);
        var passed = 0;
        var dropped = 0;

        Parallel.For(0, 2000, _ =>
        {
            if (limiter.TryAcquire())
                Interlocked.Increment(ref passed);
            else
                Interlocked.Increment(ref dropped);
        });

        Assert.Equal(1000, passed);
        Assert.Equal(1000, dropped);
    }

    [Fact]
    public void DroppedCount_TracksAccurately()
    {
        var limiter = new RateLimiter(maxTokens: 3, refillRatePerSecond: 0);

        limiter.TryAcquire();
        limiter.TryAcquire();
        limiter.TryAcquire();
        limiter.TryAcquire(); // dropped
        limiter.TryAcquire(); // dropped

        Assert.Equal(3, limiter.PassedCount);
        Assert.Equal(2, limiter.DroppedCount);
    }
}
