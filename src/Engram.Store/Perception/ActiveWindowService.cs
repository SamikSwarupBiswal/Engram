using Engram.Store.Events;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Perception;

/// <summary>
/// Semantic active window service.
/// 
/// Wraps window tracking with semantic event generation.
/// NOT raw filesystem spam. Semantic density.
/// 
/// Emits:
/// - ActiveWindowChanged: when user switches windows
/// - FocusSessionStarted: when user starts focused work
/// - FocusSessionEnded: when user leaves focused work
/// - ContextSwitchDetected: when user switches rapidly
/// - IdleTransitionDetected: when user goes idle
/// 
/// This alone enables:
/// - Focus analysis
/// - Drift detection
/// - Workflow modeling
/// - Project tracking
/// - Productivity synthesis
/// - Behavioral trajectories
/// WITHOUT invasive surveillance.
/// </summary>
public class ActiveWindowService : IDisposable
{
    private readonly IEventBus _eventBus;
    private readonly ILogger<ActiveWindowService>? _logger;

    private string _currentProcess = string.Empty;
    private string _currentTitle = string.Empty;
    private DateTimeOffset _focusStartedAt;
    private readonly List<WindowSession> _recentSessions = new();
    private readonly int _maxRecentSessions = 100;
    private bool _disposed;

    /// <summary>Minimum focus duration (seconds) to emit a focus session event.</summary>
    public int MinFocusDurationSeconds { get; set; } = 30;

    /// <summary>Number of switches per minute to trigger context switch detection.</summary>
    public int ContextSwitchThreshold { get; set; } = 5;

    public ActiveWindowService(
        IEventBus eventBus,
        ILogger<ActiveWindowService>? logger = null)
    {
        _eventBus = eventBus;
        _logger = logger;
    }

    /// <summary>
    /// Process an active window change. Called by ActiveWindowTracker or polling.
    /// </summary>
    public void ProcessWindowChange(string processName, string windowTitle)
    {
        if (string.IsNullOrEmpty(processName)) return;

        var now = DateTimeOffset.UtcNow;

        // End previous focus session if significant
        if (!string.IsNullOrEmpty(_currentProcess))
        {
            var focusDuration = now - _focusStartedAt;
            if (focusDuration.TotalSeconds >= MinFocusDurationSeconds)
            {
                EndFocusSession(focusDuration);
            }
        }

        // Start new focus session
        _currentProcess = processName;
        _currentTitle = windowTitle;
        _focusStartedAt = now;

        // Emit window change event
        _eventBus.Publish(new EventEnvelope
        {
            EventType = "perception.active_window_changed",
            Source = "active_window_service",
            Payload = new
            {
                Process = processName,
                Title = windowTitle,
                Timestamp = now
            }
        });

        // Check for context switching
        DetectContextSwitching(now);

        _logger?.LogDebug("Active window: {Process} - {Title}", processName, windowTitle);
    }

    /// <summary>
    /// Process idle transition (user went idle).
    /// </summary>
    public void ProcessIdleTransition(TimeSpan idleDuration)
    {
        var now = DateTimeOffset.UtcNow;

        // End current focus session
        if (!string.IsNullOrEmpty(_currentProcess))
        {
            var focusDuration = now - _focusStartedAt;
            if (focusDuration.TotalSeconds >= MinFocusDurationSeconds)
            {
                EndFocusSession(focusDuration);
            }
        }

        _eventBus.Publish(new EventEnvelope
        {
            EventType = "perception.idle_transition",
            Source = "active_window_service",
            Payload = new
            {
                IdleDurationSeconds = idleDuration.TotalSeconds,
                Timestamp = now
            }
        });

        _logger?.LogInformation("User idle for {Seconds:F0}s", idleDuration.TotalSeconds);
    }

    /// <summary>
    /// Get recent focus sessions for analysis.
    /// </summary>
    public List<WindowSession> GetRecentSessions()
    {
        lock (_recentSessions)
        {
            return _recentSessions.ToList();
        }
    }

