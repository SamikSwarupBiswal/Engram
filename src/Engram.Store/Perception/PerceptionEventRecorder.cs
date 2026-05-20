using Engram.Store.Events;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Perception;

/// <summary>
/// Records perception events as replayable snapshots.
/// 
/// Taps into the EventBus and captures every perception event
/// with its raw input AND the interpretation that resulted.
/// 
/// This is the foundation of Phase 7 — without recording,
/// nothing is replayable, and nothing is testable.
/// 
/// Pipeline:
///   EventBus (perception.*) → PerceptionEventRecorder → List&lt;PerceptionSnapshot&gt;
/// 
/// The recorder is passive — it does not modify events or interpretations.
/// It only captures them for later replay and comparison.
/// </summary>
public class PerceptionEventRecorder : IDisposable
{
    private readonly IEventBus _eventBus;
    private readonly ILogger<PerceptionEventRecorder>? _logger;
    private readonly List<PerceptionSnapshot> _snapshots = new();
    private readonly object _lock = new();
    private readonly List<IDisposable> _subscriptions = new();
    private long _sequenceCounter;
    private bool _disposed;
    private bool _recording;

    /// <summary>Maximum snapshots to retain in memory.</summary>
    public int MaxSnapshots { get; set; } = 10_000;

    public PerceptionEventRecorder(
        IEventBus eventBus,
        ILogger<PerceptionEventRecorder>? logger = null)
    {
        _eventBus = eventBus;
        _logger = logger;
    }

    /// <summary>Whether the recorder is currently capturing events.</summary>
    public bool IsRecording => _recording;

    /// <summary>Number of snapshots captured.</summary>
    public int SnapshotCount
    {
        get { lock (_lock) return _snapshots.Count; }
    }

