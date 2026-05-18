using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Google;

/// <summary>
/// Manages Google Workspace integration.
/// Orchestrates OAuth, Gmail, Calendar, and Drive metadata ingestion.
/// Privacy-first: metadata only, never reads content.
/// </summary>
public class GoogleWorkspaceManager : IDisposable
{
    public GoogleOAuthManager OAuth { get; }
    public GmailMetadataProvider Gmail { get; }
    public CalendarMetadataProvider Calendar { get; }
    public DriveMetadataProvider Drive { get; }

    private readonly ILogger<GoogleWorkspaceManager>? _logger;
    private readonly HttpClient _http;

    public GoogleWorkspaceManager(string configDir, ILogger<GoogleWorkspaceManager>? logger = null)
    {
        _logger = logger;
        _http = new HttpClient();

        var oauthLogger = logger as ILogger<GoogleOAuthManager>;
        var gmailLogger = logger as ILogger<GmailMetadataProvider>;
        var calLogger = logger as ILogger<CalendarMetadataProvider>;
        var driveLogger = logger as ILogger<DriveMetadataProvider>;

        OAuth = new GoogleOAuthManager(configDir, oauthLogger);
        Gmail = new GmailMetadataProvider(OAuth, _http, gmailLogger);
        Calendar = new CalendarMetadataProvider(OAuth, _http, calLogger);
        Drive = new DriveMetadataProvider(OAuth, _http, driveLogger);
    }

    /// <summary>
    /// Run a full metadata sync across all connected services.
    /// Returns a summary of what was ingested.
    /// </summary>
    public async Task<GwsSyncResult> SyncAllAsync(CancellationToken cancellationToken = default)
    {
        var result = new GwsSyncResult();

        if (!OAuth.IsAuthenticated)
        {
            result.Error = "Not authenticated with Google";
            return result;
        }

        result.Email = OAuth.UserEmail;

        // Fetch Gmail metadata
        try
        {
            result.Emails = await Gmail.GetRecentEmailsAsync(50, cancellationToken);
            result.EmailCount = result.Emails.Count;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Gmail sync failed");
            result.Errors.Add($"Gmail: {ex.Message}");
        }

        // Fetch Calendar metadata
        try
        {
            result.Events = await Calendar.GetUpcomingEventsAsync(7, 50, cancellationToken);
            result.EventCount = result.Events.Count;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Calendar sync failed");
            result.Errors.Add($"Calendar: {ex.Message}");
        }

        // Fetch Drive metadata
        try
        {
            result.Files = await Drive.GetRecentFilesAsync(50, cancellationToken);
            result.FileCount = result.Files.Count;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Drive sync failed");
            result.Errors.Add($"Drive: {ex.Message}");
        }

        result.Success = result.Errors.Count == 0;
        result.SyncedAt = DateTimeOffset.UtcNow;

        _logger?.LogInformation("GWS sync complete: {Emails} emails, {Events} events, {Files} files",
            result.EmailCount, result.EventCount, result.FileCount);

        return result;
    }

    /// <summary>
    /// Get the OAuth authorization URL for the user to visit.
    /// </summary>
    public static string GetAuthorizationUrl(string clientId, string redirectUri)
    {
        var scopes = Uri.EscapeDataString(string.Join(" ", Scopes));
        return $"https://accounts.google.com/o/oauth2/v2/auth?client_id={clientId}&redirect_uri={Uri.EscapeDataString(redirectUri)}&response_type=code&scope={scopes}&access_type=offline&prompt=consent";
    }

    private static readonly string[] Scopes = GoogleOAuthManager.Scopes;

    public void Dispose()
    {
        OAuth.Dispose();
        _http.Dispose();
    }
}

public class GwsSyncResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Email { get; set; }
    public DateTimeOffset SyncedAt { get; set; }
    public int EmailCount { get; set; }
    public int EventCount { get; set; }
    public int FileCount { get; set; }
    public List<EmailMetadata> Emails { get; set; } = new();
    public List<CalendarEventMetadata> Events { get; set; } = new();
    public List<DriveFileMetadata> Files { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}
