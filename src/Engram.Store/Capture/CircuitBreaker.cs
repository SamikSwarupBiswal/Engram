namespace Engram.Store.Capture;

/// <summary>
/// Circuit breaker for capture flood protection.
/// If rate is exceeded for a sustained period, pauses capture entirely.
/// Three states: Closed (normal), Open (paused), HalfOpen (testing).
/// </summary>
public class CircuitBreaker
{
    private readonly TimeSpan _openDuration;
    private readonly int _failureThreshold;
    private int _failureCount;
    private DateTime? _openUntil;
    private CircuitState _state = CircuitState.Closed;

    public CircuitState State
    {
        get
        {
            if (_state == CircuitState.Open && _openUntil.HasValue && DateTime.UtcNow >= _openUntil.Value)
            {
                _state = CircuitState.HalfOpen;
                _failureCount = 0;
            }
            return _state;
        }
    }

    /// <summary>
    /// Create a circuit breaker.
    /// </summary>
    /// <param name="failureThreshold">Number of failures before opening</param>
    /// <param name="openDuration">How long to stay open before half-open</param>
    public CircuitBreaker(int failureThreshold, TimeSpan openDuration)
    {
        _failureThreshold = failureThreshold;
        _openDuration = openDuration;
    }

    /// <summary>
    /// Record a success. Resets failure count, closes circuit.
    /// </summary>
    public void RecordSuccess()
    {
        _failureCount = 0;
        _state = CircuitState.Closed;
    }

    /// <summary>
    /// Record a failure. Opens circuit if threshold exceeded.
    /// </summary>
    public void RecordFailure()
    {
        _failureCount++;

        if (_failureCount >= _failureThreshold)
        {
            _state = CircuitState.Open;
            _openUntil = DateTime.UtcNow + _openDuration;
        }
    }

    /// <summary>
    /// Check if the circuit allows an operation.
    /// </summary>
    public bool IsAllowed => State != CircuitState.Open;
}

public enum CircuitState
{
    Closed,    // Normal operation
    Open,      // Paused, rejecting all
    HalfOpen   // Testing if recovery is possible
}
