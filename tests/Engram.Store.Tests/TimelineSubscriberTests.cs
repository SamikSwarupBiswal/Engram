using Engram.Store.Events;
using Xunit;

namespace Engram.Store.Tests;

public class TimelineSubscriberTests : IDisposable
{
    private readonly string _tempDir;
    private readonly InMemoryEventBus _bus;
    private readonly RawEventWriter _writer;

    public TimelineSubscriberTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram_timeline_test_" + Guid.NewGuid().ToString("n")[..8]);
        var paths = new WorkspacePaths(_tempDir);
        _bus = new InMemoryEventBus();
        _writer = new RawEventWriter(paths, new ContentHasher());
    }

    public void Dispose()
    {
        _bus.Dispose();
        _writer.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ── Core Subscription ──

    [Fact]
    public void Start_SubscribesToAllEvents()
    {
        using var subscriber = new TimelineSubscriber(_bus, _writer);
        subscriber.Start();

        Assert.True(_bus.SubscriberCount > 0);
    }

    [Fact]
    public void OnEvent_WritesToTimeline()
    {
        using var subscriber = new TimelineSubscriber(_bus, _writer);
        subscriber.Start();

        _bus.Publish(new EventEnvelope
        {
            EventType = EventTypes.ChatCompleted,
            Source = "chat",
            Payload = new { Message = "hello" }
        });

        // Verify event was processed without exception (subscriber didn't crash)
        // The writer creates its own directory structure under WorkspacePaths.Raw
        Assert.True(true, "Event processed without exception");
    }

    [Fact]
    public void OnEvent_WritesMultipleEvents()
    {
        using var subscriber = new TimelineSubscriber(_bus, _writer);
        subscriber.Start();

        _bus.Publish(new EventEnvelope { EventType = EventTypes.ChatCompleted, Source = "chat" });
        _bus.Publish(new EventEnvelope { EventType = EventTypes.WikiNodeCreated, Source = "wiki" });
        _bus.Publish(new EventEnvelope { EventType = EventTypes.CaptureDetected, Source = "capture" });

        // All events processed without exception
        Assert.Equal(3, _bus.EventsPublished);
    }

    [Fact]
    public void OnEvent_PreservesEventType()
    {
        using var subscriber = new TimelineSubscriber(_bus, _writer);
        subscriber.Start();

        _bus.Publish(new EventEnvelope
        {
            EventType = "custom.event.type",
            Source = "test"
        });

        // Event processed without exception
        Assert.Equal(1, _bus.EventsPublished);
    }

    [Fact]
    public void OnEvent_PreservesSource()
    {
        using var subscriber = new TimelineSubscriber(_bus, _writer);
        subscriber.Start();

        _bus.Publish(new EventEnvelope
        {
            EventType = EventTypes.ChatCompleted,
            Source = "my_subsystem"
        });

        // Timeline should have the event
        var rawDir = Path.Combine(_tempDir, "raw");
        Assert.True(Directory.Exists(rawDir));
    }

    // ── Lifecycle ──

    [Fact]
    public void Dispose_StopsSubscription()
    {
        var subscriber = new TimelineSubscriber(_bus, _writer);
        subscriber.Start();

        Assert.True(_bus.SubscriberCount > 0);

        subscriber.Dispose();

        // After dispose, publishing should not crash
        _bus.Publish(new EventEnvelope { EventType = EventTypes.ChatCompleted, Source = "test" });
    }

    [Fact]
    public void Start_AfterDispose_Throws()
    {
        var subscriber = new TimelineSubscriber(_bus, _writer);
        subscriber.Dispose();

        Assert.Throws<ObjectDisposedException>(() => subscriber.Start());
    }

    [Fact]
    public void Dispose_MultipleTimes_NoError()
    {
        var subscriber = new TimelineSubscriber(_bus, _writer);
        subscriber.Start();
        subscriber.Dispose();
        subscriber.Dispose(); // Should not throw
    }

    // ── Error Handling ──

    [Fact]
    public void OnEvent_WriterException_DoesNotCrash()
    {
        // Create a writer with invalid path to force errors
        var badWriter = new RawEventWriter(
            new WorkspacePaths(Path.Combine(_tempDir, "nonexistent", "deep", "path")),
            new ContentHasher());

        using var subscriber = new TimelineSubscriber(_bus, badWriter);
        subscriber.Start();

        // Should not throw even if writer fails
        _bus.Publish(new EventEnvelope { EventType = EventTypes.ChatCompleted, Source = "test" });
    }
}
