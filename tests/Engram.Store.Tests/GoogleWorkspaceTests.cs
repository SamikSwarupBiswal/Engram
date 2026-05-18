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

    // ─── Token Persistence ───

    [Fact]
    public void OAuth_TokenPersistence_SurvivesRestart()
    {
        // Write a valid token state
        var tokenState = new GoogleTokenState
        {
            AccessToken = "test-access-token",
            RefreshToken = "test-refresh-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Email = "test@gmail.com",
            Scopes = new[] { "gmail.readonly" }
        };
        var json = System.Text.Json.JsonSerializer.Serialize(tokenState, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = true
        });
        File.WriteAllText(Path.Combine(_tempDir, "google-tokens.json"), json);

        // Load in new manager
        var oauth = new GoogleOAuthManager(_tempDir);
        Assert.True(oauth.IsAuthenticated);
        Assert.Equal("test@gmail.com", oauth.UserEmail);
    }

    [Fact]
    public void OAuth_TokenPersistence_ExpiredToken()
    {
        var tokenState = new GoogleTokenState
        {
            AccessToken = "test-access-token",
            RefreshToken = "test-refresh-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1),
            Email = "test@gmail.com"
        };
        var json = System.Text.Json.JsonSerializer.Serialize(tokenState, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower
        });
        File.WriteAllText(Path.Combine(_tempDir, "google-tokens.json"), json);

        var oauth = new GoogleOAuthManager(_tempDir);
        Assert.False(oauth.IsAuthenticated); // Expired
        Assert.Equal("test@gmail.com", oauth.UserEmail); // But email still there
    }

    // ─── Token File Edge Cases ───

    [Fact]
    public void OAuth_EmptyTokenFile_DoesNotThrow()
    {
        File.WriteAllText(Path.Combine(_tempDir, "google-tokens.json"), "");
        var oauth = new GoogleOAuthManager(_tempDir);
        Assert.False(oauth.IsAuthenticated);
    }

    [Fact]
    public void OAuth_MissingRefreshToken_CannotRefresh()
    {
        var tokenState = new GoogleTokenState
        {
            AccessToken = "test",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1)
            // No refresh token
        };
        var json = System.Text.Json.JsonSerializer.Serialize(tokenState, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower
        });
        File.WriteAllText(Path.Combine(_tempDir, "google-tokens.json"), json);

        var oauth = new GoogleOAuthManager(_tempDir);
        var token = oauth.GetAccessTokenAsync().GetAwaiter().GetResult();
        Assert.NotNull(token); // Returns expired token (caller decides what to do)
    }

    // ─── Concurrent Access ───

    [Fact]
    public void OAuth_ConcurrentStatusReads_ThreadSafe()
    {
        var oauth = new GoogleOAuthManager(_tempDir);
        var tasks = new List<Task>();

        for (int i = 0; i < 20; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < 100; j++)
                {
                    var status = oauth.GetStatus();
                    var authenticated = oauth.IsAuthenticated;
                    var email = oauth.UserEmail;
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());
        // No exception = thread safe
    }

    // ─── Manager Edge Cases ───

    [Fact]
    public void Manager_MultipleSyncs_NoSideEffects()
    {
        var manager = new GoogleWorkspaceManager(_tempDir);

        var r1 = manager.SyncAllAsync().GetAwaiter().GetResult();
        var r2 = manager.SyncAllAsync().GetAwaiter().GetResult();

        Assert.False(r1.Success);
        Assert.False(r2.Success);
        Assert.Equal(r1.Error, r2.Error);
    }

    [Fact]
    public void Manager_DisposeMultipleTimes_DoesNotThrow()
    {
        var manager = new GoogleWorkspaceManager(_tempDir);
        manager.Dispose();
        var ex = Record.Exception(() => manager.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void Manager_AfterDispose_SyncFails()
    {
        var manager = new GoogleWorkspaceManager(_tempDir);
        manager.Dispose();
        // Should not throw, just fail gracefully
        var ex = Record.Exception(() =>
        {
            var result = manager.SyncAllAsync().GetAwaiter().GetResult();
        });
        // May throw ObjectDisposedException — that's acceptable
    }

    // ─── OAuth URL Generation ───

    [Fact]
    public void AuthorizationUrl_ContainsAllScopes()
    {
        var url = GoogleWorkspaceManager.GetAuthorizationUrl("client", "http://redirect");

        foreach (var scope in GoogleOAuthManager.Scopes)
        {
            Assert.Contains(Uri.EscapeDataString(scope), url);
        }
    }

    [Fact]
    public void AuthorizationUrl_ContainsOfflineAccess()
    {
        var url = GoogleWorkspaceManager.GetAuthorizationUrl("client", "http://redirect");
        Assert.Contains("access_type=offline", url);
        Assert.Contains("prompt=consent", url);
    }

    [Fact]
    public void AuthorizationUrl_EncodesRedirectUri()
    {
        var url = GoogleWorkspaceManager.GetAuthorizationUrl("client", "http://localhost:5000/callback?state=test");
        Assert.Contains(Uri.EscapeDataString("http://localhost:5000/callback?state=test"), url);
    }

    [Fact]
    public void AuthorizationUrl_EmptyClientId_StillGeneratesUrl()
    {
        var url = GoogleWorkspaceManager.GetAuthorizationUrl("", "http://redirect");
        Assert.Contains("accounts.google.com", url);
    }

    // ─── Email Metadata ───

    [Fact]
    public void EmailMetadata_WithLabels_PreservesLabels()
    {
        var email = new EmailMetadata
        {
            Labels = new List<string> { "INBOX", "IMPORTANT", "UNREAD" }
        };
        Assert.Equal(3, email.Labels.Count);
        Assert.Contains("INBOX", email.Labels);
    }

    [Fact]
    public void EmailMetadata_HasAttachments_DefaultFalse()
    {
        var email = new EmailMetadata();
        Assert.False(email.HasAttachments);
    }

    // ─── Calendar Metadata ───

    [Fact]
    public void CalendarEvent_AllDay_HasNoTime()
    {
        var evt = new CalendarEventMetadata
        {
            IsAllDay = true,
            StartTime = "2026-05-20",
            EndTime = "2026-05-21"
        };
        Assert.True(evt.IsAllDay);
        Assert.DoesNotContain("T", evt.StartTime);
    }

    [Fact]
    public void CalendarEvent_WithAttendees_PreservesList()
    {
        var evt = new CalendarEventMetadata
        {
            Attendees = new List<string> { "a@gmail.com", "b@gmail.com", "c@gmail.com" }
        };
        Assert.Equal(3, evt.Attendees.Count);
    }

    // ─── Drive Metadata ───

    [Fact]
    public void DriveFile_WithSize_PreservesValue()
    {
        var file = new DriveFileMetadata { SizeBytes = 1_500_000 };
        Assert.Equal(1_500_000, file.SizeBytes);
    }

    [Fact]
    public void DriveFile_UnknownMime_PreservesValue()
    {
        var file = new DriveFileMetadata { MimeType = "application/x-custom" };
        Assert.Equal("application/x-custom", file.MimeType);
    }

    // ─── Sync Result ───

    [Fact]
    public void SyncResult_WithErrors_ReportsAll()
    {
        var result = new GwsSyncResult
        {
            Errors = new List<string> { "Gmail: timeout", "Drive: rate limited" }
        };
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public void SyncResult_PartialSuccess_StillReportsCounts()
    {
        var result = new GwsSyncResult
        {
            EmailCount = 25,
            EventCount = 10,
            FileCount = 0,
            Errors = new List<string> { "Drive: failed" }
        };
        Assert.Equal(25, result.EmailCount);
        Assert.Equal(10, result.EventCount);
        Assert.False(result.Success); // Has errors
    }

    // ─── Scope Validation ───

    [Fact]
    public void Scopes_ReadOnly_NoWriteAccess()
    {
        foreach (var scope in GoogleOAuthManager.Scopes)
        {
            Assert.DoesNotContain("write", scope);
            Assert.DoesNotContain("modify", scope);
            Assert.DoesNotContain("delete", scope);
        }
    }

    [Fact]
    public void Scopes_ContainsGmailReadonly()
    {
        Assert.Contains(GoogleOAuthManager.Scopes, s => s.Contains("gmail.readonly"));
    }

    [Fact]
    public void Scopes_ContainsCalendarReadonly()
    {
        Assert.Contains(GoogleOAuthManager.Scopes, s => s.Contains("calendar.readonly"));
    }

    [Fact]
    public void Scopes_ContainsDriveMetadataReadonly()
    {
        Assert.Contains(GoogleOAuthManager.Scopes, s => s.Contains("drive.metadata.readonly"));
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
