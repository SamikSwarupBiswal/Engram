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
        ObjectDisposedException.ThrowIf(_disposed, this);
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
        ObjectDisposedException.ThrowIf(_disposed, this);
        var entry = new WalEntry
        {
            Operation = "commit",
            EventId = eventId,
            Timestamp = DateTimeOffset.UtcNow
        };

        AppendEntry(entry);
    }

    /// <summary>
    /// Append a transaction start entry to the WAL.
    /// </summary>
    public void LogTransactionStart(Guid transactionId, List<WalTransactionOperation> operations)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var entry = new WalEntry
        {
            Operation = "tx_start",
            EventId = transactionId.ToString(),
            TxOperations = operations,
            Timestamp = DateTimeOffset.UtcNow
        };

        AppendEntry(entry);
    }

    /// <summary>
    /// Append a transaction commit entry to the WAL.
    /// </summary>
    public void LogTransactionCommit(Guid transactionId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var entry = new WalEntry
        {
            Operation = "tx_commit",
            EventId = transactionId.ToString(),
            Timestamp = DateTimeOffset.UtcNow
        };

        AppendEntry(entry);
    }

    /// <summary>
    /// Replay WAL on startup to get uncommitted transactions.
    /// </summary>
    public IReadOnlyList<WalEntry> GetUncommittedTransactions()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!File.Exists(_walPath))
            return Array.Empty<WalEntry>();

        var txs = new Dictionary<string, WalEntry>();
        var commits = new HashSet<string>();

        try
        {
            foreach (var line in File.ReadLines(_walPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var entry = JsonSerializer.Deserialize<WalEntry>(line, JsonOptions);
                if (entry == null) continue;

                if (entry.Operation == "tx_start")
                    txs[entry.EventId] = entry;
                else if (entry.Operation == "tx_commit")
                    commits.Add(entry.EventId);
            }
        }
        catch
        {
            // Corrupted line - skip or treat as uncommitted
        }

        return txs.Values
            .Where(t => !commits.Contains(t.EventId))
            .ToList();
    }

    /// <summary>
    /// Replay WAL on startup. Returns list of uncommitted writes that need recovery.
    /// </summary>
    public IReadOnlyList<WalEntry> GetUncommittedWrites()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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
        ObjectDisposedException.ThrowIf(_disposed, this);
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

    [System.Text.Json.Serialization.JsonPropertyName("tx_operations")]
    public List<WalTransactionOperation>? TxOperations { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }
}

public class WalTransactionOperation
{
    [System.Text.Json.Serialization.JsonPropertyName("file_path")]
    public string FilePath { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("previous_content")]
    public string? PreviousContent { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("new_content")]
    public string NewContent { get; set; } = string.Empty;
}
