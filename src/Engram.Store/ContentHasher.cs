using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Engram.Store;

/// <summary>
/// Computes deterministic content hashes for raw events.
/// Uses SHA-256 over stable event content (excluding event_id, hash, and processing_status).
/// </summary>
public class ContentHasher
{
    private static readonly JsonSerializerOptions HashJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    /// <summary>
    /// Computes a deterministic SHA-256 hash from the event's stable content fields.
    /// Excludes: event_id (filename, not content), hash (circular), processing_status (mutable metadata).
    /// Includes: event_type, captured_at, source, source_uri, active_window, text, metadata, privacy_class.
    /// </summary>
    public string ComputeHash(RawEvent rawEvent)
    {
        ArgumentNullException.ThrowIfNull(rawEvent);

        // Build a canonical representation of stable content
        var stableContent = new
        {
            rawEvent.EventType,
            rawEvent.CapturedAt,
            rawEvent.Source,
            rawEvent.SourceUri,
            rawEvent.ActiveWindow,
            rawEvent.Text,
            rawEvent.Metadata,
            rawEvent.PrivacyClass
        };

        var canonical = JsonSerializer.Serialize(stableContent, HashJsonOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));

        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
