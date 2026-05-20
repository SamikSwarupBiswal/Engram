using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Metabolism;

/// <summary>
/// Persistent contradiction history graph.
/// 
/// Contradictions are NOT ephemeral detections. They are longitudinal behavioral records.
/// Without persistence, Engram repeatedly rediscovers contradictions from scratch.
/// With persistence, patterns accumulate over time — genuine continuity.
/// 
/// Example: "Deep work contradiction seen 12 times, worsening over 3 weeks, linked to YouTube activity"
/// 
/// Stored in .engram/config/contradiction_history.json.
/// </summary>
public class ContradictionHistoryStore : IDisposable
{
    private readonly string _storePath;
    private readonly ILogger<ContradictionHistoryStore>? _logger;
    private readonly object _lock = new();
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public ContradictionHistoryStore(WorkspacePaths paths, ILogger<ContradictionHistoryStore>? logger = null)
    {
        _storePath = Path.Combine(paths.Config, "contradiction_history.json");
        _logger = logger;
    }

    /// <summary>
    /// Record a contradiction detection. Creates or updates a ContradictionHistoryEntry.
    /// </summary>
    public void Record(BehavioralContradiction contradiction)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_lock)
        {
            var history = LoadAllInternal();
            var existing = history.FirstOrDefault(h =>
                h.Type == contradiction.Type &&
                h.DeclaredIntent == contradiction.DeclaredIntent);

            if (existing != null)
            {
                // Update existing record
                existing.Observations.Add(new ContradictionObservation
                {
                    ObservedAt = DateTimeOffset.UtcNow,
                    Severity = contradiction.Severity,
                    ObservedBehavior = contradiction.ObservedBehavior,
                    Description = contradiction.Description
                });
                existing.LastSeenAt = DateTimeOffset.UtcNow;
                existing.ObservationCount++;
                existing.CurrentSeverity = contradiction.Severity;

                // Update trend
                existing.Trend = ComputeTrend(existing);
            }
            else
            {
                // Create new record
                history.Add(new ContradictionHistoryEntry
                {
                    ContradictionId = Guid.NewGuid().ToString("n")[..12],
                    Type = contradiction.Type,
                    DeclaredIntent = contradiction.DeclaredIntent,
                    FirstSeenAt = DateTimeOffset.UtcNow,
                    LastSeenAt = DateTimeOffset.UtcNow,
                    ObservationCount = 1,
                    CurrentSeverity = contradiction.Severity,
                    Trend = ContradictionTrend.Stable,
                    Status = ContradictionStatus.Active,
                    RelatedNodeIds = contradiction.RelatedNodeIds,
                    Observations = new List<ContradictionObservation>
                    {
                        new()
                        {
                            ObservedAt = DateTimeOffset.UtcNow,
                            Severity = contradiction.Severity,
                            ObservedBehavior = contradiction.ObservedBehavior,
                            Description = contradiction.Description
                        }
                    }
                });
            }

            SaveAll(history);
        }
    }

    /// <summary>
    /// Load all contradiction records.
    /// </summary>
    public List<ContradictionHistoryEntry> LoadAll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_lock)
        {
            return LoadAllInternal();
        }
    }

    /// <summary>
    /// Load active (unresolved) contradictions.
    /// </summary>
    public List<ContradictionHistoryEntry> LoadActive()
    {
        return LoadAll().Where(h => h.Status == ContradictionStatus.Active).ToList();
    }

    /// <summary>
    /// Load contradictions by type.
    /// </summary>
    public List<ContradictionHistoryEntry> LoadByType(ContradictionType type)
    {
        return LoadAll().Where(h => h.Type == type).ToList();
    }

    /// <summary>
    /// Load escalating contradictions (trend is worsening).
    /// </summary>
    public List<ContradictionHistoryEntry> LoadEscalating()
    {
        return LoadAll().Where(h => h.Trend == ContradictionTrend.Worsening && h.Status == ContradictionStatus.Active).ToList();
    }

    /// <summary>
    /// Mark a contradiction as resolved.
    /// </summary>
    public void Resolve(string contradictionId, string resolution = "")
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_lock)
        {
            var history = LoadAllInternal();
            var record = history.FirstOrDefault(h => h.ContradictionId == contradictionId);
            if (record != null)
            {
                record.Status = ContradictionStatus.Resolved;
                record.ResolvedAt = DateTimeOffset.UtcNow;
                record.Resolution = resolution;
                SaveAll(history);
                _logger?.LogInformation("Contradiction resolved: {Id}", contradictionId);
            }
        }
    }

    /// <summary>
    /// Get statistics about contradiction history.
    /// </summary>
    public ContradictionHistoryStats GetStats()
    {
        var all = LoadAll();
        return new ContradictionHistoryStats
        {
            TotalRecords = all.Count,
            ActiveCount = all.Count(h => h.Status == ContradictionStatus.Active),
            ResolvedCount = all.Count(h => h.Status == ContradictionStatus.Resolved),
            EscalatingCount = all.Count(h => h.Trend == ContradictionTrend.Worsening),
            AverageObservations = all.Count > 0 ? all.Average(h => h.ObservationCount) : 0,
            OldestActive = all.Where(h => h.Status == ContradictionStatus.Active)
                .OrderBy(h => h.FirstSeenAt)
                .FirstOrDefault()?.FirstSeenAt
        };
    }

    /// <summary>
    /// Compute the trend of a contradiction based on recent observations.
    /// </summary>
    private static ContradictionTrend ComputeTrend(ContradictionHistoryEntry record)
    {
        if (record.Observations.Count < 2)
            return ContradictionTrend.Stable;

        var recent = record.Observations.OrderByDescending(o => o.ObservedAt).Take(5).ToList();
        var severityValues = recent.Select(o => (int)o.Severity).ToList();

        // Check if severity is increasing
        bool increasing = true;
        for (int i = 0; i < severityValues.Count - 1; i++)
        {
            if (severityValues[i] < severityValues[i + 1])
            {
                increasing = false;
                break;
            }
        }

        if (increasing && severityValues.First() > severityValues.Last())
            return ContradictionTrend.Worsening;

        // Check if severity is decreasing
        bool decreasing = true;
        for (int i = 0; i < severityValues.Count - 1; i++)
        {
            if (severityValues[i] > severityValues[i + 1])
            {
                decreasing = false;
                break;
            }
        }

        if (decreasing && severityValues.First() < severityValues.Last())
            return ContradictionTrend.Improving;

        // Check for recurring pattern (same severity repeating)
        if (severityValues.All(s => s == severityValues[0]))
            return ContradictionTrend.Recurring;

        return ContradictionTrend.Stable;
    }

    private List<ContradictionHistoryEntry> LoadAllInternal()
    {
        if (!File.Exists(_storePath))
            return new List<ContradictionHistoryEntry>();

        try
        {
            var json = File.ReadAllText(_storePath);
            return JsonSerializer.Deserialize<List<ContradictionHistoryEntry>>(json, JsonOptions) ?? new List<ContradictionHistoryEntry>();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load contradiction history, returning empty");
            return new List<ContradictionHistoryEntry>();
        }
    }

    private void SaveAll(List<ContradictionHistoryEntry> history)
    {
        var dir = Path.GetDirectoryName(_storePath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(history, JsonOptions);
        File.WriteAllText(_storePath, json);
    }

    public void Dispose()
    {
        _disposed = true;
    }
}

