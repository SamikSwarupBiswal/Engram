using System.Text.Json;

namespace Engram.Store;

/// <summary>
/// Write-ahead log for crash recovery.
/// Before writing an event, append a WAL entry. After commit, append commit marker.
/// On startup, replay WAL to recover incomplete writes.
/// </summary>
public class WriteAheadLog : IDisposable
{
    private readonly string _walPath;
    private readonly object _writeLock = new();
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    public WriteAheadLog(string rawDirectory)
    {
        _walPath = Path.Combine(rawDirectory, ".wal");
    }

    /// <summary>
    /// Append a "write" entry before writing the event file.
    /// </summary>
    public void LogWrite(string eventId, string hash, string filePath)
    {
        var entry = new WalEntry
        {
            Operation = "write",
            EventId = eventId,
            Hash = hash,
            FilePath = filePath,
            Timestamp = DateTimeOffset.UtcNow
        };

        AppendEntry(entry);
    }

    /// <summary>
    /// Append a "commit" entry after successful write.
    /// </summary>
    public void LogCommit(string eventId)
    {
        var entry = new WalEntry
        {
            Operation = "commit",
            EventId = eventId,
            Timestamp = DateTimeOffset.UtcNow
        };

        AppendEntry(entry);
    }

    /// <summary>
    /// Replay WAL on startup. Returns list of uncommitted writes that need recovery.
    /// </summary>
    public IReadOnlyList<WalEntry> GetUncommittedWrites()
    {
        if (!File.Exists(_walPath))
            return Array.Empty<WalEntry>();

        var writes = new Dictionary<string, WalEntry>();
        var commits = new HashSet<string>();

        try
        {
            foreach (var line in File.ReadLines(_walPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var entry = JsonSerializer.Deserialize<WalEntry>(line, JsonOptions);
                if (entry == null) continue;

                if (entry.Operation == "write")
                    writes[entry.EventId] = entry;
                else if (entry.Operation == "commit")
                    commits.Add(entry.EventId);
            }
        }
        catch
        {
            // Corrupted WAL — treat all as uncommitted
        }

        return writes.Values
            .Where(w => !commits.Contains(w.EventId))
            .ToList();
    }

    /// <summary>
    /// Clean the WAL file after successful recovery.
    /// </summary>
    public void Clear()
    {
        lock (_writeLock)
        {
            if (File.Exists(_walPath))
                File.Delete(_walPath);
        }
    }

    private void AppendEntry(WalEntry entry)
    {
        lock (_writeLock)
        {
            var json = JsonSerializer.Serialize(entry, JsonOptions);
            File.AppendAllText(_walPath, json + Environment.NewLine);
        }
    }

    public void Dispose()
    {
        _disposed = true;
    }
}

public class WalEntry
{
    [System.Text.Json.Serialization.JsonPropertyName("operation")]
    public string Operation { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("event_id")]
    public string EventId { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("hash")]
    public string? Hash { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("file_path")]
    public string? FilePath { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }
}
