namespace Engram.Store.Cloud;

/// <summary>
/// Pre-processes data before cloud transmission.
/// Strips private data (raw screen, clipboard, email bodies) and produces
/// sanitized state summaries that are safe to send to cloud providers.
/// Reduces token ingress by ~90%.
/// </summary>
public class LocalFilter
{
    /// <summary>
    /// Filter raw event data into a cloud-safe summary.
    /// Private and Sensitive data is stripped entirely.
    /// </summary>
    public FilterResult Filter(RawEvent rawEvent)
    {
        ArgumentNullException.ThrowIfNull(rawEvent);

        // Private/sensitive data is NEVER sent to cloud
        var privacy = ParsePrivacyClass(rawEvent.PrivacyClass);
        if (privacy == PrivacyClass.Private || privacy == PrivacyClass.Sensitive)
        {
            return new FilterResult
            {
                IsAllowed = false,
                Reason = $"Data classified as {privacy} — not sent to cloud.",
                FilteredPayload = string.Empty,
                OriginalSize = rawEvent.Text?.Length ?? 0,
                FilteredSize = 0
            };
        }

        // Internal/Public data: strip PII patterns, keep metadata
        var filtered = StripSensitivePatterns(rawEvent.Text ?? string.Empty);
        var summary = BuildSummary(rawEvent, filtered);

        return new FilterResult
        {
            IsAllowed = true,
            Reason = "Data sanitized for cloud transmission.",
            FilteredPayload = summary,
            OriginalSize = rawEvent.Text?.Length ?? 0,
            FilteredSize = summary.Length
        };
    }

    /// <summary>
    /// Filter arbitrary text with a given privacy class.
    /// </summary>
    public FilterResult FilterText(string text, PrivacyClass privacyClass)
    {
        if (privacyClass == PrivacyClass.Private || privacyClass == PrivacyClass.Sensitive)
        {
            return new FilterResult
            {
                IsAllowed = false,
                Reason = $"Data classified as {privacyClass} — not sent to cloud.",
                FilteredPayload = string.Empty,
                OriginalSize = text?.Length ?? 0,
                FilteredSize = 0
            };
        }

        var filtered = StripSensitivePatterns(text ?? string.Empty);

        return new FilterResult
        {
            IsAllowed = true,
            Reason = "Data sanitized for cloud transmission.",
            FilteredPayload = filtered,
            OriginalSize = text?.Length ?? 0,
            FilteredSize = filtered.Length
        };
    }

    private static PrivacyClass ParsePrivacyClass(string value)
    {
        return value?.ToLowerInvariant() switch
        {
            "public" => PrivacyClass.Public,
            "internal" => PrivacyClass.Internal,
            "private" => PrivacyClass.Private,
            "sensitive" => PrivacyClass.Sensitive,
            _ => PrivacyClass.Private // Default to most restrictive
        };
    }

    private static string StripSensitivePatterns(string text)
    {
        // Strip email addresses
        var result = System.Text.RegularExpressions.Regex.Replace(text, @"[\w.-]+@[\w.-]+\.\w+", "[EMAIL]");
        // Strip phone numbers (555-123-4567, 555.123.4567, 5551234567)
        result = System.Text.RegularExpressions.Regex.Replace(result, @"(?<!\w)\d{3}[-.]?\d{3}[-.]?\d{4}(?!\w)", "[PHONE]");
        // Strip potential tokens/keys (long hex/base64 strings)
        result = System.Text.RegularExpressions.Regex.Replace(result, @"(?<![A-Za-z0-9+/])[A-Za-z0-9+/]{40,}={0,2}(?![A-Za-z0-9+/])", "[TOKEN]");
        return result;
    }

    private static string BuildSummary(RawEvent rawEvent, string filteredText)
    {
        var source = rawEvent.Source ?? "unknown";
        var type = rawEvent.EventType ?? "unknown";
        var window = rawEvent.ActiveWindow ?? "unknown";
        var truncated = filteredText.Length > 500 ? filteredText[..500] + "..." : filteredText;

        return $"[source={source} type={type} window={window}] {truncated}";
    }
}

public class FilterResult
{
    public bool IsAllowed { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string FilteredPayload { get; init; } = string.Empty;
    public int OriginalSize { get; init; }
    public int FilteredSize { get; init; }

    public double ReductionRatio => OriginalSize > 0 ? 1.0 - ((double)FilteredSize / OriginalSize) : 0;
}
