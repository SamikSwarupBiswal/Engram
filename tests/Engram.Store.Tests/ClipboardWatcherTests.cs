using Engram.Store.Capture;
using Engram.Store.Providers;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for ClipboardWatcher.
/// Validates content hash detection, exclusion enforcement, rate limiting, and disposal.
/// </summary>
public class ClipboardWatcherTests : IDisposable
{
    private readonly MockClipboardProvider _clipboardProvider = new();
    private readonly MockActiveWindowProviderForClipboard _activeWindowProvider = new();
    private readonly ExclusionList _exclusionList;
    private readonly RateLimiter _rateLimiter;
    private readonly string _tempDir;

    public ClipboardWatcherTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram-clipboard-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _exclusionList = new ExclusionList();
        _rateLimiter = new RateLimiter(100, 100.0 / 60.0);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private ClipboardWatcher CreateWatcher(TimeSpan? interval = null)
    {
        return new ClipboardWatcher(
            _clipboardProvider,
            _exclusionList,
            _activeWindowProvider,
            _rateLimiter,
            pollInterval: interval ?? TimeSpan.FromMilliseconds(50));
    }

    // ─── Constructor ───

    [Fact]
    public void Constructor_DoesNotThrow()
    {
        var watcher = CreateWatcher();
        Assert.False(watcher.IsMonitoring);
    }

    // ─── Start/Stop ───

    [Fact]
    public void Start_SetsIsMonitoring()
    {
        var watcher = CreateWatcher();
        watcher.Start();
        Assert.True(watcher.IsMonitoring);
        watcher.Stop();
    }

    [Fact]
    public void Stop_ClearsIsMonitoring()
    {
        var watcher = CreateWatcher();
        watcher.Start();
        watcher.Stop();
        Assert.False(watcher.IsMonitoring);
    }

    [Fact]
    public void DoubleStart_IsIdempotent()
    {
        var watcher = CreateWatcher();
        watcher.Start();
        watcher.Start(); // Should not throw
        Assert.True(watcher.IsMonitoring);
        watcher.Stop();
    }

    [Fact]
    public void Stop_WithoutStart_DoesNotThrow()
    {
        var watcher = CreateWatcher();
        watcher.Stop();
        Assert.False(watcher.IsMonitoring);
    }

    // ─── Content Change Detection ───

    [Fact]
    public void ClipboardChanged_FiresOnNewContent()
    {
        _clipboardProvider.NextContent = new ClipboardContent
        {
            Text = "hello world",
            CapturedAt = DateTimeOffset.UtcNow
        };

        var watcher = CreateWatcher();
        ClipboardChangedEventArgs? captured = null;
        watcher.ClipboardChanged += (_, args) => captured = args;

        watcher.Start();
        Thread.Sleep(200);
        watcher.Stop();

        Assert.NotNull(captured);
        Assert.Equal("hello world", captured!.Content.Text);
    }

