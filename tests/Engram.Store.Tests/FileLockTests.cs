using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for file-level locking.
/// </summary>
public class FileLockTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void Acquire_AndDispose_ReleasesLock()
    {
        var targetPath = Path.Combine(_workspace.Root, "test.json");
        Directory.CreateDirectory(_workspace.Root);
        File.WriteAllText(targetPath, "{}");

        using (var fileLock = FileLock.Acquire(targetPath))
        {
            Assert.NotNull(fileLock);
        }

        // Lock should be released, can acquire again
        using var fileLock2 = FileLock.Acquire(targetPath);
        Assert.NotNull(fileLock2);
    }

    [Fact]
    public void Acquire_Twice_ThrowsTimeout()
    {
        var targetPath = Path.Combine(_workspace.Root, "test.json");
        Directory.CreateDirectory(_workspace.Root);
        File.WriteAllText(targetPath, "{}");

        using var fileLock = FileLock.Acquire(targetPath);

        Assert.Throws<TimeoutException>(() =>
        {
            using var fileLock2 = FileLock.Acquire(targetPath, TimeSpan.FromMilliseconds(200));
        });
    }

    [Fact]
    public void Acquire_LockFileDeletedOnDispose()
    {
        var targetPath = Path.Combine(_workspace.Root, "test.json");
        Directory.CreateDirectory(_workspace.Root);
        File.WriteAllText(targetPath, "{}");

        using (var fileLock = FileLock.Acquire(targetPath))
        {
            Assert.True(File.Exists(targetPath + ".lock"));
        }

        Assert.False(File.Exists(targetPath + ".lock"));
    }
}
