namespace Engram.Store.Cloud;

/// <summary>
/// Rate limiter for cloud API calls.
/// Enforces per-user requests-per-minute and requests-per-hour limits.
/// Uses sliding window with timestamp tracking.
/// </summary>
public class CloudRateLimiter
{
    private readonly int _maxPerMinute;
    private readonly int _maxPerHour;
    private readonly Queue<DateTimeOffset> _recentCalls = new();
    private readonly object _lock = new();

    public CloudRateLimiter(int maxPerMinute = 20, int maxPerHour = 200)
    {
        if (maxPerMinute <= 0) throw new ArgumentOutOfRangeException(nameof(maxPerMinute));
        if (maxPerHour <= 0) throw new ArgumentOutOfRangeException(nameof(maxPerHour));
        if (maxPerMinute > maxPerHour) throw new ArgumentException("Per-minute limit cannot exceed per-hour limit.");

        _maxPerMinute = maxPerMinute;
        _maxPerHour = maxPerHour;
    }

    /// <summary>
    /// Check if a cloud call is allowed under the rate limit.
    /// Returns allowed/denied with reason.
    /// </summary>
    public RateLimitResult CheckRateLimit()
    {
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow;
            PurgeExpired(now);

            var callsInLastMinute = 0;
            var callsInLastHour = 0;
            var oneMinuteAgo = now.AddMinutes(-1);
            var oneHourAgo = now.AddHours(-1);

            foreach (var call in _recentCalls)
            {
                if (call >= oneMinuteAgo) callsInLastMinute++;
                if (call >= oneHourAgo) callsInLastHour++;
            }

            if (callsInLastMinute >= _maxPerMinute)
                return RateLimitResult.Denied(
                    $"Rate limit exceeded: {callsInLastMinute} calls in the last minute (limit: {_maxPerMinute}).",
                    callsInLastMinute, callsInLastHour);

            if (callsInLastHour >= _maxPerHour)
                return RateLimitResult.Denied(
                    $"Rate limit exceeded: {callsInLastHour} calls in the last hour (limit: {_maxPerHour}).",
                    callsInLastMinute, callsInLastHour);

            return RateLimitResult.Allowed(callsInLastMinute, callsInLastHour);
        }
    }

    /// <summary>
    /// Record a cloud call for rate limiting purposes.
    /// Call this AFTER a successful or attempted cloud call.
    /// </summary>
    public void RecordCall()
    {
        lock (_lock)
        {
            _recentCalls.Enqueue(DateTimeOffset.UtcNow);
        }
    }

    /// <summary>
    /// Check rate limit and record the call in one operation.
    /// </summary>
    public RateLimitResult CheckAndRecord()
    {
        lock (_lock)
        {
            var result = CheckRateLimit();
            if (result.IsAllowed)
            {
                _recentCalls.Enqueue(DateTimeOffset.UtcNow);
            }
            return result;
        }
    }

    /// <summary>Number of calls currently tracked in the sliding window.</summary>
    public int TrackedCallCount
    {
        get
        {
            lock (_lock)
            {
                PurgeExpired(DateTimeOffset.UtcNow);
                return _recentCalls.Count;
            }
        }
    }

    private void PurgeExpired(DateTimeOffset now)
    {
        var oneHourAgo = now.AddHours(-1);
        while (_recentCalls.Count > 0 && _recentCalls.Peek() < oneHourAgo)
        {
            _recentCalls.Dequeue();
        }
    }
}

public class RateLimitResult
{
    public bool IsAllowed { get; init; }
    public string? DenyReason { get; init; }
    public int CallsInLastMinute { get; init; }
    public int CallsInLastHour { get; init; }

    public static RateLimitResult Allowed(int callsInMinute, int callsInHour) => new()
    {
        IsAllowed = true,
        CallsInLastMinute = callsInMinute,
        CallsInLastHour = callsInHour
    };

    public static RateLimitResult Denied(string reason, int callsInMinute, int callsInHour) => new()
    {
        IsAllowed = false,
        DenyReason = reason,
        CallsInLastMinute = callsInMinute,
        CallsInLastHour = callsInHour
    };
}
