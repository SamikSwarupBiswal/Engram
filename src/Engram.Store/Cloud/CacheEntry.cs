using System.Text.Json.Serialization;

namespace Engram.Store.Cloud;

/// <summary>
/// A cached cloud model response for common non-private research topics.
/// </summary>
public class CacheEntry
{
    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    [JsonPropertyName("response")]
    public string Response { get; init; } = string.Empty;

    [JsonPropertyName("provider")]
    public string Provider { get; init; } = string.Empty;

    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("cost_usd")]
    public decimal CostUsd { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("hit_count")]
    public int HitCount { get; init; }

    [JsonPropertyName("last_hit_at")]
    public DateTimeOffset? LastHitAt { get; init; }

    [JsonPropertyName("ttl_hours")]
    public int TtlHours { get; init; } = 168; // 7 days default

    [JsonIgnore]
    public bool IsExpired => DateTimeOffset.UtcNow > CreatedAt.AddHours(TtlHours);
}
