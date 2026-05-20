namespace Engram.Store;

/// <summary>
/// Simple API rate limiter using sliding window.
/// Limits to N requests per minute per instance.
/// </summary>
public class ApiRateLimiter
{
    private readonly int _permitLimit;
    private readonly Queue<DateTimeOffset> _timestamps = new();
    private readonly object _lock = new();

    public ApiRateLimiter(int permitLimit = 100)
    {
        _permitLimit = permitLimit;
    }

    /// <summary>
    /// Try to acquire a permit. Returns false if rate limit exceeded.
    /// </summary>
    public bool TryAcquire()
    {
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow;
            var windowStart = now.AddMinutes(-1);

            // Remove expired entries
            while (_timestamps.Count > 0 && _timestamps.Peek() < windowStart)
                _timestamps.Dequeue();

            if (_timestamps.Count >= _permitLimit)
                return false;

            _timestamps.Enqueue(now);
            return true;
        }
    }

    /// <summary>
    /// Get current request count in the window.
    /// </summary>
    public int CurrentCount
    {
        get
        {
            lock (_lock)
            {
                var now = DateTimeOffset.UtcNow;
                var windowStart = now.AddMinutes(-1);
                while (_timestamps.Count > 0 && _timestamps.Peek() < windowStart)
                    _timestamps.Dequeue();
                return _timestamps.Count;
            }
        }
    }
}
