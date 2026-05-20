using Engram.Store.Events;
using Xunit;

namespace Engram.Store.Tests;

public class EventBusTests : IDisposable
{
    private readonly InMemoryEventBus _bus = new();

    public void Dispose()
    {
        _bus.Dispose();
    }

    // ── Publish/Subscribe ──

    [Fact]
    public void Publish_SubscriberReceivesEvent()
    {
        EventEnvelope? received = null;
        _bus.Subscribe(EventTypes.ChatCompleted, e => received = e);

        var envelope = new EventEnvelope
        {
            EventType = EventTypes.ChatCompleted,
            Source = "chat",
            Payload = new { Message = "hello" }
        };

        _bus.Publish(envelope);

        Assert.NotNull(received);
        Assert.Equal(EventTypes.ChatCompleted, received.EventType);
        Assert.Equal("chat", received.Source);
    }

    [Fact]
    public void Publish_MultipleSubscribers_AllReceive()
    {
        int count = 0;
        _bus.Subscribe(EventTypes.ChatCompleted, _ => Interlocked.Increment(ref count));
        _bus.Subscribe(EventTypes.ChatCompleted, _ => Interlocked.Increment(ref count));
        _bus.Subscribe(EventTypes.ChatCompleted, _ => Interlocked.Increment(ref count));

        _bus.Publish(new EventEnvelope { EventType = EventTypes.ChatCompleted });

        Assert.Equal(3, count);
    }

    [Fact]
    public void Publish_DifferentEventTypes_OnlyMatchingSubscribersReceive()
    {
        int chatCount = 0, wikiCount = 0;
        _bus.Subscribe(EventTypes.ChatCompleted, _ => Interlocked.Increment(ref chatCount));
        _bus.Subscribe(EventTypes.WikiNodeCreated, _ => Interlocked.Increment(ref wikiCount));

        _bus.Publish(new EventEnvelope { EventType = EventTypes.ChatCompleted });

        Assert.Equal(1, chatCount);
        Assert.Equal(0, wikiCount);
    }

    [Fact]
    public void SubscribeAll_ReceivesAllEvents()
    {
        int count = 0;
        _bus.SubscribeAll(_ => Interlocked.Increment(ref count));

        _bus.Publish(new EventEnvelope { EventType = EventTypes.ChatCompleted });
        _bus.Publish(new EventEnvelope { EventType = EventTypes.WikiNodeCreated });
        _bus.Publish(new EventEnvelope { EventType = EventTypes.CaptureDetected });

        Assert.Equal(3, count);
    }

    // ── Wildcard Subscriptions ──

    [Fact]
    public void Subscribe_Wildcard_MatchesPrefix()
    {
        int count = 0;
        _bus.Subscribe("wiki.*", _ => Interlocked.Increment(ref count));

        _bus.Publish(new EventEnvelope { EventType = EventTypes.WikiNodeCreated });
        _bus.Publish(new EventEnvelope { EventType = EventTypes.WikiNodeUpdated });
        _bus.Publish(new EventEnvelope { EventType = EventTypes.ChatCompleted });

        Assert.Equal(2, count);
    }

    // ── Unsubscribe ──

    [Fact]
    public void Subscribe_Dispose_Unsubscribes()
    {
        int count = 0;
        var sub = _bus.Subscribe(EventTypes.ChatCompleted, _ => Interlocked.Increment(ref count));

        _bus.Publish(new EventEnvelope { EventType = EventTypes.ChatCompleted });
        Assert.Equal(1, count);

        sub.Dispose();

        _bus.Publish(new EventEnvelope { EventType = EventTypes.ChatCompleted });
        Assert.Equal(1, count); // Should not increase
    }

    [Fact]
    public void Subscribe_DisposeMultipleTimes_NoError()
    {
        var sub = _bus.Subscribe(EventTypes.ChatCompleted, _ => { });
        sub.Dispose();
        sub.Dispose(); // Should not throw
    }

    // ── Event Envelope ──

    [Fact]
    public void EventEnvelope_HasDefaultValues()
    {
        var envelope = new EventEnvelope();

        Assert.NotEmpty(envelope.EventId);
        Assert.True(envelope.Timestamp > DateTimeOffset.MinValue);
        Assert.NotNull(envelope.Metadata);
    }

    [Fact]
    public void EventEnvelope_PreservesPayload()
    {
        EventEnvelope? received = null;
        _bus.Subscribe(EventTypes.ChatCompleted, e => received = e);

        var payload = new { User = "Samik", Message = "hello" };
        _bus.Publish(new EventEnvelope
        {
            EventType = EventTypes.ChatCompleted,
            Payload = payload
        });

        Assert.NotNull(received);
        Assert.NotNull(received.Payload);
    }

