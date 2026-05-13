namespace Engram.Store;

/// <summary>
/// Mutable processing state for a raw event.
/// Stored in a .meta.json sidecar file, separate from the immutable raw event payload.
/// </summary>
public class ProcessingState
{
    [System.Text.Json.Serialization.JsonPropertyName("processing_status")]
    public string Status { get; set; } = "pending";

    [System.Text.Json.Serialization.JsonPropertyName("last_processed_at")]
    public DateTimeOffset? LastProcessedAt { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("processing_error")]
    public string? Error { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("retry_count")]
    public int RetryCount { get; set; }
}
