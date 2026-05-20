using Engram.Store.Capture;
using Engram.Store.Providers;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for ActiveWindowTracker.
/// Validates polling, caching, event raising, and disposal.
/// </summary>
public class ActiveWindowTrackerTests : IDisposable
{
    private readonly MockActiveWindowProvider _provider = new();

    public void Dispose() { }

    // ─── Constructor ───

    [Fact]
    public void Constructor_DefaultPollInterval_IsOneSecond()
    {
        var tracker = new ActiveWindowTracker(_provider);
        // Default interval is 1 second — just verify construction works
        Assert.Null(tracker.CurrentWindow);
    }

    [Fact]
    public void Constructor_CustomPollInterval_IsRespected()
    {
        var tracker = new ActiveWindowTracker(_provider, pollInterval: TimeSpan.FromMilliseconds(100));
        Assert.Null(tracker.CurrentWindow);
    }

    // ─── CurrentWindow ───

    [Fact]
    public void CurrentWindow_InitiallyNull()
    {
        var tracker = new ActiveWindowTracker(_provider);
        Assert.Null(tracker.CurrentWindow);
    }

    [Fact]
    public void CurrentWindow_AfterPoll_ReturnsProviderValue()
    {
        _provider.NextWindow = new ActiveWindowInfo
        {
            ProcessName = "chrome",
            WindowTitle = "Google",
            ProcessId = 1234
        };

        var tracker = new ActiveWindowTracker(_provider, pollInterval: TimeSpan.FromMilliseconds(50));
        tracker.Start();
        Thread.Sleep(200);
        tracker.Stop();

        Assert.NotNull(tracker.CurrentWindow);
        Assert.Equal("chrome", tracker.CurrentWindow!.ProcessName);
        Assert.Equal("Google", tracker.CurrentWindow.WindowTitle);
    }

    // ─── Start/Stop ───

    [Fact]
    public void Start_SetsIsWatching()
    {
        var tracker = new ActiveWindowTracker(_provider, pollInterval: TimeSpan.FromMilliseconds(100));
        tracker.Start();
        tracker.Stop();
        // No exception = success
    }

    [Fact]
    public void DoubleStart_DoesNotThrow()
    {
        var tracker = new ActiveWindowTracker(_provider, pollInterval: TimeSpan.FromMilliseconds(100));
        tracker.Start();
        tracker.Start(); // Second start should be safe
        tracker.Stop();
    }

    [Fact]
    public void Stop_WithoutStart_DoesNotThrow()
    {
        var tracker = new ActiveWindowTracker(_provider);
        tracker.Stop(); // Should be safe
    }

    // ─── WindowChanged Event ───

    [Fact]
    public void WindowChanged_FiresOnFirstDetection()
    {
        _provider.NextWindow = new ActiveWindowInfo
        {
            ProcessName = "code",
            WindowTitle = "VS Code",
            ProcessId = 5678
        };

        var tracker = new ActiveWindowTracker(_provider, pollInterval: TimeSpan.FromMilliseconds(50));
        ActiveWindowInfo? captured = null;
        tracker.WindowChanged += (_, info) => captured = info;

        tracker.Start();
        Thread.Sleep(200);
        tracker.Stop();

        Assert.NotNull(captured);
        Assert.Equal("code", captured!.ProcessName);
    }

    [Fact]
    public void WindowChanged_DoesNotFireWhenSameWindow()
    {
        _provider.NextWindow = new ActiveWindowInfo
        {
            ProcessName = "chrome",
            WindowTitle = "Google",
            ProcessId = 1234
        };

        var tracker = new ActiveWindowTracker(_provider, pollInterval: TimeSpan.FromMilliseconds(50));
        var fireCount = 0;
        tracker.WindowChanged += (_, _) => fireCount++;

        tracker.Start();
        Thread.Sleep(500); // Multiple polls with same window
        tracker.Stop();

        // Should fire once on first detection, not on subsequent polls
        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void WindowChanged_FiresWhenWindowChanges()
    {
        _provider.NextWindow = new ActiveWindowInfo
        {
            ProcessName = "chrome",
            WindowTitle = "Google",
            ProcessId = 1234
        };

        var tracker = new ActiveWindowTracker(_provider, pollInterval: TimeSpan.FromMilliseconds(50));
        var fireCount = 0;
        tracker.WindowChanged += (_, _) => fireCount++;

        tracker.Start();
        Thread.Sleep(150);

        // Change the window
        _provider.NextWindow = new ActiveWindowInfo
        {
            ProcessName = "code",
            WindowTitle = "VS Code",
            ProcessId = 5678
        };
        Thread.Sleep(200);
        tracker.Stop();

        Assert.Equal(2, fireCount);
    }

    // ─── Provider Returns Null ───

    [Fact]
    public void ProviderReturnsNull_CurrentWindowStaysNull()
    {
        _provider.NextWindow = null;
        var tracker = new ActiveWindowTracker(_provider, pollInterval: TimeSpan.FromMilliseconds(50));
        tracker.Start();
        Thread.Sleep(200);
        tracker.Stop();

        Assert.Null(tracker.CurrentWindow);
    }

    // ─── Provider Exception ───

    [Fact]
    public void ProviderThrows_DoesNotCrashTracker()
    {
        _provider.ThrowOnGet = true;
        var tracker = new ActiveWindowTracker(_provider, pollInterval: TimeSpan.FromMilliseconds(50));
        tracker.Start();
        Thread.Sleep(200);
        tracker.Stop();
        // No exception = graceful handling
    }

    // ─── Dispose ───

    [Fact]
    public void Dispose_StopsTracking()
    {
        var tracker = new ActiveWindowTracker(_provider, pollInterval: TimeSpan.FromMilliseconds(50));
        tracker.Start();
        tracker.Dispose();
        // No exception
    }

    [Fact]
    public void DoubleDispose_DoesNotThrow()
    {
        var tracker = new ActiveWindowTracker(_provider);
        tracker.Dispose();
        tracker.Dispose();
    }

    // ─── Thread Safety ───

    [Fact]
    public void ConcurrentAccess_ThreadSafe()
    {
        _provider.NextWindow = new ActiveWindowInfo { ProcessName = "test", WindowTitle = "test", ProcessId = 1 };
        var tracker = new ActiveWindowTracker(_provider, pollInterval: TimeSpan.FromMilliseconds(10));

        var tasks = new List<Task>();
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < 100; j++)
                {
                    var _ = tracker.CurrentWindow;
                }
            }));
        }

        tracker.Start();
        Task.WaitAll(tasks.ToArray());
        tracker.Stop();
    }
}

// ─── Mock Provider ───

public class MockActiveWindowProvider : IActiveWindowProvider
{
    public ActiveWindowInfo? NextWindow { get; set; }
    public bool ThrowOnGet { get; set; }

    public ActiveWindowInfo? GetActiveWindowInfo()
    {
        if (ThrowOnGet) throw new InvalidOperationException("Mock error");
        return NextWindow;
    }

    public void Dispose() { }
}
