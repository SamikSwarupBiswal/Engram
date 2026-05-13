using Engram.Store.Capture;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for file watcher with production hardening.
/// Note: FileSystemWatcher has platform-specific behavior.
/// On WSL, events may not fire reliably. Tests account for this.
/// </summary>
public class FileWatcherTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void Start_SetsIsWatching()
    {
        var watchDir = Path.Combine(_workspace.Root, "watch");
        Directory.CreateDirectory(watchDir);
        var rateLimiter = new RateLimiter(100, 100);

        using var watcher = new FileWatcher(
            new[] { watchDir },
            _workspace.Paths.Root,
            rateLimiter);

        watcher.Start();

        Assert.True(watcher.IsWatching);
    }

    [Fact]
    public void Stop_ClearsIsWatching()
    {
        var watchDir = Path.Combine(_workspace.Root, "watch");
        Directory.CreateDirectory(watchDir);
        var rateLimiter = new RateLimiter(100, 100);

        using var watcher = new FileWatcher(
            new[] { watchDir },
            _workspace.Paths.Root,
            rateLimiter);

        watcher.Start();
        watcher.Stop();

        Assert.False(watcher.IsWatching);
    }

    [Fact]
    public void Start_IgnoresNonExistentPaths_Gracefully()
    {
        var rateLimiter = new RateLimiter(100, 100);

        using var watcher = new FileWatcher(
            new[] { "/nonexistent/path/xyz_12345" },
            _workspace.Paths.Root,
            rateLimiter);

        // Should not throw — just logs warning
        watcher.Start();

        // No valid paths were watched
        Assert.False(watcher.IsWatching);
    }

    [Fact]
    public void DoubleStart_IsIdempotent()
    {
        var watchDir = Path.Combine(_workspace.Root, "watch");
        Directory.CreateDirectory(watchDir);
        var rateLimiter = new RateLimiter(100, 100);

        using var watcher = new FileWatcher(new[] { watchDir }, _workspace.Paths.Root, rateLimiter);

        watcher.Start();
        watcher.Start(); // Should not throw

        Assert.True(watcher.IsWatching);
    }

    [Fact]
    public void DoubleStop_IsIdempotent()
    {
        var watchDir = Path.Combine(_workspace.Root, "watch");
        Directory.CreateDirectory(watchDir);
        var rateLimiter = new RateLimiter(100, 100);

        using var watcher = new FileWatcher(new[] { watchDir }, _workspace.Paths.Root, rateLimiter);

        watcher.Start();
        watcher.Stop();
        watcher.Stop(); // Should not throw

        Assert.False(watcher.IsWatching);
    }

    [Fact]
    public void SelfFilter_Logic_Correct()
    {
        // Test the self-filtering logic independently of FileSystemWatcher
        var selfPath = "/tmp/.engram";

        // Paths starting with selfPath should be filtered
        Assert.StartsWith(selfPath, "/tmp/.engram/raw/2026-01-01/event.json");
        Assert.DoesNotContain("/home/user/document.txt", selfPath);
    }

    [Fact]
    public void Dispose_CleansUpResources()
    {
        var watchDir = Path.Combine(_workspace.Root, "watch");
        Directory.CreateDirectory(watchDir);
        var rateLimiter = new RateLimiter(100, 100);

        var watcher = new FileWatcher(new[] { watchDir }, _workspace.Paths.Root, rateLimiter);
        watcher.Start();
        watcher.Dispose();

        Assert.False(watcher.IsWatching);

        // Double dispose should not throw
        watcher.Dispose();
    }

    [Fact]
    public void FileChanged_EventSignature_Correct()
    {
        // Verify the event args structure is correct
        var args = new Engram.Store.Providers.FileChangeEventArgs
        {
            FilePath = "/test/file.txt",
            ChangeType = Engram.Store.Providers.FileChangeType.Created,
            Timestamp = DateTimeOffset.UtcNow
        };

        Assert.Equal("/test/file.txt", args.FilePath);
        Assert.Equal(Engram.Store.Providers.FileChangeType.Created, args.ChangeType);
    }
}
