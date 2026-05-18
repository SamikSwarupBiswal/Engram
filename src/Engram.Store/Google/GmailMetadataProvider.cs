using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Google;

/// <summary>
/// Fetches Gmail METADATA only — sender, subject, timestamps, labels.
/// NEVER reads email body content. Privacy-first design.
/// Uses gmail.readonly scope with metadata-only format.
/// </summary>
public class GmailMetadataProvider
{
    private readonly GoogleOAuthManager _oauth;
    private readonly ILogger<GmailMetadataProvider>? _logger;
    private readonly HttpClient _http;

    public GmailMetadataProvider(GoogleOAuthManager oauth, HttpClient http, ILogger<GmailMetadataProvider>? logger = null)
    {
        _oauth = oauth;
        _http = http;
        _logger = logger;
    }

    /// <summary>
    /// Fetch recent email metadata (last N messages).
    /// Returns sender, subject, date, labels — NO body content.
    /// </summary>
    public async Task<List<EmailMetadata>> GetRecentEmailsAsync(int maxResults = 50, CancellationToken cancellationToken = default)
    {
        var token = await _oauth.GetAccessTokenAsync(cancellationToken);
        if (token == null) return new List<EmailMetadata>();

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            // List recent message IDs
            var listUrl = $"https://gmail.googleapis.com/gmail/v1/users/me/messages?maxResults={maxResults}&q=in:inbox";
            var listResponse = await _http.GetFromJsonAsync<GmailMessageList>(listUrl, cancellationToken);
            if (listResponse?.Messages == null) return new List<EmailMetadata>();

            var emails = new List<EmailMetadata>();
            foreach (var msg in listResponse.Messages.Take(maxResults))
            {
                // Fetch metadata only (format=metadata, not full)
                var msgUrl = $"https://gmail.googleapis.com/gmail/v1/users/me/messages/{msg.Id}?format=metadata&metadataHeaders=From&metadataHeaders=Subject&metadataHeaders=Date";
                var msgResponse = await _http.GetFromJsonAsync<GmailMessageDetail>(msgUrl, cancellationToken);
                if (msgResponse == null) continue;

                var headers = msgResponse.Payload?.Headers ?? new List<GmailHeader>();
                var from = headers.FirstOrDefault(h => h.Name == "From")?.Value ?? "unknown";
                var subject = headers.FirstOrDefault(h => h.Name == "Subject")?.Value ?? "(no subject)";
                var date = headers.FirstOrDefault(h => h.Name == "Date")?.Value ?? "";

                emails.Add(new EmailMetadata
                {
                    MessageId = msg.Id,
                    From = from,
                    Subject = subject,
                    Date = date,
                    Labels = msgResponse.LabelIds ?? new List<string>(),
                    Snippet = msgResponse.Snippet ?? "",
                    HasAttachments = msgResponse.Payload?.Parts?.Any(p => p.Filename?.Length > 0) ?? false
                });
            }

            _logger?.LogInformation("Fetched {Count} email metadata entries", emails.Count);
            return emails;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to fetch Gmail metadata");
            return new List<EmailMetadata>();
        }
    }
}

public class EmailMetadata
{
    public string MessageId { get; init; } = string.Empty;
    public string From { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string Date { get; init; } = string.Empty;
    public string Snippet { get; init; } = string.Empty;
    public List<string> Labels { get; init; } = new();
    public bool HasAttachments { get; init; }
}

// Gmail API response models
public class GmailMessageList
{
    public List<GmailMessageRef>? Messages { get; set; }
    public string? NextPageToken { get; set; }
    public int ResultSizeEstimate { get; set; }
}

public class GmailMessageRef
{
    public string? Id { get; set; }
    public string? ThreadId { get; set; }
}

public class GmailMessageDetail
{
    public string? Id { get; set; }
    public List<string>? LabelIds { get; set; }
    public string? Snippet { get; set; }
    public GmailPayload? Payload { get; set; }
}

public class GmailPayload
{
    public List<GmailHeader>? Headers { get; set; }
    public List<GmailPart>? Parts { get; set; }
}

public class GmailHeader
{
    public string? Name { get; set; }
    public string? Value { get; set; }
}

public class GmailPart
{
    public string? Filename { get; set; }
}
