using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Search;

/// <summary>
/// Tracks capture system status: which sources are active, event counts, pause/resume.
/// Persisted in .engram/config/capture_state.json.
/// </summary>
public class CaptureStatus
{
    private readonly string _statePath;
    private readonly ILogger<CaptureStatus>? _logger;
    private CaptureState _state;
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public CaptureStatus(WorkspacePaths paths, ILogger<CaptureStatus>? logger = null)
    {
        _statePath = Path.Combine(paths.Config, "capture_state.json");
        _logger = logger;
        _state = LoadState();
    }

    /// <summary>Current capture state.</summary>
    public CaptureState CurrentState
    {
        get { lock (_lock) return _state; }
    }

    /// <summary>Is global capture paused?</summary>
    public bool IsPaused => _state.IsPaused;

    /// <summary>Pause all capture sources.</summary>
    public void Pause()
    {
        lock (_lock)
        {
            _state.IsPaused = true;
            _state.LastPausedAt = DateTimeOffset.UtcNow;
            SaveState();
            _logger?.LogInformation("Capture paused");
        }
    }

    /// <summary>Resume all capture sources.</summary>
    public void Resume()
    {
        lock (_lock)
        {
            _state.IsPaused = false;
            _state.LastResumedAt = DateTimeOffset.UtcNow;
            SaveState();
            _logger?.LogInformation("Capture resumed");
        }
    }

    /// <summary>Record a captured event.</summary>
    public void RecordEvent(string source)
    {
        lock (_lock)
        {
            _state.TotalEventsCaptured++;
            _state.LastEventAt = DateTimeOffset.UtcNow;
            _state.LastEventSource = source;

            if (!_state.EventsBySource.ContainsKey(source))
                _state.EventsBySource[source] = 0;
            _state.EventsBySource[source]++;
        }
    }

    /// <summary>Record a dropped event (rate limited, excluded, etc).</summary>
    public void RecordDrop(string reason)
    {
        lock (_lock)
        {
            _state.TotalEventsDropped++;

            if (!_state.DropsByReason.ContainsKey(reason))
                _state.DropsByReason[reason] = 0;
            _state.DropsByReason[reason]++;
        }
    }

    /// <summary>Enable/disable a specific source.</summary>
    public void SetSourceEnabled(string source, bool enabled)
    {
        lock (_lock)
        {
            _state.SourceStates[source] = enabled;
            SaveState();
            _logger?.LogInformation("Source {Source} {State}", source, enabled ? "enabled" : "disabled");
        }
    }

    /// <summary>Check if a specific source is enabled.</summary>
    public bool IsSourceEnabled(string source)
    {
        lock (_lock)
        {
            if (_state.IsPaused) return false;
            if (_state.SourceStates.TryGetValue(source, out var enabled))
                return enabled;
            return true; // Default: enabled
        }
    }

    /// <summary>Reset all counters.</summary>
    public void ResetCounters()
    {
        lock (_lock)
        {
            _state.TotalEventsCaptured = 0;
            _state.TotalEventsDropped = 0;
            _state.EventsBySource.Clear();
            _state.DropsByReason.Clear();
            SaveState();
        }
    }

    private CaptureState LoadState()
    {
        try
        {
            if (File.Exists(_statePath))
            {
                var json = File.ReadAllText(_statePath);
                return JsonSerializer.Deserialize<CaptureState>(json, JsonOptions) ?? new CaptureState();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load capture state, using defaults");
        }
        return new CaptureState();
    }

    private void SaveState()
    {
        try
        {
            var dir = Path.GetDirectoryName(_statePath);
            if (dir != null) Directory.CreateDirectory(dir);

            var tmpPath = _statePath + ".tmp";
            var json = JsonSerializer.Serialize(_state, JsonOptions);
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, _statePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to save capture state");
        }
    }
}

public class CaptureState
{
    public bool IsPaused { get; set; }
    public long TotalEventsCaptured { get; set; }
    public long TotalEventsDropped { get; set; }
    public DateTimeOffset? LastEventAt { get; set; }
    public string? LastEventSource { get; set; }
    public DateTimeOffset? LastPausedAt { get; set; }
    public DateTimeOffset? LastResumedAt { get; set; }
    public Dictionary<string, long> EventsBySource { get; set; } = new();
    public Dictionary<string, long> DropsByReason { get; set; } = new();
    public Dictionary<string, bool> SourceStates { get; set; } = new();
}
