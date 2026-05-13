using Engram.Store.Capture;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for event debouncing.
/// Production requirement: coalesce rapid file changes into single events.
/// </summary>
public class DebouncerTests : IDisposable
{
    public void Dispose() { }

    [Fact]
    public void Debounce_FiresCallbackAfterDelay()
    {
        var fired = new List<string>();
        using var debouncer = new Debouncer<string>(TimeSpan.FromMilliseconds(50), key => fired.Add(key));

        debouncer.Debounce("file1");

        Thread.Sleep(100);

        Assert.Single(fired);
        Assert.Equal("file1", fired[0]);
    }

    [Fact]
    public void Debounce_CoalescesRapidEvents()
    {
        var fired = new List<string>();
        using var debouncer = new Debouncer<string>(TimeSpan.FromMilliseconds(100), key => fired.Add(key));

        // Rapid fire same key
        debouncer.Debounce("file1");
        Thread.Sleep(20);
        debouncer.Debounce("file1");
        Thread.Sleep(20);
        debouncer.Debounce("file1");
        Thread.Sleep(20);

        // Should not have fired yet
        Assert.Empty(fired);

        // Wait for debounce
        Thread.Sleep(150);

        Assert.Single(fired); // Only one callback for "file1"
    }

    [Fact]
    public void Debounce_DifferentKeysFireIndependently()
    {
        var fired = new List<string>();
        using var debouncer = new Debouncer<string>(TimeSpan.FromMilliseconds(50), key => fired.Add(key));

        debouncer.Debounce("file1");
        debouncer.Debounce("file2");

        Thread.Sleep(100);

        Assert.Equal(2, fired.Count);
        Assert.Contains("file1", fired);
        Assert.Contains("file2", fired);
    }

    [Fact]
    public void PendingCount_TracksPendingEvents()
    {
        using var debouncer = new Debouncer<string>(TimeSpan.FromMilliseconds(200), _ => { });

        debouncer.Debounce("file1");
        debouncer.Debounce("file2");

        Assert.Equal(2, debouncer.PendingCount);
    }

    [Fact]
    public void Dispose_StopsPendingTimers()
    {
        var fired = false;
        var debouncer = new Debouncer<string>(TimeSpan.FromMilliseconds(50), _ => fired = true);

        debouncer.Debounce("file1");
        debouncer.Dispose();

        Thread.Sleep(100);

        Assert.False(fired); // Should not fire after dispose
    }
}
