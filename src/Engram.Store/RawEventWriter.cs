using System.Text.Json;
using Microsoft.Extensions.Logging;
using Engram.Store.Validation;

namespace Engram.Store;

/// <summary>
/// Production-grade raw event writer.
/// Features: atomic writes, hash index dedup, file locking, WAL, input validation, structured logging.
/// </summary>
public class RawEventWriter : IDisposable
{
    private readonly WorkspacePaths _paths;
    private readonly ContentHasher _hasher;
    private readonly HashIndex _hashIndex;
    private readonly WriteAheadLog _wal;
    private readonly ILogger<RawEventWriter>? _logger;
    private readonly SemaphoreSlim _concurrencyLock = new(1, 1);
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public RawEventWriter(WorkspacePaths paths, ContentHasher hasher, ILogger<RawEventWriter>? logger = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
        _logger = logger;
        _hashIndex = new HashIndex(paths.Raw);
        _wal = new WriteAheadLog(paths.Raw);
    }

    /// <summary>
    /// Writes a raw event to the store using atomic write with full crash recovery.
    /// </summary>
    public WriteResult Write(RawEvent rawEvent)
    {
        ArgumentNullException.ThrowIfNull(rawEvent);
        InputValidator.ValidateRawEvent(rawEvent);

        if (Array.Exists(Environment.GetCommandLineArgs(), arg => arg == "--safe-mode") ||
            Environment.GetEnvironmentVariable("ENGRAM_SAFE_MODE") == "true")
        {
            throw new InvalidOperationException("System is running in read-only Safe Mode due to semantic uncertainty.");
        }

        _concurrencyLock.Wait();
        try
        {
            return WriteInternal(rawEvent);
        }
        finally
        {
            _concurrencyLock.Release();
        }
    }

    private WriteResult WriteInternal(RawEvent rawEvent)
    {
        var hash = _hasher.ComputeHash(rawEvent);
        rawEvent.Hash = hash;

        var dateFolder = rawEvent.CapturedAt.ToString("yyyy-MM-dd");
        var dateDir = Path.Combine(_paths.Raw, dateFolder);
        var filePath = Path.Combine(dateDir, $"{rawEvent.EventId}.json");

        _logger?.LogDebug("Writing event {EventId} (hash={Hash}, type={Type})",
            rawEvent.EventId, hash[..16], rawEvent.EventType);

        // O(1) dedup via hash index
        if (_hashIndex.TryGet(hash, out var existingPath) && File.Exists(existingPath))
        {
            _logger?.LogInformation("Duplicate event detected (hash={Hash}), existing={Path}", hash[..16], existingPath);
            return new WriteResult
            {
                Outcome = WriteOutcome.Duplicate,
                EventId = rawEvent.EventId,
                FilePath = existingPath,
                Hash = hash
            };
        }

        // Fallback: scan date directory for legacy events not in index
        if (TryFindDuplicateByHash(dateDir, hash, out var legacyPath))
        {
            _hashIndex.Add(hash, legacyPath); // Backfill index
            _logger?.LogInformation("Duplicate found via scan (hash={Hash}), backfilled index", hash[..16]);
            return new WriteResult
            {
                Outcome = WriteOutcome.Duplicate,
                EventId = rawEvent.EventId,
                FilePath = legacyPath,
                Hash = hash
            };
        }

        // Atomic write with file locking and WAL
        Directory.CreateDirectory(dateDir);

        FileLock? fileLock = null;
        try
        {
            fileLock = FileLock.Acquire(filePath);

            // WAL: log write intent
            _wal.LogWrite(rawEvent.EventId, hash, filePath);

            // Atomic write: .tmp then rename
            var tmpPath = filePath + ".tmp";
            var json = JsonSerializer.Serialize(rawEvent, JsonOptions);
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, filePath, overwrite: false);

            // WAL: log commit
            _wal.LogCommit(rawEvent.EventId);

            // Update hash index
            _hashIndex.Add(hash, filePath);

            _logger?.LogInformation("Event written: {EventId} -> {Path}", rawEvent.EventId, filePath);

            return new WriteResult
            {
                Outcome = WriteOutcome.Created,
                EventId = rawEvent.EventId,
                FilePath = filePath,
                Hash = hash
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to write event {EventId}", rawEvent.EventId);
            throw;
        }
        finally
        {
            fileLock?.Dispose();
        }
    }

    private bool TryFindDuplicateByHash(string dateDir, string hash, out string existingFilePath)
    {
        existingFilePath = string.Empty;

        if (!Directory.Exists(dateDir))
            return false;

        foreach (var file in Directory.EnumerateFiles(dateDir, "*.json"))
        {
            if (file.EndsWith(".meta.json") || file.EndsWith(".tmp"))
                continue;

            try
            {
                var json = File.ReadAllText(file);
                var existing = JsonSerializer.Deserialize<RawEvent>(json, JsonOptions);
                if (existing?.Hash == hash)
                {
                    existingFilePath = file;
                    return true;
                }
            }
            catch
            {
                continue;
            }
        }

        return false;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _concurrencyLock.Dispose();
            _hashIndex.Dispose();
            _wal.Dispose();
            _disposed = true;
        }
    }
}
