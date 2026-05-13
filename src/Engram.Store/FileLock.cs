namespace Engram.Store;

/// <summary>
/// Provides file-level locking using .lock files.
/// Prevents concurrent writes to the same target file.
/// Cross-platform: works on both Windows and Linux/WSL.
/// </summary>
public sealed class FileLock : IDisposable
{
    private readonly string _lockPath;
    private bool _disposed;

    private FileLock(string lockPath)
    {
        _lockPath = lockPath;
    }

    /// <summary>
    /// Acquire a file lock with timeout. Throws TimeoutException if lock cannot be acquired.
    /// Uses exclusive file creation for cross-platform atomic locking.
    /// </summary>
    public static FileLock Acquire(string targetPath, TimeSpan? timeout = null)
    {
        var lockPath = targetPath + ".lock";
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(10);
        var deadline = DateTime.UtcNow + effectiveTimeout;

        var dir = Path.GetDirectoryName(lockPath);
        if (dir != null) Directory.CreateDirectory(dir);

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                // Use FileMode.CreateNew for atomic exclusive creation
                using var stream = new FileStream(
                    lockPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None);

                // Write PID for debugging
                var pid = Environment.ProcessId;
                var bytes = System.Text.Encoding.UTF8.GetBytes(pid.ToString());
                stream.Write(bytes, 0, bytes.Length);

                return new FileLock(lockPath);
            }
            catch (IOException)
            {
                // Lock file exists, another process holds it
                Thread.Sleep(50);
            }
        }

        throw new TimeoutException($"Could not acquire file lock for '{targetPath}' within {effectiveTimeout.TotalSeconds}s.");
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            try
            {
                if (File.Exists(_lockPath))
                    File.Delete(_lockPath);
            }
            catch
            {
                // Best effort cleanup
            }
            _disposed = true;
        }
    }
}