    [Fact]
    public void ClipboardChanged_DoesNotFireOnSameContent()
    {
        _clipboardProvider.NextContent = new ClipboardContent
        {
            Text = "same content",
            CapturedAt = DateTimeOffset.UtcNow
        };

        var watcher = CreateWatcher();
        var fireCount = 0;
        watcher.ClipboardChanged += (_, _) => fireCount++;

        watcher.Start();
        Thread.Sleep(500); // Multiple polls with same content
        watcher.Stop();

        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void ClipboardChanged_FiresWhenContentChanges()
    {
        _clipboardProvider.NextContent = new ClipboardContent
        {
            Text = "first",
            CapturedAt = DateTimeOffset.UtcNow
        };

        var watcher = CreateWatcher();
        var fireCount = 0;
        watcher.ClipboardChanged += (_, _) => fireCount++;

        watcher.Start();
        Thread.Sleep(150);

        _clipboardProvider.NextContent = new ClipboardContent
        {
            Text = "second",
            CapturedAt = DateTimeOffset.UtcNow
        };
        Thread.Sleep(200);
        watcher.Stop();

        Assert.Equal(2, fireCount);
    }

    // ─── Exclusion List ───

    [Fact]
    public void ExcludedApp_SkipsClipboardCapture()
    {
        _exclusionList.Add("password_manager");
        _activeWindowProvider.NextWindow = new ActiveWindowInfo
        {
            ProcessName = "password_manager",
            WindowTitle = "KeePass",
            ProcessId = 1234
        };
        _clipboardProvider.NextContent = new ClipboardContent
        {
            Text = "secret password",
            CapturedAt = DateTimeOffset.UtcNow
        };

        var watcher = CreateWatcher();
        var fireCount = 0;
        watcher.ClipboardChanged += (_, _) => fireCount++;

        watcher.Start();
        Thread.Sleep(200);
        watcher.Stop();

        Assert.Equal(0, fireCount);
    }

    [Fact]
    public void NonExcludedApp_AllowsClipboardCapture()
    {
        _activeWindowProvider.NextWindow = new ActiveWindowInfo
        {
            ProcessName = "chrome",
            WindowTitle = "Google",
            ProcessId = 1234
        };
        _clipboardProvider.NextContent = new ClipboardContent
        {
            Text = "normal content",
            CapturedAt = DateTimeOffset.UtcNow
        };

        var watcher = CreateWatcher();
        var fireCount = 0;
        watcher.ClipboardChanged += (_, _) => fireCount++;

        watcher.Start();
        Thread.Sleep(200);
        watcher.Stop();

        Assert.Equal(1, fireCount);
    }

    // ─── Empty Content ───

    [Fact]
    public void EmptyContent_DoesNotFire()
    {
        _clipboardProvider.NextContent = new ClipboardContent
        {
            Text = "",
            CapturedAt = DateTimeOffset.UtcNow
        };

        var watcher = CreateWatcher();
        var fireCount = 0;
        watcher.ClipboardChanged += (_, _) => fireCount++;

        watcher.Start();
        Thread.Sleep(200);
        watcher.Stop();

        Assert.Equal(0, fireCount);
    }

    [Fact]
    public void NullContent_DoesNotFire()
    {
        _clipboardProvider.NextContent = null;

        var watcher = CreateWatcher();
        var fireCount = 0;
        watcher.ClipboardChanged += (_, _) => fireCount++;

        watcher.Start();
        Thread.Sleep(200);
        watcher.Stop();

        Assert.Equal(0, fireCount);
    }

    // ─── Provider Exception ───

    [Fact]
    public void ProviderThrows_DoesNotCrashWatcher()
    {
        _clipboardProvider.ThrowOnGet = true;

        var watcher = CreateWatcher();
        watcher.Start();
        Thread.Sleep(200);
        watcher.Stop();
        // No exception = graceful handling
    }

    // ─── Dispose ───

    [Fact]
    public void Dispose_StopsWatching()
    {
        var watcher = CreateWatcher();
        watcher.Start();
        watcher.Dispose();
        Assert.False(watcher.IsMonitoring);
    }

    [Fact]
    public void DoubleDispose_DoesNotThrow()
    {
        var watcher = CreateWatcher();
        watcher.Dispose();
        watcher.Dispose();
    }

    // ─── GetCurrentContent ───

    [Fact]
    public void GetCurrentContent_DelegatesToProvider()
    {
        _clipboardProvider.NextContent = new ClipboardContent { Text = "test", CapturedAt = DateTimeOffset.UtcNow };
        var watcher = CreateWatcher();
        var content = watcher.GetCurrentContent();
        Assert.NotNull(content);
        Assert.Equal("test", content!.Text);
    }
}

// ─── Mock Providers ───

public class MockClipboardProvider : IClipboardProvider
{
    public ClipboardContent? NextContent { get; set; }
    public bool ThrowOnGet { get; set; }
    public bool IsMonitoring => false;

    public ClipboardContent? GetCurrentContent()
    {
        if (ThrowOnGet) throw new InvalidOperationException("Mock error");
        return NextContent;
    }

    public void Start() { }
    public void Stop() { }
    public void Dispose() { }
    public event EventHandler<ClipboardChangedEventArgs>? ClipboardChanged;
}

public class MockActiveWindowProviderForClipboard : IActiveWindowProvider
{
    public ActiveWindowInfo? NextWindow { get; set; }

    public ActiveWindowInfo? GetActiveWindowInfo() => NextWindow;
    public void Dispose() { }
}
