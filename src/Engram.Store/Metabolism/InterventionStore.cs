using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Metabolism;

/// <summary>
/// Persistent storage for interventions.
/// 
/// Interventions are NOT ephemeral outputs. They are first-class semantic entities.
/// Without persistence, Engram forgets its own reflections.
/// 
/// Stored in .engram/config/interventions.json.
/// </summary>
public class InterventionStore : IDisposable
{
    private readonly string _storePath;
    private readonly ILogger<InterventionStore>? _logger;
    private readonly object _lock = new();
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public InterventionStore(WorkspacePaths paths, ILogger<InterventionStore>? logger = null)
    {
        _storePath = Path.Combine(paths.Config, "interventions.json");
        _logger = logger;
    }

    /// <summary>
    /// Save an intervention. Updates existing if same InterventionId.
    /// </summary>
    public void Save(Intervention intervention)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_lock)
        {
            var interventions = LoadAllInternal();
            var existing = interventions.FindIndex(i => i.InterventionId == intervention.InterventionId);
            if (existing >= 0)
                interventions[existing] = intervention;
            else
                interventions.Add(intervention);

            SaveAll(interventions);
            _logger?.LogDebug("Intervention saved: {Id} ({Type})", intervention.InterventionId, intervention.Type);
        }
    }

    /// <summary>
    /// Load all interventions.
    /// </summary>
    public List<Intervention> LoadAll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_lock)
        {
            return LoadAllInternal();
        }
    }

    /// <summary>
    /// Load interventions filtered by status.
    /// </summary>
    public List<Intervention> LoadByStatus(InterventionStatus status)
    {
        return LoadAll().Where(i => i.Status == status).ToList();
    }

    /// <summary>
    /// Load recent interventions (within timespan).
    /// </summary>
    public List<Intervention> LoadRecent(TimeSpan within)
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(within);
        return LoadAll().Where(i => i.GeneratedAt >= cutoff).ToList();
    }

    /// <summary>
    /// Load interventions related to a specific node.
    /// </summary>
    public List<Intervention> LoadByNode(string nodeId)
    {
        return LoadAll().Where(i => i.RelatedNodeId == nodeId).ToList();
    }

    /// <summary>
    /// Get intervention statistics.
    /// </summary>
    public InterventionStoreStats GetStats()
    {
        var all = LoadAll();
        return new InterventionStoreStats
        {
            TotalCount = all.Count,
            PendingCount = all.Count(i => i.Status == InterventionStatus.Pending),
            AcknowledgedCount = all.Count(i => i.Status == InterventionStatus.Acknowledged),
            DismissedCount = all.Count(i => i.Status == InterventionStatus.Dismissed),
            ActedCount = all.Count(i => i.Status == InterventionStatus.Acted),
            OldestPending = all.Where(i => i.Status == InterventionStatus.Pending)
                .OrderBy(i => i.GeneratedAt)
                .FirstOrDefault()?.GeneratedAt,
            NewestGenerated = all.OrderByDescending(i => i.GeneratedAt)
                .FirstOrDefault()?.GeneratedAt
        };
    }

    /// <summary>
    /// Prune old interventions (keep last N or within timespan).
    /// </summary>
    public int Prune(int keepLast = 500, TimeSpan? olderThan = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_lock)
        {
            var all = LoadAllInternal();
            var before = all.Count;

            if (olderThan.HasValue)
            {
                var cutoff = DateTimeOffset.UtcNow.Subtract(olderThan.Value);
                all = all.Where(i => i.GeneratedAt >= cutoff).ToList();
            }

            if (all.Count > keepLast)
            {
                all = all.OrderByDescending(i => i.GeneratedAt).Take(keepLast).ToList();
            }

            SaveAll(all);
            var pruned = before - all.Count;
            if (pruned > 0)
                _logger?.LogInformation("Pruned {Count} old interventions", pruned);
            return pruned;
        }
    }

    private List<Intervention> LoadAllInternal()
    {
        if (!File.Exists(_storePath))
            return new List<Intervention>();

        try
        {
            var json = File.ReadAllText(_storePath);
            return JsonSerializer.Deserialize<List<Intervention>>(json, JsonOptions) ?? new List<Intervention>();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load interventions, returning empty");
            return new List<Intervention>();
        }
    }

    private void SaveAll(List<Intervention> interventions)
    {
        var dir = Path.GetDirectoryName(_storePath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(interventions, JsonOptions);
        File.WriteAllText(_storePath, json);
    }

    public void Dispose()
    {
        _disposed = true;
    }
}

public class InterventionStoreStats
{
    public int TotalCount { get; set; }
    public int PendingCount { get; set; }
    public int AcknowledgedCount { get; set; }
    public int DismissedCount { get; set; }
    public int ActedCount { get; set; }
    public DateTimeOffset? OldestPending { get; set; }
    public DateTimeOffset? NewestGenerated { get; set; }
}
