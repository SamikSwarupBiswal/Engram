using System.Collections.Concurrent;

namespace Engram.Store.Capture;

/// <summary>
/// Debounces rapid events. Coalesces multiple events for the same key
/// into a single callback after a quiet period.
/// Thread-safe.
/// </summary>
public class Debouncer<TKey> : IDisposable where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, Timer> _timers = new();
    private readonly TimeSpan _delay;
    private readonly Action<TKey> _callback;
    private bool _disposed;

    public Debouncer(TimeSpan delay, Action<TKey> callback)
    {
        _delay = delay;
        _callback = callback;
    }

    /// <summary>
    /// Register an event for the given key. Resets the debounce timer.
    /// The callback fires only after _delay with no new events for this key.
    /// </summary>
    public void Debounce(TKey key)
    {
        if (_disposed) return;

        var timer = _timers.GetOrAdd(key, _ => new Timer(OnTimerElapsed, key, Timeout.Infinite, Timeout.Infinite));
        timer.Change((int)_delay.TotalMilliseconds, Timeout.Infinite);
    }

    private void OnTimerElapsed(object? state)
    {
        if (state is TKey key)
        {
            _timers.TryRemove(key, out _);
            _callback(key);
        }
    }

    /// <summary>
    /// Count of pending (not yet fired) debounced events.
    /// </summary>
    public int PendingCount => _timers.Count;

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            foreach (var timer in _timers.Values)
                timer.Dispose();
            _timers.Clear();
        }
    }
}
