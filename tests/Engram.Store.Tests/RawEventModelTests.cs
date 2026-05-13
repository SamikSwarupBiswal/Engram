using System.Text.Json;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for the raw event data model and serialization.
/// Derived from: REQ-003, Implementation Plan §4
///
/// PRD Contract:
/// - Fields: event_id, event_type, captured_at, source, source_uri,
///   active_window, text, metadata, privacy_class, hash, processing_status
/// - "Use .NET naming conventions in code while preserving JSON field names as snake_case"
/// </summary>
public class RawEventModelTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    [Fact]
    public void RawEvent_HasAllRequiredFields()
    {
        // REQ-003: All 11 fields must exist
        var evt = new RawEvent();

        Assert.NotNull(evt.EventId);
        Assert.NotNull(evt.EventType);
        Assert.NotNull(evt.Source);
        Assert.NotNull(evt.PrivacyClass);
        Assert.NotNull(evt.Hash);
        Assert.NotNull(evt.ProcessingStatus);
        // CapturedAt is a struct, always has a value
    }

    [Fact]
    public void RawEvent_DefaultValues_AreCorrect()
    {
        var evt = new RawEvent();

        Assert.Equal(string.Empty, evt.EventId);
        Assert.Equal(string.Empty, evt.EventType);
        Assert.Equal("private", evt.PrivacyClass);
        Assert.Equal("pending", evt.ProcessingStatus);
        Assert.Equal(string.Empty, evt.Hash);
        Assert.Null(evt.SourceUri);
        Assert.Null(evt.ActiveWindow);
        Assert.Null(evt.Text);
        Assert.Null(evt.Metadata);
    }

    [Fact]
    public void Serialize_ProducesSnakeCaseFieldNames()
    {
        // REQ-003: JSON field names as snake_case
        var evt = TestEvents.Create();
        evt.EventId = "test-123";
        evt.EventType = "screen_capture";
        evt.Source = "ocr";
        evt.PrivacyClass = "sensitive";
        evt.ProcessingStatus = "processed";

        var json = JsonSerializer.Serialize(evt, JsonOptions);

        Assert.Contains("\"event_id\": \"test-123\"", json);
        Assert.Contains("\"event_type\": \"screen_capture\"", json);
        Assert.Contains("\"source\": \"ocr\"", json);
        Assert.Contains("\"captured_at\":", json);
        Assert.Contains("\"source_uri\":", json);
        Assert.Contains("\"active_window\":", json);
        Assert.Contains("\"text\":", json);
        Assert.Contains("\"privacy_class\": \"sensitive\"", json);
        Assert.Contains("\"hash\":", json);
        Assert.Contains("\"processing_status\": \"processed\"", json);
        Assert.Contains("\"metadata\":", json);
    }

    [Fact]
    public void SerializeDeserialize_RoundTrips_AllFields()
    {
        // Full round-trip preserves all data
        var original = TestEvents.CreateWithMetadata();

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<RawEvent>(json, JsonOptions)!;

        Assert.Equal(original.EventId, deserialized.EventId);
        Assert.Equal(original.EventType, deserialized.EventType);
        Assert.Equal(original.CapturedAt, deserialized.CapturedAt);
        Assert.Equal(original.Source, deserialized.Source);
        Assert.Equal(original.SourceUri, deserialized.SourceUri);
        Assert.Equal(original.ActiveWindow, deserialized.ActiveWindow);
        Assert.Equal(original.Text, deserialized.Text);
        Assert.Equal(original.PrivacyClass, deserialized.PrivacyClass);
        Assert.Equal(original.Hash, deserialized.Hash);
        Assert.Equal(original.ProcessingStatus, deserialized.ProcessingStatus);
        Assert.NotNull(deserialized.Metadata);
        Assert.Equal(original.Metadata!.Count, deserialized.Metadata.Count);
        Assert.Equal("report.pdf", deserialized.Metadata["file_name"]);
    }

    [Fact]
    public void Serialize_WithNullOptionalFields_ProducesNullInJson()
    {
        var evt = TestEvents.Create();
        evt.SourceUri = null;
        evt.ActiveWindow = null;
        evt.Text = null;
        evt.Metadata = null;

        var json = JsonSerializer.Serialize(evt, JsonOptions);

        Assert.Contains("\"source_uri\": null", json);
        Assert.Contains("\"active_window\": null", json);
        Assert.Contains("\"text\": null", json);
        Assert.Contains("\"metadata\": null", json);
    }

    [Fact]
    public void Serialize_WithEmptyMetadata_ProducesEmptyObject()
    {
        var evt = TestEvents.Create();
        evt.Metadata = new Dictionary<string, string>();

        var json = JsonSerializer.Serialize(evt, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<RawEvent>(json, JsonOptions)!;

        Assert.NotNull(deserialized.Metadata);
        Assert.Empty(deserialized.Metadata);
    }

    [Fact]
    public void CapturedAt_PreservesTimezoneOffset()
    {
        var offset = new TimeSpan(5, 30, 0); // IST
        var evt = TestEvents.Create(capturedAt: new DateTimeOffset(2026, 5, 13, 10, 30, 0, offset));

        var json = JsonSerializer.Serialize(evt, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<RawEvent>(json, JsonOptions)!;

        Assert.Equal(evt.CapturedAt, deserialized.CapturedAt);
        Assert.Equal(offset, deserialized.CapturedAt.Offset);
    }
}