    /// <summary>
    /// Start recording perception events from the EventBus.
    /// </summary>
    public void StartRecording()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_recording) return;

        _subscriptions.Add(_eventBus.Subscribe("perception.active_window_changed", OnWindowChanged));
        _subscriptions.Add(_eventBus.Subscribe("perception.focus_session_ended", OnFocusSessionEnded));
        _subscriptions.Add(_eventBus.Subscribe("perception.context_switch_detected", OnContextSwitchDetected));
        _subscriptions.Add(_eventBus.Subscribe("perception.idle_transition", OnIdleTransition));
        _subscriptions.Add(_eventBus.Subscribe("perception.file_changed", OnFileChanged));
        _subscriptions.Add(_eventBus.Subscribe("perception.behavioral_mode_changed", OnBehavioralModeChanged));

        _recording = true;
        _logger?.LogInformation("PerceptionEventRecorder started. Subscribed to perception events.");
    }

    /// <summary>
    /// Stop recording. Events are still published but not captured.
    /// </summary>
    public void StopRecording()
    {
        foreach (var sub in _subscriptions)
        {
            try { sub.Dispose(); } catch { }
        }
        _subscriptions.Clear();
        _recording = false;
        _logger?.LogInformation("PerceptionEventRecorder stopped. {Count} snapshots captured.", _snapshots.Count);
    }

    /// <summary>
    /// Get all recorded snapshots in chronological order.
    /// </summary>
    public List<PerceptionSnapshot> GetSnapshots()
    {
        lock (_lock)
        {
            return _snapshots.OrderBy(s => s.SequenceNumber).ToList();
        }
    }

    /// <summary>
    /// Get snapshots for a specific time range.
    /// </summary>
    public List<PerceptionSnapshot> GetSnapshots(DateTimeOffset from, DateTimeOffset to)
    {
        lock (_lock)
        {
            return _snapshots
                .Where(s => s.Timestamp >= from && s.Timestamp <= to)
                .OrderBy(s => s.SequenceNumber)
                .ToList();
        }
    }

    /// <summary>
    /// Get snapshots filtered by event type.
    /// </summary>
    public List<PerceptionSnapshot> GetSnapshotsByType(string eventType)
    {
        lock (_lock)
        {
            return _snapshots
                .Where(s => s.Input.EventType == eventType)
                .OrderBy(s => s.SequenceNumber)
                .ToList();
        }
    }

    /// <summary>
    /// Clear all recorded snapshots.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _snapshots.Clear();
            _sequenceCounter = 0;
        }
    }

    /// <summary>
    /// Inject a snapshot directly (for testing and replay setup).
    /// </summary>
    public void InjectSnapshot(PerceptionSnapshot snapshot)
    {
        lock (_lock)
        {
            var withSequence = snapshot with { SequenceNumber = _sequenceCounter++ };
            _snapshots.Add(withSequence);
            TrimToCapacity();
        }
    }

    private void OnWindowChanged(EventEnvelope envelope)
    {
        if (envelope.Payload is not object payload) return;

        var processName = GetProperty<string>(payload, "Process") ?? string.Empty;
        var title = GetProperty<string>(payload, "Title") ?? string.Empty;

        var snapshot = new PerceptionSnapshot
        {
            Timestamp = envelope.Timestamp,
            SequenceNumber = NextSequence(),
            StrategyName = "event_bus_recording",
            Input = new PerceptionInput
            {
                ProcessName = processName,
                WindowTitle = title,
                EventType = "window_change"
            },
            Interpretation = new PerceptionInterpretation
            {
                BehavioralMode = "pending", // Will be filled by mode_changed event
                ProjectDetected = ExtractProjectFromTitle(title)
            }
        };

        AddSnapshot(snapshot);
    }

    private void OnFocusSessionEnded(EventEnvelope envelope)
    {
        if (envelope.Payload is not object payload) return;

        var processName = GetProperty<string>(payload, "Process") ?? string.Empty;
        var title = GetProperty<string>(payload, "Title") ?? string.Empty;
        var durationSeconds = GetProperty<double>(payload, "DurationSeconds");

        var snapshot = new PerceptionSnapshot
        {
            Timestamp = envelope.Timestamp,
            SequenceNumber = NextSequence(),
            StrategyName = "event_bus_recording",
            Input = new PerceptionInput
            {
                ProcessName = processName,
                WindowTitle = title,
                FocusDuration = TimeSpan.FromSeconds(durationSeconds),
                EventType = "focus_session_ended"
            },
            Interpretation = new PerceptionInterpretation
            {
                BehavioralMode = "focus_session",
                ProjectDetected = ExtractProjectFromTitle(title)
            }
        };

        AddSnapshot(snapshot);
    }

    private void OnContextSwitchDetected(EventEnvelope envelope)
    {
        if (envelope.Payload is not object payload) return;

        var switchCount = GetProperty<int>(payload, "SwitchCount");

        var snapshot = new PerceptionSnapshot
        {
            Timestamp = envelope.Timestamp,
            SequenceNumber = NextSequence(),
            StrategyName = "event_bus_recording",
            Input = new PerceptionInput
            {
                EventType = "context_switch_detected",
                AdditionalContext = new Dictionary<string, string>
                {
                    ["switch_count"] = switchCount.ToString()
                }
            },
            Interpretation = new PerceptionInterpretation
            {
                BehavioralMode = "context_switching"
            }
        };

        AddSnapshot(snapshot);
    }

    private void OnIdleTransition(EventEnvelope envelope)
    {
        if (envelope.Payload is not object payload) return;

        var idleSeconds = GetProperty<double>(payload, "IdleDurationSeconds");

        var snapshot = new PerceptionSnapshot
        {
            Timestamp = envelope.Timestamp,
            SequenceNumber = NextSequence(),
            StrategyName = "event_bus_recording",
            Input = new PerceptionInput
            {
                EventType = "idle_transition",
                FocusDuration = TimeSpan.FromSeconds(idleSeconds)
            },
            Interpretation = new PerceptionInterpretation
            {
                BehavioralMode = "idle"
            }
        };

        AddSnapshot(snapshot);
    }

    private void OnFileChanged(EventEnvelope envelope)
    {
        var snapshot = new PerceptionSnapshot
        {
            Timestamp = envelope.Timestamp,
            SequenceNumber = NextSequence(),
            StrategyName = "event_bus_recording",
            Input = new PerceptionInput
            {
                EventType = "file_change",
                AdditionalContext = new Dictionary<string, string>
                {
                    ["payload_type"] = envelope.Payload?.GetType().Name ?? "null"
                }
            },
            Interpretation = new PerceptionInterpretation
            {
                BehavioralMode = "file_activity"
            }
        };

        AddSnapshot(snapshot);
    }

    private void OnBehavioralModeChanged(EventEnvelope envelope)
    {
        if (envelope.Payload is not object payload) return;

        var newMode = GetProperty<string>(payload, "NewMode") ?? string.Empty;
        var previousMode = GetProperty<string>(payload, "PreviousMode") ?? string.Empty;

        // Update the most recent snapshot's interpretation with the actual mode
        lock (_lock)
        {
            var lastSnapshot = _snapshots.LastOrDefault();
            if (lastSnapshot != null && lastSnapshot.Interpretation.BehavioralMode == "pending")
            {
                var index = _snapshots.Count - 1;
                _snapshots[index] = lastSnapshot with
                {
                    Interpretation = lastSnapshot.Interpretation with
                    {
                        BehavioralMode = newMode,
                        PreviousMode = previousMode
                    }
                };
            }
            else
            {
                // Standalone mode change event
                var snapshot = new PerceptionSnapshot
                {
                    Timestamp = envelope.Timestamp,
                    SequenceNumber = NextSequence(),
                    StrategyName = "event_bus_recording",
                    Input = new PerceptionInput
                    {
                        EventType = "mode_transition"
                    },
                    Interpretation = new PerceptionInterpretation
                    {
                        BehavioralMode = newMode,
                        PreviousMode = previousMode
                    }
                };
                _snapshots.Add(snapshot);
                TrimToCapacity();
            }
        }
    }

    private void AddSnapshot(PerceptionSnapshot snapshot)
    {
        lock (_lock)
        {
            _snapshots.Add(snapshot);
            TrimToCapacity();
        }
    }

    private long NextSequence()
    {
        return Interlocked.Increment(ref _sequenceCounter);
    }

    private void TrimToCapacity()
    {
        while (_snapshots.Count > MaxSnapshots)
            _snapshots.RemoveAt(0);
    }

    private static T? GetProperty<T>(object obj, string propertyName)
    {
        try
        {
            var prop = obj.GetType().GetProperty(propertyName);
            if (prop == null) return default;
            var value = prop.GetValue(obj);
            if (value is T typed) return typed;
            return default;
        }
        catch
        {
            return default;
        }
    }

    private static string ExtractProjectFromTitle(string windowTitle)
    {
        if (string.IsNullOrEmpty(windowTitle)) return string.Empty;

        if (windowTitle.Contains("Visual Studio Code"))
        {
            var parts = windowTitle.Split(" - ");
            if (parts.Length >= 2)
                return parts[^2].Trim();
        }

        return string.Empty;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            StopRecording();
            _disposed = true;
        }
    }
}
