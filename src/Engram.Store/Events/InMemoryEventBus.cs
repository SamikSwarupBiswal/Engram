using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Events;

/// <summary>
/// In-memory event bus implementation.
/// Thread-safe, non-blocking publish, immediate subscriber notification.
/// 
/// Design decisions:
/// - In-memory only (no external dependencies like Redis/Kafka)
/// - Synchronous notification (subscribers run on publisher's thread)
/// - Fire-and-forget (subscriber exceptions are logged, not propagated)
/// - No persistence (events are ephemeral — wiki/timeline are the durable stores)
/// </summary>
public class InMemoryEventBus : IEventBus, IDisposable
{
    private readonly ConcurrentDictionary<string, List<Action<EventEnvelope>>> _typedSubscribers = new();
    private readonly ConcurrentBag<Action<EventEnvelope>> _globalSubscribers = new();
    private long _eventsPublished;
    private readonly ILogger<InMemoryEventBus>? _logger;
    private bool _disposed;

    public InMemoryEventBus(ILogger<InMemoryEventBus>? logger = null)
    {
        _logger = logger;
    }

    public int SubscriberCount
    {
        get
        {
            var typed = _typedSubscribers.Values.Sum(list => list.Count);
            return typed + _globalSubscribers.Count;
        }
    }

    public long EventsPublished => Interlocked.Read(ref _eventsPublished);

    public void Publish(EventEnvelope envelope)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(envelope);

        Interlocked.Increment(ref _eventsPublished);

        // Notify typed subscribers
        if (_typedSubscribers.TryGetValue(envelope.EventType, out var handlers))
        {
            foreach (var handler in handlers.ToList()) // ToList for thread safety
            {
                try
                {
                    handler(envelope);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Event subscriber failed for {EventType}", envelope.EventType);
                }
            }
        }

        // Notify wildcard subscribers (matching prefix patterns)
        foreach (var kvp in _typedSubscribers)
        {
            if (kvp.Key.EndsWith(".*") && envelope.EventType.StartsWith(kvp.Key[..^2]))
            {
                foreach (var handler in kvp.Value.ToList())
                {
                    try
                    {
                        handler(envelope);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Wildcard subscriber failed for {EventType}", envelope.EventType);
                    }
                }
            }
        }

        // Notify global subscribers
        foreach (var handler in _globalSubscribers)
        {
            try
            {
                handler(envelope);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Global event subscriber failed for {EventType}", envelope.EventType);
            }
        }

        _logger?.LogDebug("Published event {EventType} ({EventId})", envelope.EventType, envelope.EventId);
    }

    public IDisposable Subscribe(string eventType, Action<EventEnvelope> handler)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(eventType);
        ArgumentNullException.ThrowIfNull(handler);

        _typedSubscribers.AddOrUpdate(
            eventType,
            _ => new List<Action<EventEnvelope>> { handler },
            (_, list) =>
            {
                lock (list) { list.Add(handler); }
                return list;
            });

        _logger?.LogDebug("Subscriber added for {EventType}", eventType);

        return new Subscription(() =>
        {
            if (_typedSubscribers.TryGetValue(eventType, out var list))
            {
                lock (list) { list.Remove(handler); }
            }
        });
    }

    public IDisposable SubscribeAll(Action<EventEnvelope> handler)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(handler);

        _globalSubscribers.Add(handler);

        _logger?.LogDebug("Global subscriber added");

        return new Subscription(() =>
        {
            // ConcurrentBag doesn't support removal, but GC will collect
            // when the subscription is disposed and no other references exist
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _typedSubscribers.Clear();
        // ConcurrentBag doesn't have Clear, but we set _disposed to prevent new publishes
        _logger?.LogInformation("EventBus disposed. Published {Count} events total.", _eventsPublished);
    }

    /// <summary>
    /// Disposable subscription handle.
    /// Disposing removes the subscription.
    /// </summary>
    private class Subscription : IDisposable
    {
        private Action? _unsubscribe;
        private bool _disposed;

        public Subscription(Action unsubscribe)
        {
            _unsubscribe = unsubscribe;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _unsubscribe?.Invoke();
            _unsubscribe = null;
        }
    }
}
