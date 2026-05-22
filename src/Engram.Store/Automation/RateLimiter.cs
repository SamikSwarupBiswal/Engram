using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Engram.Store.Automation;

/// <summary>
/// Throttles automated interactions to prevent macro runaway, click storms,
/// and execution hysteresis oscillation.
/// </summary>
public class RateLimiter
{
    private DateTimeOffset _lastKeystrokeTime = DateTimeOffset.MinValue;
    private DateTimeOffset _lastMouseTime = DateTimeOffset.MinValue;
    private readonly Queue<DateTimeOffset> _actionHistory = new();
    private int _consecutiveReplans = 0;
    private DateTimeOffset _lastReplanTime = DateTimeOffset.MinValue;

    private readonly int _keystrokeDelayMs;
    private readonly int _mouseDelayMs;
    private readonly int _maxActionsPerMinute;

    public RateLimiter(int keystrokeDelayMs = 150, int mouseDelayMs = 500, int maxActionsPerMinute = 30)
    {
        _keystrokeDelayMs = keystrokeDelayMs;
        _mouseDelayMs = mouseDelayMs;
        _maxActionsPerMinute = maxActionsPerMinute;
    }

    /// <summary>
    /// Throttles execution and sleeps if necessary to satisfy rate limits and interaction delays.
    /// </summary>
    public async Task ThrottleActionAsync(ActionType type, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        // 1. Budget enforcement (sliding window 1 minute)
        CleanActionHistory(now);
        if (_actionHistory.Count >= _maxActionsPerMinute)
        {
            var oldest = _actionHistory.Peek();
            var waitTime = oldest.AddMinutes(1) - now;
            if (waitTime > TimeSpan.Zero)
            {
                await Task.Delay(waitTime, ct);
                now = DateTimeOffset.UtcNow;
                CleanActionHistory(now);
            }
        }

        // 2. Interaction delays
        if (type == ActionType.Type || type == ActionType.KeyPress)
        {
            var elapsed = now - _lastKeystrokeTime;
            var minDelay = TimeSpan.FromMilliseconds(_keystrokeDelayMs);
            if (elapsed < minDelay)
            {
                await Task.Delay(minDelay - elapsed, ct);
            }
            _lastKeystrokeTime = DateTimeOffset.UtcNow;
        }
        else if (type == ActionType.Click)
        {
            var elapsed = now - _lastMouseTime;
            var minDelay = TimeSpan.FromMilliseconds(_mouseDelayMs);
            if (elapsed < minDelay)
            {
                await Task.Delay(minDelay - elapsed, ct);
            }
            _lastMouseTime = DateTimeOffset.UtcNow;
        }

        _actionHistory.Enqueue(DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Records a replan action and checks if it exceeds hysteresis damping bounds.
    /// Throws InvalidOperationException if consecutive replans occur too rapidly.
    /// </summary>
    public void RecordReplan()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastReplanTime < TimeSpan.FromMinutes(2))
        {
            _consecutiveReplans++;
        }
        else
        {
            _consecutiveReplans = 1;
        }

        _lastReplanTime = now;

        if (_consecutiveReplans > 3)
        {
            throw new InvalidOperationException("Execution halted: Re-planning rate exceeded safety hysteresis damping limits. Avoided replan oscillation.");
        }
    }

    private void CleanActionHistory(DateTimeOffset now)
    {
        var cutoff = now.AddMinutes(-1);
        while (_actionHistory.Count > 0 && _actionHistory.Peek() < cutoff)
        {
            _actionHistory.Dequeue();
        }
    }
}