    /// <summary>
    /// Get focus statistics for the current period.
    /// </summary>
    public FocusStatistics GetFocusStatistics(TimeSpan period)
    {
        lock (_recentSessions)
        {
            var cutoff = DateTimeOffset.UtcNow - period;
            var sessions = _recentSessions
                .Where(s => s.StartedAt >= cutoff)
                .ToList();

            if (sessions.Count == 0)
            {
                return new FocusStatistics
                {
                    Period = period,
                    TotalFocusTime = TimeSpan.Zero,
                    SessionCount = 0,
                    AverageSessionDuration = TimeSpan.Zero,
                    ContextSwitchCount = 0,
                    TopProcesses = new Dictionary<string, TimeSpan>()
                };
            }

            var totalFocus = TimeSpan.FromSeconds(sessions.Sum(s => s.Duration.TotalSeconds));
            var avgDuration = TimeSpan.FromSeconds(totalFocus.TotalSeconds / sessions.Count);

            var topProcesses = sessions
                .GroupBy(s => s.ProcessName)
                .ToDictionary(
                    g => g.Key,
                    g => TimeSpan.FromSeconds(g.Sum(s => s.Duration.TotalSeconds)))
                .OrderByDescending(kv => kv.Value)
                .Take(5)
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            return new FocusStatistics
            {
                Period = period,
                TotalFocusTime = totalFocus,
                SessionCount = sessions.Count,
                AverageSessionDuration = avgDuration,
                ContextSwitchCount = CountContextSwitches(sessions),
                TopProcesses = topProcesses
            };
        }
    }

    private void EndFocusSession(TimeSpan duration)
    {
        var session = new WindowSession
        {
            ProcessName = _currentProcess,
            WindowTitle = _currentTitle,
            StartedAt = _focusStartedAt,
            EndedAt = DateTimeOffset.UtcNow,
            Duration = duration
        };

        lock (_recentSessions)
        {
            _recentSessions.Add(session);
            while (_recentSessions.Count > _maxRecentSessions)
                _recentSessions.RemoveAt(0);
        }

        _eventBus.Publish(new EventEnvelope
        {
            EventType = "perception.focus_session_ended",
            Source = "active_window_service",
            Payload = new
            {
                Process = _currentProcess,
                Title = _currentTitle,
                DurationSeconds = duration.TotalSeconds,
                StartedAt = _focusStartedAt,
                EndedAt = DateTimeOffset.UtcNow
            }
        });

        _logger?.LogInformation("Focus session ended: {Process} ({Duration:F0}s)",
            _currentProcess, duration.TotalSeconds);
    }

    private void DetectContextSwitching(DateTimeOffset now)
    {
        lock (_recentSessions)
        {
            var recentSwitches = _recentSessions
                .Where(s => s.EndedAt >= now.AddMinutes(-1))
                .Count();

            if (recentSwitches >= ContextSwitchThreshold)
            {
                _eventBus.Publish(new EventEnvelope
                {
                    EventType = "perception.context_switch_detected",
                    Source = "active_window_service",
                    Payload = new
                    {
                        SwitchCount = recentSwitches,
                        PeriodMinutes = 1,
                        Timestamp = now
                    }
                });

                _logger?.LogWarning("Context switching detected: {Count} switches in 1 minute", recentSwitches);
            }
        }
    }

    private static int CountContextSwitches(List<WindowSession> sessions)
    {
        if (sessions.Count < 2) return 0;

        int switches = 0;
        for (int i = 1; i < sessions.Count; i++)
        {
            if (sessions[i].ProcessName != sessions[i - 1].ProcessName)
                switches++;
        }
        return switches;
    }

    public void Dispose()
    {
        _disposed = true;
    }
}

/// <summary>
/// A focus session on a specific window.
/// </summary>
public class WindowSession
{
    public string ProcessName { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset EndedAt { get; set; }
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Focus statistics for a time period.
/// </summary>
public class FocusStatistics
{
    public TimeSpan Period { get; set; }
    public TimeSpan TotalFocusTime { get; set; }
    public int SessionCount { get; set; }
    public TimeSpan AverageSessionDuration { get; set; }
    public int ContextSwitchCount { get; set; }
    public Dictionary<string, TimeSpan> TopProcesses { get; set; } = new();
}
