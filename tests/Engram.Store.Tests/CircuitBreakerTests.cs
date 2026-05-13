using Engram.Store.Capture;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for circuit breaker flood protection.
/// Production requirement: if rate exceeded for sustained period, pause capture.
/// </summary>
public class CircuitBreakerTests
{
    [Fact]
    public void InitialState_IsClosed()
    {
        var cb = new CircuitBreaker(failureThreshold: 5, openDuration: TimeSpan.FromSeconds(10));
        Assert.Equal(CircuitState.Closed, cb.State);
        Assert.True(cb.IsAllowed);
    }

    [Fact]
    public void OpensAfterThreshold()
    {
        var cb = new CircuitBreaker(failureThreshold: 3, openDuration: TimeSpan.FromSeconds(10));

        cb.RecordFailure();
        cb.RecordFailure();
        Assert.Equal(CircuitState.Closed, cb.State);

        cb.RecordFailure(); // 3rd failure = threshold
        Assert.Equal(CircuitState.Open, cb.State);
        Assert.False(cb.IsAllowed);
    }

    [Fact]
    public void SuccessResetsFailureCount()
    {
        var cb = new CircuitBreaker(failureThreshold: 3, openDuration: TimeSpan.FromSeconds(10));

        cb.RecordFailure();
        cb.RecordFailure();
        cb.RecordSuccess(); // Reset

        cb.RecordFailure();
        cb.RecordFailure();
        Assert.Equal(CircuitState.Closed, cb.State); // Only 2 failures since reset
    }

    [Fact]
    public void HalfOpen_AfterDuration()
    {
        var cb = new CircuitBreaker(failureThreshold: 2, openDuration: TimeSpan.FromMilliseconds(50));

        cb.RecordFailure();
        cb.RecordFailure();
        Assert.Equal(CircuitState.Open, cb.State);

        Thread.Sleep(100);

        Assert.Equal(CircuitState.HalfOpen, cb.State);
        Assert.True(cb.IsAllowed);
    }

    [Fact]
    public void HalfOpen_SuccessCloses()
    {
        var cb = new CircuitBreaker(failureThreshold: 2, openDuration: TimeSpan.FromMilliseconds(50));

        cb.RecordFailure();
        cb.RecordFailure();
        Thread.Sleep(100);

        cb.RecordSuccess();
        Assert.Equal(CircuitState.Closed, cb.State);
    }

    [Fact]
    public void HalfOpen_FailureReopens()
    {
        var cb = new CircuitBreaker(failureThreshold: 2, openDuration: TimeSpan.FromMilliseconds(50));

        cb.RecordFailure();
        cb.RecordFailure();
        Thread.Sleep(100); // Half-open

        cb.RecordFailure(); // Fail in half-open = reopen
        Assert.Equal(CircuitState.Open, cb.State);
    }
}
