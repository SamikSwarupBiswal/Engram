namespace Engram.Store.Capture;

/// <summary>
/// Token bucket rate limiter. Prevents event floods from overwhelming the system.
/// Thread-safe for concurrent access.
/// </summary>
public class RateLimiter
{
    private readonly double _maxTokens;
    private readonly double _refillRatePerSecond;
    private double _tokens;
    private DateTime _lastRefill;
    private readonly object _lock = new();

    /// <summary>Count of events that were rate-limited (dropped).</summary>
    public long DroppedCount { get; private set; }

    /// <summary>Count of events that passed the rate limiter.</summary>
    public long PassedCount { get; private set; }

    /// <summary>
    /// Create a rate limiter.
    /// </summary>
    /// <param name="maxTokens">Maximum burst size (e.g., 100 events)</param>
    /// <param name="refillRatePerSecond">Sustained rate (e.g., 100 events/sec)</param>
    public RateLimiter(double maxTokens, double refillRatePerSecond)
    {
        _maxTokens = maxTokens;
        _refillRatePerSecond = refillRatePerSecond;
        _tokens = maxTokens;
        _lastRefill = DateTime.UtcNow;
    }

    /// <summary>
    /// Try to consume one token. Returns true if allowed, false if rate-limited.
    /// </summary>
    public bool TryAcquire()
    {
        lock (_lock)
        {
            Refill();

            if (_tokens >= 1.0)
            {
                _tokens -= 1.0;
                PassedCount++;
                return true;
            }

            DroppedCount++;
            return false;
        }
    }

    /// <summary>
    /// Reset the rate limiter state.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _tokens = _maxTokens;
            _lastRefill = DateTime.UtcNow;
            DroppedCount = 0;
            PassedCount = 0;
        }
    }

    private void Refill()
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastRefill).TotalSeconds;
        _tokens = Math.Min(_maxTokens, _tokens + elapsed * _refillRatePerSecond);
        _lastRefill = now;
    }
}
