namespace Engram.Store.Events;

/// <summary>
/// Central event bus for all Engram subsystems.
/// Every subsystem publishes events here. Subscribers receive them.
/// This is the nervous system that connects isolated microservices into one organism.
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// Publish an event to all subscribers.
    /// Non-blocking — subscribers are notified asynchronously.
    /// </summary>
    void Publish(EventEnvelope envelope);

    /// <summary>
    /// Subscribe to events of a specific type.
    /// Returns a disposable subscription handle.
    /// </summary>
    IDisposable Subscribe(string eventType, Action<EventEnvelope> handler);

    /// <summary>
    /// Subscribe to all events regardless of type.
    /// Returns a disposable subscription handle.
    /// </summary>
    IDisposable SubscribeAll(Action<EventEnvelope> handler);

    /// <summary>
    /// Get the count of active subscribers.
    /// </summary>
    int SubscriberCount { get; }

    /// <summary>
    /// Get the total number of events published since startup.
    /// </summary>
    long EventsPublished { get; }
}
