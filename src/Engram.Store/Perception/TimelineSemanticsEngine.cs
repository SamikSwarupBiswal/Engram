using Microsoft.Extensions.Logging;

namespace Engram.Store.Perception;

/// <summary>
/// Transforms event history into life continuity.
/// 
/// Currently timeline is event history. It needs to become life continuity:
/// - Sessions (focused periods of work)
/// - Arcs (multi-day efforts toward a goal)
/// - Momentum (sustained activity building velocity)
/// - Regressions (losing ground on a goal)
/// - Transitions (shifts between life phases)
/// - Recoveries (returning to abandoned work)
/// - Abandoned loops (started but never finished)
/// 
/// This is much harder than event history.
/// Emerges from the other Phase 7 components operating correctly.
/// </summary>
public class TimelineSemanticsEngine
{
    private readonly ILogger<TimelineSemanticsEngine>? _logger;
    private readonly List<SessionArc> _arcs = new();
    private readonly List<MomentumSignal> _momentumSignals = new();
    private readonly List<RegressionSignal> _regressionSignals = new();
    private readonly object _lock = new();

    public TimelineSemanticsEngine(ILogger<TimelineSemanticsEngine>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Analyze a sequence of perception snapshots into sessions and arcs.
    /// </summary>
    public TimelineAnalysis Analyze(IReadOnlyList<PerceptionSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
        {
            return new TimelineAnalysis();
        }

        var ordered = snapshots.OrderBy(s => s.SequenceNumber).ToList();
        var sessions = ExtractSessions(ordered);
        var arcs = ExtractArcs(sessions);
        var momentum = ComputeMomentum(sessions);
        var regressions = DetectRegressions(sessions);

        lock (_lock)
        {
            _arcs.Clear();
            _arcs.AddRange(arcs);
            _momentumSignals.Clear();
            _momentumSignals.AddRange(momentum);
            _regressionSignals.Clear();
            _regressionSignals.AddRange(regressions);
        }

        _logger?.LogInformation(
            "Timeline analysis: {Sessions} sessions, {Arcs} arcs, {Momentum} momentum signals, {Regressions} regressions",
            sessions.Count, arcs.Count, momentum.Count, regressions.Count);

        return new TimelineAnalysis
        {
            Sessions = sessions,
            Arcs = arcs,
            MomentumSignals = momentum,
            RegressionSignals = regressions,
            AnalyzedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Extract sessions from snapshots — contiguous periods of similar activity.
    /// A session ends when the behavioral mode changes significantly or there's a gap.
    /// </summary>
    private List<TimelineSession> ExtractSessions(List<PerceptionSnapshot> snapshots)
    {
        var sessions = new List<TimelineSession>();
        if (snapshots.Count == 0) return sessions;

        var currentSession = new TimelineSession
        {
            SessionId = Guid.NewGuid().ToString("n")[..8],
            Mode = snapshots[0].Interpretation.BehavioralMode,
            StartedAt = snapshots[0].Timestamp,
            SnapshotIds = new List<string> { snapshots[0].SnapshotId }
        };

        for (int i = 1; i < snapshots.Count; i++)
        {
            var snapshot = snapshots[i];
            var timeSincePrevious = snapshot.Timestamp - snapshots[i - 1].Timestamp;
            var modeChanged = snapshot.Interpretation.BehavioralMode != currentSession.Mode;
            var significantGap = timeSincePrevious > TimeSpan.FromMinutes(30);

            if (modeChanged || significantGap)
            {
                // End current session
                currentSession.EndedAt = snapshots[i - 1].Timestamp;
                currentSession.Duration = currentSession.EndedAt - currentSession.StartedAt;
                sessions.Add(currentSession);

                // Start new session
                currentSession = new TimelineSession
                {
                    SessionId = Guid.NewGuid().ToString("n")[..8],
                    Mode = snapshot.Interpretation.BehavioralMode,
                    StartedAt = snapshot.Timestamp,
                    SnapshotIds = new List<string> { snapshot.SnapshotId }
                };
            }
            else
            {
                currentSession.SnapshotIds.Add(snapshot.SnapshotId);
            }
        }

        // Close final session
        currentSession.EndedAt = snapshots[^1].Timestamp;
        currentSession.Duration = currentSession.EndedAt - currentSession.StartedAt;
        sessions.Add(currentSession);

        return sessions;
    }

    /// <summary>
    /// Extract arcs — multi-session efforts toward a sustained mode.
    /// An arc is a sequence of sessions with the same dominant mode
    /// across multiple time periods.
    /// </summary>
    private List<SessionArc> ExtractArcs(List<TimelineSession> sessions)
    {
        var arcs = new List<SessionArc>();
        if (sessions.Count == 0) return arcs;

        // Group sessions by mode
        var modeGroups = sessions
            .GroupBy(s => s.Mode)
            .ToList();

        foreach (var group in modeGroups)
        {
            var modeSessions = group.OrderBy(s => s.StartedAt).ToList();
            if (modeSessions.Count < 2) continue;

            // Find arc boundaries — gaps > 2 hours between sessions of same mode
            var currentArc = new SessionArc
            {
                ArcId = Guid.NewGuid().ToString("n")[..8],
                DominantMode = group.Key,
                StartedAt = modeSessions[0].StartedAt,
                SessionIds = new List<string> { modeSessions[0].SessionId }
            };

            for (int i = 1; i < modeSessions.Count; i++)
            {
                var gap = modeSessions[i].StartedAt - modeSessions[i - 1].EndedAt!.Value;

                if (gap > TimeSpan.FromHours(2))
                {
                    // End current arc
                    currentArc.EndedAt = modeSessions[i - 1].EndedAt;
                    currentArc.TotalDuration = TimeSpan.FromSeconds(
                        currentArc.SessionIds.Select(id => sessions.First(s => s.SessionId == id))
                            .Sum(s => s.Duration?.TotalSeconds ?? 0));
                    arcs.Add(currentArc);

                    // Start new arc
                    currentArc = new SessionArc
                    {
                        ArcId = Guid.NewGuid().ToString("n")[..8],
                        DominantMode = group.Key,
                        StartedAt = modeSessions[i].StartedAt,
                        SessionIds = new List<string> { modeSessions[i].SessionId }
                    };
                }
                else
                {
                    currentArc.SessionIds.Add(modeSessions[i].SessionId);
                }
            }

            // Close final arc
            currentArc.EndedAt = modeSessions[^1].EndedAt;
            currentArc.TotalDuration = TimeSpan.FromSeconds(
                currentArc.SessionIds.Select(id => sessions.First(s => s.SessionId == id))
                    .Sum(s => s.Duration?.TotalSeconds ?? 0));
            arcs.Add(currentArc);
        }

        return arcs;
    }

    /// <summary>
    /// Compute momentum — sustained activity in a mode building velocity.
    /// Positive momentum = increasing session duration over time.
    /// Negative momentum = decreasing session duration (potential regression).
    /// </summary>
    private List<MomentumSignal> ComputeMomentum(List<TimelineSession> sessions)
    {
        var signals = new List<MomentumSignal>();
        var modeGroups = sessions.GroupBy(s => s.Mode);

        foreach (var group in modeGroups)
        {
            var modeSessions = group
                .Where(s => s.Duration.HasValue)
                .OrderBy(s => s.StartedAt)
                .ToList();

            if (modeSessions.Count < 3) continue;

            // Compute trend: is session duration increasing or decreasing?
            var durations = modeSessions.Select(s => s.Duration!.Value.TotalMinutes).ToList();
            var firstHalf = durations.Take(durations.Count / 2).Average();
            var secondHalf = durations.Skip(durations.Count / 2).Average();

            var trend = secondHalf - firstHalf;
            var momentum = trend / Math.Max(firstHalf, 1); // Normalized

            signals.Add(new MomentumSignal
            {
                Mode = group.Key,
                Momentum = momentum, // Positive = building, negative = fading
                SessionCount = modeSessions.Count,
                AverageDuration = TimeSpan.FromMinutes(durations.Average()),
                Trend = trend > 0 ? MomentumTrend.Building
                    : trend < -5 ? MomentumTrend.Fading
                    : MomentumTrend.Stable,
                DetectedAt = DateTimeOffset.UtcNow
            });
        }

        return signals;
    }

    /// <summary>
    /// Detect regressions — losing ground on previously active modes.
    /// A regression is when a mode that was previously dominant becomes rare.
    /// </summary>
    private List<RegressionSignal> DetectRegressions(List<TimelineSession> sessions)
    {
        var signals = new List<RegressionSignal>();

        if (sessions.Count < 10) return signals; // Need enough data

        var midpoint = sessions.Count / 2;
        var firstHalf = sessions.Take(midpoint).ToList();
        var secondHalf = sessions.Skip(midpoint).ToList();

        var firstModes = firstHalf.GroupBy(s => s.Mode)
            .ToDictionary(g => g.Key, g => (double)g.Count() / firstHalf.Count);
        var secondModes = secondHalf.GroupBy(s => s.Mode)
            .ToDictionary(g => g.Key, g => (double)g.Count() / secondHalf.Count);

        foreach (var (mode, firstRatio) in firstModes)
        {
            if (!secondModes.TryGetValue(mode, out var secondRatio))
                secondRatio = 0;

            var drop = firstRatio - secondRatio;
            if (drop > 0.2) // Mode lost >20% of its share
            {
                signals.Add(new RegressionSignal
                {
                    Mode = mode,
                    PreviousShare = firstRatio,
                    CurrentShare = secondRatio,
                    DropMagnitude = drop,
                    DetectedAt = DateTimeOffset.UtcNow
                });
            }
        }

        return signals;
    }

    /// <summary>
    /// Get the current timeline state.
    /// </summary>
    public TimelineState GetCurrentState()
    {
        lock (_lock)
        {
            return new TimelineState
            {
                ActiveArcs = _arcs.Count(a => a.EndedAt == null || a.EndedAt > DateTimeOffset.UtcNow.AddHours(-24)),
                TotalArcs = _arcs.Count,
                BuildingMomentum = _momentumSignals.Count(m => m.Trend == MomentumTrend.Building),
                FadingMomentum = _momentumSignals.Count(m => m.Trend == MomentumTrend.Fading),
                ActiveRegressions = _regressionSignals.Count
            };
        }
    }
}

/// <summary>
/// A focused period of activity with a single behavioral mode.
/// </summary>
public class TimelineSession
{
    public string SessionId { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public TimeSpan? Duration { get; set; }
    public List<string> SnapshotIds { get; set; } = new();
}

/// <summary>
/// A multi-session effort toward a sustained mode.
/// </summary>
public class SessionArc
{
    public string ArcId { get; set; } = string.Empty;
    public string DominantMode { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public TimeSpan? TotalDuration { get; set; }
    public List<string> SessionIds { get; set; } = new();
}

/// <summary>
/// A momentum signal — is a mode building or fading?
/// </summary>
public record MomentumSignal
{
    public string Mode { get; init; } = string.Empty;
    public double Momentum { get; init; }
    public int SessionCount { get; init; }
    public TimeSpan AverageDuration { get; init; }
    public MomentumTrend Trend { get; init; }
    public DateTimeOffset DetectedAt { get; init; }
}

public enum MomentumTrend
{
    Building,  // Session durations increasing
    Stable,    // Session durations stable
    Fading     // Session durations decreasing
}

/// <summary>
/// A regression signal — a previously active mode becoming rare.
/// </summary>
public record RegressionSignal
{
    public string Mode { get; init; } = string.Empty;
    public double PreviousShare { get; init; }
    public double CurrentShare { get; init; }
    public double DropMagnitude { get; init; }
    public DateTimeOffset DetectedAt { get; init; }
}

/// <summary>
/// Full timeline analysis result.
/// </summary>
public record TimelineAnalysis
{
    public List<TimelineSession> Sessions { get; init; } = new();
    public List<SessionArc> Arcs { get; init; } = new();
    public List<MomentumSignal> MomentumSignals { get; init; } = new();
    public List<RegressionSignal> RegressionSignals { get; init; } = new();
    public DateTimeOffset AnalyzedAt { get; init; }
}

/// <summary>
/// Current timeline state summary.
/// </summary>
public record TimelineState
{
    public int ActiveArcs { get; init; }
    public int TotalArcs { get; init; }
    public int BuildingMomentum { get; init; }
    public int FadingMomentum { get; init; }
    public int ActiveRegressions { get; init; }
}
