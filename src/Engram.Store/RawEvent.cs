namespace Engram.Store;

/// <summary>
/// A raw event in the Engram store.
/// Fields use snake_case in JSON via JsonPropertyName attributes.
/// </summary>
public class RawEvent
{
    [System.Text.Json.Serialization.JsonPropertyName("event_id")]
    public string EventId { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("event_type")]
    public string EventType { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("captured_at")]
    public DateTimeOffset CapturedAt { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("source_uri")]
    public string? SourceUri { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("active_window")]
    public string? ActiveWindow { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("text")]
    public string? Text { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("privacy_class")]
    public string PrivacyClass { get; set; } = "private";

    [System.Text.Json.Serialization.JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("processing_status")]
    public string ProcessingStatus { get; set; } = "pending";
}