    [Fact]
    public void EventEnvelope_PreservesCorrelationId()
    {
        EventEnvelope? received = null;
        _bus.Subscribe(EventTypes.ChatCompleted, e => received = e);

        _bus.Publish(new EventEnvelope
        {
            EventType = EventTypes.ChatCompleted,
            CorrelationId = "conv-123"
        });

        Assert.Equal("conv-123", received!.CorrelationId);
    }

    // ── Statistics ──

    [Fact]
    public void EventsPublished_Increments()
    {
        Assert.Equal(0, _bus.EventsPublished);

        _bus.Publish(new EventEnvelope { EventType = EventTypes.ChatCompleted });
        _bus.Publish(new EventEnvelope { EventType = EventTypes.WikiNodeCreated });

        Assert.Equal(2, _bus.EventsPublished);
    }

    [Fact]
    public void SubscriberCount_ReflectsActiveSubscriptions()
    {
        Assert.Equal(0, _bus.SubscriberCount);

        var sub1 = _bus.Subscribe(EventTypes.ChatCompleted, _ => { });
        var sub2 = _bus.Subscribe(EventTypes.WikiNodeCreated, _ => { });

        Assert.Equal(2, _bus.SubscriberCount);

        sub1.Dispose();
        Assert.Equal(1, _bus.SubscriberCount);
    }

    // ── Error Handling ──

    [Fact]
    public void Publish_SubscriberException_DoesNotPropagate()
    {
        _bus.Subscribe(EventTypes.ChatCompleted, _ => throw new Exception("boom"));
        _bus.Subscribe(EventTypes.ChatCompleted, _ => { }); // Should still be called

        // Should not throw
        _bus.Publish(new EventEnvelope { EventType = EventTypes.ChatCompleted });
    }

    [Fact]
    public void Publish_SubscriberException_DoesNotBlockOtherSubscribers()
    {
        int count = 0;
        _bus.Subscribe(EventTypes.ChatCompleted, _ => throw new Exception("boom"));
        _bus.Subscribe(EventTypes.ChatCompleted, _ => Interlocked.Increment(ref count));

        _bus.Publish(new EventEnvelope { EventType = EventTypes.ChatCompleted });

        Assert.Equal(1, count);
    }

    // ── Disposal ──

    [Fact]
    public void Dispose_PublishAfterDispose_Throws()
    {
        _bus.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            _bus.Publish(new EventEnvelope { EventType = EventTypes.ChatCompleted }));
    }

    [Fact]
    public void Dispose_SubscribeAfterDispose_Throws()
    {
        _bus.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            _bus.Subscribe(EventTypes.ChatCompleted, _ => { }));
    }

    // ── Thread Safety ──

    [Fact]
    public void Publish_ConcurrentPublishes_DoNotCorrupt()
    {
        long count = 0;
        _bus.SubscribeAll(_ => Interlocked.Increment(ref count));

        var tasks = new List<Task>();
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                _bus.Publish(new EventEnvelope { EventType = EventTypes.ChatCompleted });
            }));
        }

        Task.WaitAll(tasks.ToArray());

        Assert.Equal(100, count);
        Assert.Equal(100, _bus.EventsPublished);
    }

    [Fact]
    public void Subscribe_ConcurrentSubscribes_DoNotCorrupt()
    {
        int count = 0;
        var tasks = new List<Task>();
        for (int i = 0; i < 50; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                _bus.Subscribe(EventTypes.ChatCompleted, _ => Interlocked.Increment(ref count));
            }));
        }

        Task.WaitAll(tasks.ToArray());

        _bus.Publish(new EventEnvelope { EventType = EventTypes.ChatCompleted });
        Assert.Equal(50, count);
    }

    // ── Event Types ──

    [Fact]
    public void EventTypes_AreWellDefined()
    {
        Assert.NotEmpty(EventTypes.ChatCompleted);
        Assert.NotEmpty(EventTypes.WikiNodeCreated);
        Assert.NotEmpty(EventTypes.WikiNodeUpdated);
        Assert.NotEmpty(EventTypes.CaptureDetected);
        Assert.NotEmpty(EventTypes.MemoryExtracted);
        Assert.NotEmpty(EventTypes.ResearchCompleted);
        Assert.NotEmpty(EventTypes.AutomationExecuted);
        Assert.NotEmpty(EventTypes.DriftDetected);
        Assert.NotEmpty(EventTypes.SystemStarted);
    }
}