/// <summary>
/// A persistent record of a contradiction pattern.
/// Tracks observations over time, trend, and resolution.
/// </summary>
public class ContradictionHistoryEntry
{
    public string ContradictionId { get; set; } = string.Empty;
    public ContradictionType Type { get; set; }
    public string DeclaredIntent { get; set; } = string.Empty;
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public int ObservationCount { get; set; }
    public ContradictionSeverity CurrentSeverity { get; set; }
    public ContradictionTrend Trend { get; set; }
    public ContradictionStatus Status { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string Resolution { get; set; } = string.Empty;
    public List<string> RelatedNodeIds { get; set; } = new();
    public List<ContradictionObservation> Observations { get; set; } = new();
}

public class ContradictionObservation
{
    public DateTimeOffset ObservedAt { get; set; }
    public ContradictionSeverity Severity { get; set; }
    public string ObservedBehavior { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public enum ContradictionTrend
{
    Stable,
    Worsening,
    Improving,
    Recurring
}

public enum ContradictionStatus
{
    Active,
    Resolved,
    Suppressed
}

public class ContradictionHistoryStats
{
    public int TotalRecords { get; set; }
    public int ActiveCount { get; set; }
    public int ResolvedCount { get; set; }
    public int EscalatingCount { get; set; }
    public double AverageObservations { get; set; }
    public DateTimeOffset? OldestActive { get; set; }
}
