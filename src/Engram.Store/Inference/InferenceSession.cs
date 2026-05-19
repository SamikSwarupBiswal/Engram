using System.Diagnostics;

namespace Engram.Store.Inference;

/// <summary>
/// Tracks a single inference request with heartbeat monitoring, timeout enforcement,
/// and graceful cancellation escalation.
/// 
/// Architecture:
///   Request starts → InferenceSession created → heartbeat on each token
///   Watchdog monitors: no-token timeout, total runtime, cancellation state
///   Violation → graceful cancel → escalate to hard cancel → context reset
/// 
/// The heartbeat is the critical signal: process alive != generation alive.
/// </summary>
public sealed class InferenceSession : IDisposable
{
    private readonly InferenceLogger _log = InferenceLogger.Instance;
    private readonly Stopwatch _totalTimer = new();
    private readonly Stopwatch _tokenTimer = new();
    private readonly CancellationTokenSource _cts;
    private readonly object _lock = new();

    // ── Heartbeat state ──
    private long _tokensEmitted;
    private DateTime _lastTokenAt;
    private DateTime _startedAt;
    private bool _completed;
    private bool _cancelled;
    private InferenceViolation? _violation;

    // ── Configuration ──
    public string SessionId { get; }
    public TimeSpan NoTokenTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan HardTimeout { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan HeartbeatCheckInterval { get; set; } = TimeSpan.FromSeconds(5);

    // ── Public state ──
    public long TokensEmitted => Interlocked.Read(ref _tokensEmitted);
    public TimeSpan Elapsed => _totalTimer.Elapsed;
    public TimeSpan TimeSinceLastToken
    {
        get { lock (_lock) return DateTime.UtcNow - _lastTokenAt; }
    }
    public bool IsCompleted => _completed;
    public bool IsCancelled => _cancelled;
    public InferenceViolation? Violation => _violation;
    public CancellationToken Token => _cts.Token;

    // ── Events ──
    public event Action<InferenceSession, InferenceViolation>? OnViolation;
    public event Action<InferenceSession>? OnComplete;

    public InferenceSession(string? sessionId = null)
    {
        SessionId = sessionId ?? Guid.NewGuid().ToString("n")[..8];
        _cts = new CancellationTokenSource();
        _startedAt = DateTime.UtcNow;
        _lastTokenAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Start the session and watchdog. Call before inference begins.
    /// </summary>
    public void Start()
    {
        _totalTimer.Start();
        _tokenTimer.Start();
        _log.Inference($"Session {SessionId} started (noToken={NoTokenTimeout.TotalSeconds}s, hard={HardTimeout.TotalSeconds}s)");

        // Start watchdog on background thread
        _ = Task.Run(WatchdogLoop);
    }

    /// <summary>
    /// Called by the inference engine each time a token is emitted.
    /// This is the heartbeat — the watchdog monitors its freshness.
    /// </summary>
    public void RecordToken()
    {
        lock (_lock)
        {
            Interlocked.Increment(ref _tokensEmitted);
            _lastTokenAt = DateTime.UtcNow;
            _tokenTimer.Restart();
        }
    }

    /// <summary>
    /// Mark session as successfully completed.
    /// </summary>
    public void Complete()
    {
        lock (_lock)
        {
            if (_completed) return;
            _completed = true;
            _totalTimer.Stop();
        }

        _log.Inference($"Session {SessionId} complete: {TokensEmitted} tokens in {Elapsed.TotalSeconds:F1}s " +
            $"({TokensEmitted / Math.Max(0.1, Elapsed.TotalSeconds):F1} tok/s)");
        OnComplete?.Invoke(this);
    }

    /// <summary>
    /// Request graceful cancellation. Watchdog will escalate if needed.
    /// </summary>
    public void Cancel(string reason = "user_cancelled")
    {
        lock (_lock)
        {
            if (_cancelled) return;
            _cancelled = true;
        }

        _log.Inference($"Session {SessionId} cancellation requested: {reason}");
        _cts.Cancel();
    }

    /// <summary>
    /// Watchdog loop. Runs on background thread.
    /// Monitors heartbeat freshness and total runtime.
    /// </summary>
    private async Task WatchdogLoop()
    {
        try
        {
            while (!_completed && !_cancelled)
            {
                await Task.Delay(HeartbeatCheckInterval, CancellationToken.None);

                lock (_lock)
                {
                    if (_completed || _cancelled) break;

                    // Check 1: No token timeout (only if generation has started)
                    var timeSinceToken = DateTime.UtcNow - _lastTokenAt;
                    if (TokensEmitted > 0 && timeSinceToken > NoTokenTimeout)
                    {
                        _violation = new InferenceViolation
                        {
                            Type = ViolationType.NoTokenTimeout,
                            Message = $"No token emitted for {timeSinceToken.TotalSeconds:F0}s " +
                                      $"(limit: {NoTokenTimeout.TotalSeconds}s). " +
                                      $"Last token at {_lastTokenAt:HH:mm:ss}. " +
                                      $"Total tokens so far: {TokensEmitted}.",
                            SessionId = SessionId,
                            TokensEmitted = TokensEmitted,
                            Elapsed = Elapsed
                        };

                        _log.InferenceError($"WATCHDOG: Session {SessionId} — {_violation.Message}");
                        OnViolation?.Invoke(this, _violation);
                        _cts.Cancel();
                        _cancelled = true;
                        break;
                    }

                    // Check 2: Hard timeout
                    if (_totalTimer.Elapsed > HardTimeout)
                    {
                        _violation = new InferenceViolation
                        {
                            Type = ViolationType.HardTimeout,
                            Message = $"Inference exceeded hard timeout: {_totalTimer.Elapsed.TotalSeconds:F0}s " +
                                      $"(limit: {HardTimeout.TotalSeconds}s). " +
                                      $"Tokens: {TokensEmitted}.",
                            SessionId = SessionId,
                            TokensEmitted = TokensEmitted,
                            Elapsed = Elapsed
                        };

                        _log.InferenceError($"WATCHDOG: Session {SessionId} — {_violation.Message}");
                        OnViolation?.Invoke(this, _violation);
                        _cts.Cancel();
                        _cancelled = true;
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _log.InferenceError($"Watchdog error for session {SessionId}", ex);
        }
    }

    /// <summary>
    /// Get current session telemetry for health reporting.
    /// </summary>
    public InferenceSessionTelemetry GetTelemetry()
    {
        lock (_lock)
        {
            return new InferenceSessionTelemetry
            {
                SessionId = SessionId,
                IsActive = !_completed && !_cancelled,
                TokensEmitted = TokensEmitted,
                ElapsedMs = (int)Elapsed.TotalMilliseconds,
                TimeSinceLastTokenMs = (int)(DateTime.UtcNow - _lastTokenAt).TotalMilliseconds,
                TokensPerSecond = Elapsed.TotalSeconds > 0.1
                    ? TokensEmitted / Elapsed.TotalSeconds
                    : 0,
                Violation = _violation
            };
        }
    }

    public void Dispose()
    {
        _cts.Dispose();
        _totalTimer.Stop();
        _tokenTimer.Stop();
    }
}

/// <summary>
/// A violation detected by the inference watchdog.
/// </summary>
public class InferenceViolation
{
    public ViolationType Type { get; init; }
    public string Message { get; init; } = "";
    public string SessionId { get; init; } = "";
    public long TokensEmitted { get; init; }
    public TimeSpan Elapsed { get; init; }
}

public enum ViolationType
{
    /// <summary>Tokens stopped flowing but inference didn't complete.</summary>
    NoTokenTimeout,

    /// <summary>Absolute time limit exceeded.</summary>
    HardTimeout,

    /// <summary>Memory pressure exceeded threshold.</summary>
    MemoryPressure,

    /// <summary>Cancellation requested and enforced.</summary>
    Cancelled
}

/// <summary>
/// Telemetry snapshot for a single inference session.
/// </summary>
public class InferenceSessionTelemetry
{
    public string SessionId { get; init; } = "";
    public bool IsActive { get; init; }
    public long TokensEmitted { get; init; }
    public int ElapsedMs { get; init; }
    public int TimeSinceLastTokenMs { get; init; }
    public double TokensPerSecond { get; init; }
    public InferenceViolation? Violation { get; init; }
}
