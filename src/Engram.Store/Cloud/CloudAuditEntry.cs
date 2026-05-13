using System.Text.Json.Serialization;

namespace Engram.Store.Cloud;

/// <summary>
/// Audit entry for every cloud model call.
/// Stored in .engram/logs/cloud-audit.jsonl (append-only JSONL).
/// Derived from PRD: "Every cloud call records reason, provider, payload summary, and cost."
/// </summary>
public class CloudAuditEntry
{
    [JsonPropertyName("entry_id")]
    public string EntryId { get; init; } = Guid.NewGuid().ToString("n");

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;

    [JsonPropertyName("provider")]
    public string Provider { get; init; } = string.Empty;

    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("payload_summary")]
    public string PayloadSummary { get; init; } = string.Empty;

    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; init; }

    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; init; }

    [JsonPropertyName("cost_usd")]
    public decimal CostUsd { get; init; }

    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; init; }

    [JsonPropertyName("from_cache")]
    public bool FromCache { get; init; }

    [JsonPropertyName("task_complexity")]
    public string TaskComplexity { get; init; } = string.Empty;

    [JsonPropertyName("compute_target")]
    public string ComputeTarget { get; init; } = string.Empty;
}
