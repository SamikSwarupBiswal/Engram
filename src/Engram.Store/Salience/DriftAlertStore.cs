using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Salience;

/// <summary>
/// Persists and manages drift alerts.
/// Stored in .engram/config/drift_alerts.json.
/// </summary>
public class DriftAlertStore : IDisposable
{
    private readonly string _alertsPath;
    private readonly ILogger<DriftAlertStore>? _logger;
    private readonly object _lock = new();
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public DriftAlertStore(WorkspacePaths paths, ILogger<DriftAlertStore>? logger = null)
    {
        _alertsPath = Path.Combine(paths.Config, "drift_alerts.json");
        _logger = logger;
    }

    /// <summary>
    /// Save a drift alert.
    /// </summary>
    public void Save(DriftAlert alert)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_lock)
        {
            var alerts = LoadAllInternal();
            alerts.Add(alert);
            SaveAll(alerts);
            _logger?.LogInformation("Drift alert saved: {AlertId} for node {NodeId}", alert.AlertId, alert.NodeId);
        }
    }

    /// <summary>
    /// Save multiple drift alerts.
    /// </summary>
    public void SaveBatch(IEnumerable<DriftAlert> alerts)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_lock)
        {
            var existing = LoadAllInternal();
            existing.AddRange(alerts);
            SaveAll(existing);
            _logger?.LogInformation("Saved {Count} drift alerts", alerts.Count());
        }
    }

    /// <summary>
    /// Load all drift alerts.
    /// </summary>
    public IReadOnlyList<DriftAlert> LoadAll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_lock) { return LoadAllInternal(); }
    }

    /// <summary>
    /// Load pending alerts only.
    /// </summary>
    public IReadOnlyList<DriftAlert> LoadPending()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_lock) { return LoadAllInternal().Where(a => a.Status == DriftAlertStatus.Pending).ToList(); }
    }

    /// <summary>
    /// Dismiss an alert (mark as false positive).
    /// </summary>
    public bool Dismiss(string alertId)
    {
        return UpdateStatus(alertId, DriftAlertStatus.Dismissed, "Dismissed by user");
    }

    /// <summary>
    /// Accept an alert (confirm drift).
    /// </summary>
    public bool Accept(string alertId)
    {
        return UpdateStatus(alertId, DriftAlertStatus.Accepted, "Accepted by user");
    }

    /// <summary>
    /// Convert an alert (drift resolved, wiki updated).
    /// </summary>
    public bool Convert(string alertId, string? resolution = null)
    {
        return UpdateStatus(alertId, DriftAlertStatus.Converted, resolution ?? "Converted to wiki update");
    }

    /// <summary>
    /// Get alert statistics.
    /// </summary>
    public DriftAlertStats GetStats()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var alerts = LoadAllInternal();
        return new DriftAlertStats
        {
            Total = alerts.Count,
            Pending = alerts.Count(a => a.Status == DriftAlertStatus.Pending),
            Dismissed = alerts.Count(a => a.Status == DriftAlertStatus.Dismissed),
            Accepted = alerts.Count(a => a.Status == DriftAlertStatus.Accepted),
            Converted = alerts.Count(a => a.Status == DriftAlertStatus.Converted)
        };
    }

    private bool UpdateStatus(string alertId, DriftAlertStatus status, string resolution)
    {
        lock (_lock)
        {
            var alerts = LoadAllInternal();
            var alert = alerts.FirstOrDefault(a => a.AlertId == alertId);
            if (alert == null) return false;

            alert.Status = status;
            alert.Resolution = resolution;
            alert.ResolvedAt = DateTimeOffset.UtcNow;

            SaveAll(alerts);
            _logger?.LogInformation("Drift alert {AlertId} updated to {Status}", alertId, status);
            return true;
        }
    }

    private List<DriftAlert> LoadAllInternal()
    {
        if (!File.Exists(_alertsPath)) return new List<DriftAlert>();
        try
        {
            var json = File.ReadAllText(_alertsPath);
            return JsonSerializer.Deserialize<List<DriftAlert>>(json, JsonOptions) ?? new List<DriftAlert>();
        }
        catch { return new List<DriftAlert>(); }
    }

    private void SaveAll(List<DriftAlert> alerts)
    {
        var dir = Path.GetDirectoryName(_alertsPath);
        if (dir != null) Directory.CreateDirectory(dir);

        var tmpPath = _alertsPath + ".tmp";
        var json = JsonSerializer.Serialize(alerts, JsonOptions);
        File.WriteAllText(tmpPath, json);
        File.Move(tmpPath, _alertsPath, overwrite: true);
    }

    public void Dispose() { _disposed = true; }
}

public class DriftAlertStats
{
    public int Total { get; set; }
    public int Pending { get; set; }
    public int Dismissed { get; set; }
    public int Accepted { get; set; }
    public int Converted { get; set; }
}
