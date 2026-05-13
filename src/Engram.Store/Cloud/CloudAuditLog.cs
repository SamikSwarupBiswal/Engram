using System.Text.Json;

namespace Engram.Store.Cloud;

/// <summary>
/// Append-only JSONL audit log for all cloud model calls.
/// Writes to .engram/logs/cloud-audit.jsonl.
/// Thread-safe via file locking.
/// </summary>
public class CloudAuditLog : IDisposable
{
    private readonly string _logPath;
    private readonly object _lock = new();
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    public CloudAuditLog(string logsDirectory)
    {
        ArgumentNullException.ThrowIfNull(logsDirectory);
        Directory.CreateDirectory(logsDirectory);
        _logPath = Path.Combine(logsDirectory, "cloud-audit.jsonl");
    }

    /// <summary>
    /// Append an audit entry to the log. Thread-safe, append-only.
    /// </summary>
    public void Log(CloudAuditEntry entry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(entry);

        var json = JsonSerializer.Serialize(entry, JsonOptions);

        lock (_lock)
        {
            File.AppendAllText(_logPath, json + Environment.NewLine);
        }
    }

    /// <summary>
    /// Read all audit entries from the log.
    /// </summary>
    public IReadOnlyList<CloudAuditEntry> ReadAll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!File.Exists(_logPath))
            return Array.Empty<CloudAuditEntry>();

        lock (_lock)
        {
            var lines = File.ReadAllLines(_logPath);
            var entries = new List<CloudAuditEntry>();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var entry = JsonSerializer.Deserialize<CloudAuditEntry>(line, JsonOptions);
                if (entry is not null) entries.Add(entry);
            }

            return entries;
        }
    }

    /// <summary>
    /// Get the total cost from all logged entries.
    /// </summary>
    public decimal GetTotalCost()
    {
        return ReadAll().Sum(e => e.CostUsd);
    }

    /// <summary>
    /// Get entries within a date range.
    /// </summary>
    public IReadOnlyList<CloudAuditEntry> GetEntriesInRange(DateTimeOffset from, DateTimeOffset to)
    {
        return ReadAll().Where(e => e.Timestamp >= from && e.Timestamp <= to).ToList();
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
