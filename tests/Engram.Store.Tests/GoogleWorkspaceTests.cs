using Engram.Store.Google;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for Google Workspace metadata ingestion.
/// Tests OAuth flow, provider behavior, and manager orchestration.
/// Does NOT make real Google API calls — tests with mock/null tokens.
/// </summary>
public class GoogleWorkspaceTests : IDisposable
{
    private readonly string _tempDir;

    public GoogleWorkspaceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram-gws-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ─── OAuth Manager ───

    [Fact]
    public void OAuth_NewManager_NotAuthenticated()
    {
        var oauth = new GoogleOAuthManager(_tempDir);
        Assert.False(oauth.IsAuthenticated);
        Assert.Null(oauth.UserEmail);
    }

    [Fact]
    public void OAuth_GetStatus_ReturnsNotAuthenticated()
    {
        var oauth = new GoogleOAuthManager(_tempDir);
        var status = oauth.GetStatus();

        Assert.False(status.IsAuthenticated);
        Assert.Null(status.Email);
        Assert.Empty(status.Scopes);
    }

    [Fact]
    public void OAuth_GetAccessToken_ReturnsNullWhenNotAuthenticated()
    {
        var oauth = new GoogleOAuthManager(_tempDir);
        var token = oauth.GetAccessTokenAsync().GetAwaiter().GetResult();
        Assert.Null(token);
    }

    [Fact]
    public void OAuth_RevokeAsync_ReturnsTrueWhenNotAuthenticated()
    {
        var oauth = new GoogleOAuthManager(_tempDir);
        var result = oauth.RevokeAsync().GetAwaiter().GetResult();
        Assert.True(result);
    }

    [Fact]
    public void OAuth_CorruptedTokenFile_DoesNotThrow()
    {
        File.WriteAllText(Path.Combine(_tempDir, "google-tokens.json"), "not json{{{");
        var oauth = new GoogleOAuthManager(_tempDir);
        Assert.False(oauth.IsAuthenticated);
    }

    [Fact]
    public void OAuth_Scopes_AreMinimal()
    {
        Assert.Equal(3, GoogleOAuthManager.Scopes.Length);
        Assert.Contains("gmail.readonly", GoogleOAuthManager.Scopes[0]);
        Assert.Contains("calendar.readonly", GoogleOAuthManager.Scopes[1]);
        Assert.Contains("drive.metadata.readonly", GoogleOAuthManager.Scopes[2]);
    }

    // ─── Gmail Provider ───

    [Fact]
    public void Gmail_NoAuth_ReturnsEmpty()
    {
        var oauth = new GoogleOAuthManager(_tempDir);
        var http = new HttpClient();
        var gmail = new GmailMetadataProvider(oauth, http);

        var result = gmail.GetRecentEmailsAsync().GetAwaiter().GetResult();
        Assert.Empty(result);
    }

    // ─── Calendar Provider ───

    [Fact]
    public void Calendar_NoAuth_ReturnsEmpty()
    {
        var oauth = new GoogleOAuthManager(_tempDir);
        var http = new HttpClient();
        var cal = new CalendarMetadataProvider(oauth, http);

        var result = cal.GetUpcomingEventsAsync().GetAwaiter().GetResult();
        Assert.Empty(result);
    }

    // ─── Drive Provider ───

    [Fact]
    public void Drive_NoAuth_ReturnsEmpty()
    {
        var oauth = new GoogleOAuthManager(_tempDir);
        var http = new HttpClient();
        var drive = new DriveMetadataProvider(oauth, http);

        var result = drive.GetRecentFilesAsync().GetAwaiter().GetResult();
        Assert.Empty(result);
    }

    // ─── Workspace Manager ───

    [Fact]
    public void Manager_Constructor_DoesNotThrow()
    {
        var manager = new GoogleWorkspaceManager(_tempDir);
        Assert.NotNull(manager.OAuth);
        Assert.NotNull(manager.Gmail);
        Assert.NotNull(manager.Calendar);
        Assert.NotNull(manager.Drive);
    }

