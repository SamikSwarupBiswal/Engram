using Engram.Store.Events;
using Engram.Store.Metabolism;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Events;

/// <summary>
/// Subscribes to the event bus and writes events to the timeline.
/// This is the bridge that makes the timeline alive — events from all subsystems
/// automatically flow into the persistent event store.
/// 
/// Pipeline:
///   EventBus → TimelineSubscriber → RawEventWriter → timeline
/// </summary>
public class TimelineSubscriber : IDisposable
{
    private readonly IEventBus _eventBus;
    private readonly RawEventWriter _writer;
    private readonly CognitiveTelemetry? _telemetry;
    private readonly ILogger<TimelineSubscriber>? _logger;
    private readonly List<IDisposable> _subscriptions = new();
    private bool _disposed;

    public TimelineSubscriber(
        IEventBus eventBus,
        RawEventWriter writer,
        CognitiveTelemetry? telemetry = null,
        ILogger<TimelineSubscriber>? logger = null)
    {
        _eventBus = eventBus;
        _writer = writer;
        _telemetry = telemetry;
        _logger = logger;
    }

    /// <summary>
    /// Start subscribing to all events on the bus.
    /// Call once during application startup.
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Subscribe to all events
        _subscriptions.Add(_eventBus.SubscribeAll(OnEvent));

        _logger?.LogInformation("TimelineSubscriber started. Subscribed to all events.");
    }

    /// <summary>
    /// Handle an event from the bus — write it to the timeline.
    /// </summary>
    private void OnEvent(EventEnvelope envelope)
    {
        try
        {
            var rawEvent = ConvertToRawEvent(envelope);
            _writer.Write(rawEvent);
            _telemetry?.RecordTimelineEventWritten(envelope.EventType);
            _telemetry?.RecordEventBusPublish();

            _logger?.LogDebug("Timeline event written: {EventType} ({EventId})",
                envelope.EventType, envelope.EventId);
        }
        catch (Exception ex)
        {
            _telemetry?.RecordTimelineWriteFailure();
            _logger?.LogWarning(ex, "Failed to write timeline event: {EventType}", envelope.EventType);
        }
    }

    /// <summary>
    /// Convert an EventEnvelope to a RawEvent for persistence.
    /// </summary>
    private static RawEvent ConvertToRawEvent(EventEnvelope envelope)
    {
        var metadata = new Dictionary<string, string>(envelope.Metadata)
        {
            ["event_bus_id"] = envelope.EventId,
            ["source_subsystem"] = envelope.Source
        };

        if (envelope.CorrelationId != null)
            metadata["correlation_id"] = envelope.CorrelationId;

        // Extract text from payload if possible
        var text = ExtractTextFromPayload(envelope.Payload);

        return new RawEvent
        {
            EventId = envelope.EventId,
            EventType = envelope.EventType,
            CapturedAt = envelope.Timestamp,
            Source = envelope.Source,
            Text = text,
            Metadata = metadata,
            PrivacyClass = "private",
            Hash = ComputeHash(envelope.EventId + envelope.EventType),
            ProcessingStatus = "processed"
        };
    }

    /// <summary>
    /// Extract text representation from the event payload.
    /// </summary>
    private static string? ExtractTextFromPayload(object? payload)
    {
        if (payload == null) return null;

        // If payload is a string, use it directly
        if (payload is string s) return s;

        // For anonymous types and other objects, serialize to JSON
        try
        {
            return System.Text.Json.JsonSerializer.Serialize(payload);
        }
        catch
        {
            return payload.ToString();
        }
    }

    private static string ComputeHash(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash)[..16];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var sub in _subscriptions)
        {
            try { sub.Dispose(); } catch { }
        }
        _subscriptions.Clear();

        _logger?.LogInformation("TimelineSubscriber disposed.");
    }
}
