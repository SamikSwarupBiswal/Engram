using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for orphaned .tmp file cleanup on workspace init.
/// </summary>
public class OrphanCleanupTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void Cleanup_RemovesOldTmpFiles()
    {
        var init = new WorkspaceInitializer();
        init.Initialize(_workspace.Paths);

        // Create orphaned .tmp files
        var dateDir = Path.Combine(_workspace.Paths.Raw, "2026-05-13");
        Directory.CreateDirectory(dateDir);
        var tmpFile = Path.Combine(dateDir, "orphan.tmp");
        File.WriteAllText(tmpFile, "partial write");

        // Set last write time to 2 hours ago
        File.SetLastWriteTimeUtc(tmpFile, DateTime.UtcNow.AddHours(-2));

        // Re-init should clean up
        var cleaned = init.CleanupOrphanedTempFiles(_workspace.Paths);

        Assert.Equal(1, cleaned);
        Assert.False(File.Exists(tmpFile));
    }

    [Fact]
    public void Cleanup_KeepsRecentTmpFiles()
    {
        var init = new WorkspaceInitializer();
        init.Initialize(_workspace.Paths);

        var dateDir = Path.Combine(_workspace.Paths.Raw, "2026-05-13");
        Directory.CreateDirectory(dateDir);
        var tmpFile = Path.Combine(dateDir, "recent.tmp");
        File.WriteAllText(tmpFile, "in-progress write");

        // Set last write time to now (should NOT be cleaned)
        File.SetLastWriteTimeUtc(tmpFile, DateTime.UtcNow);

        var cleaned = init.CleanupOrphanedTempFiles(_workspace.Paths);

        Assert.Equal(0, cleaned);
        Assert.True(File.Exists(tmpFile));
    }

    [Fact]
    public void Init_RunCleanupAutomatically()
    {
        var dateDir = Path.Combine(_workspace.Paths.Raw, "2026-01-01");

        // Create workspace first
        var init = new WorkspaceInitializer();
        init.Initialize(_workspace.Paths);

        // Create orphaned .tmp
        Directory.CreateDirectory(dateDir);
        var tmpFile = Path.Combine(dateDir, "old.tmp");
        File.WriteAllText(tmpFile, "crashed write");
        File.SetLastWriteTimeUtc(tmpFile, DateTime.UtcNow.AddHours(-2));

        // Re-init should auto-cleanup
        init.Initialize(_workspace.Paths);

        Assert.False(File.Exists(tmpFile));
    }
}