    [Fact]
    public void Manager_SyncAll_NotAuthenticated_ReturnsError()
    {
        var manager = new GoogleWorkspaceManager(_tempDir);
        var result = manager.SyncAllAsync().GetAwaiter().GetResult();

        Assert.False(result.Success);
        Assert.Equal("Not authenticated with Google", result.Error);
    }

    [Fact]
    public void Manager_GetAuthorizationUrl_ReturnsValidUrl()
    {
        var url = GoogleWorkspaceManager.GetAuthorizationUrl("test-client-id", "http://localhost:5000/callback");

        Assert.Contains("accounts.google.com", url);
        Assert.Contains("test-client-id", url);
        Assert.Contains("gmail.readonly", url);
        Assert.Contains("calendar.readonly", url);
        Assert.Contains("drive.metadata.readonly", url);
        Assert.Contains("access_type=offline", url);
        Assert.Contains("prompt=consent", url);
    }

    // ─── Data Models ───

    [Fact]
    public void EmailMetadata_DefaultValues()
    {
        var email = new EmailMetadata();
        Assert.Equal(string.Empty, email.MessageId);
        Assert.Equal(string.Empty, email.From);
        Assert.Equal(string.Empty, email.Subject);
        Assert.Empty(email.Labels);
        Assert.False(email.HasAttachments);
    }

    [Fact]
    public void CalendarEventMetadata_DefaultValues()
    {
        var evt = new CalendarEventMetadata();
        Assert.Equal(string.Empty, evt.EventId);
        Assert.Equal(string.Empty, evt.Title);
        Assert.Empty(evt.Attendees);
        Assert.False(evt.IsAllDay);
    }

    [Fact]
    public void DriveFileMetadata_DefaultValues()
    {
        var file = new DriveFileMetadata();
        Assert.Equal(string.Empty, file.FileId);
        Assert.Equal(string.Empty, file.Name);
        Assert.Equal(0, file.SizeBytes);
    }

    [Fact]
    public void GwsSyncResult_DefaultValues()
    {
        var result = new GwsSyncResult();
        Assert.False(result.Success);
        Assert.Equal(0, result.EmailCount);
        Assert.Equal(0, result.EventCount);
        Assert.Equal(0, result.FileCount);
        Assert.Empty(result.Errors);
    }

    // ─── Token State ───

    [Fact]
    public void TokenState_NotExpired_WhenFutureDate()
    {
        var state = new GoogleTokenState
        {
            AccessToken = "test",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };
        Assert.False(state.IsExpired);
    }

    [Fact]
    public void TokenState_Expired_WhenPastDate()
    {
        var state = new GoogleTokenState
        {
            AccessToken = "test",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1)
        };
        Assert.True(state.IsExpired);
    }

    [Fact]
    public void TokenState_Expired_WhenNoExpiry()
    {
        var state = new GoogleTokenState { AccessToken = "test" };
        Assert.True(state.IsExpired); // default DateTimeOffset is.MinValue
    }

    // ─── Connection Status ───

    [Fact]
    public void ConnectionStatus_DefaultValues()
    {
        var status = new GoogleConnectionStatus();
        Assert.False(status.IsAuthenticated);
        Assert.Null(status.Email);
        Assert.Empty(status.Scopes);
        Assert.Null(status.ExpiresAt);
    }

    // ─── Dispose ───

    [Fact]
    public void Manager_Dispose_DoesNotThrow()
    {
        var manager = new GoogleWorkspaceManager(_tempDir);
        var ex = Record.Exception(() => manager.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void OAuth_Dispose_DoesNotThrow()
    {
        var oauth = new GoogleOAuthManager(_tempDir);
        var ex = Record.Exception(() => oauth.Dispose());
        Assert.Null(ex);
    }
}
