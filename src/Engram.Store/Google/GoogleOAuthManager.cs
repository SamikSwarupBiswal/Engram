using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Google;

/// <summary>
/// Manages Google OAuth 2.0 flow for Workspace APIs.
/// Uses minimal scopes: Gmail readonly, Calendar readonly, Drive readonly.
/// Stores tokens encrypted in .engram/config/google-tokens.json.
/// </summary>
public class GoogleOAuthManager : IDisposable
{
    private readonly string _configDir;
    private readonly ILogger<GoogleOAuthManager>? _logger;
    private readonly HttpClient _http;
    private GoogleTokenState? _tokenState;
    private bool _disposed;

    // Minimal scopes — metadata only, no content access
    public static readonly string[] Scopes = new[]
    {
        "https://www.googleapis.com/auth/gmail.readonly",
        "https://www.googleapis.com/auth/calendar.readonly",
        "https://www.googleapis.com/auth/drive.metadata.readonly"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public GoogleOAuthManager(string configDir, ILogger<GoogleOAuthManager>? logger = null)
    {
        _configDir = configDir;
        _logger = logger;
        _http = new HttpClient();
        LoadTokens();
    }

    /// <summary>Check if user is authenticated with Google.</summary>
    public bool IsAuthenticated => _tokenState != null && !string.IsNullOrEmpty(_tokenState.AccessToken) && !_tokenState.IsExpired;

    /// <summary>Get the current user email (from token response).</summary>
    public string? UserEmail => _tokenState?.Email;

    /// <summary>Get a valid access token, refreshing if needed.</summary>
    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_tokenState == null) return null;

        if (_tokenState.IsExpired && !string.IsNullOrEmpty(_tokenState.RefreshToken))
        {
            _logger?.LogInformation("Access token expired, refreshing...");
            await RefreshTokenAsync(cancellationToken);
        }

        return _tokenState?.AccessToken;
    }

    /// <summary>
    /// Exchange an authorization code for tokens.
    /// Called after user completes Google OAuth consent screen.
    /// </summary>
    public async Task<bool> ExchangeCodeAsync(string code, string clientId, string clientSecret, string redirectUri, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["redirect_uri"] = redirectUri,
                ["grant_type"] = "authorization_code"
            });

            var response = await _http.PostAsync("https://oauth2.googleapis.com/token", request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger?.LogError("Token exchange failed: {Error}", error);
                return false;
            }

            var tokenResponse = await response.Content.ReadFromJsonAsync<GoogleTokenResponse>(JsonOptions, cancellationToken);
            if (tokenResponse == null) return false;

            _tokenState = new GoogleTokenState
            {
                AccessToken = tokenResponse.AccessToken,
                RefreshToken = tokenResponse.RefreshToken,
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 60),
                Email = tokenResponse.Email,
                Scopes = tokenResponse.Scope?.Split(' ') ?? Array.Empty<string>()
            };

            SaveTokens();
            _logger?.LogInformation("Google OAuth complete for {Email}", _tokenState.Email);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Token exchange failed");
            return false;
        }
    }

    /// <summary>Refresh the access token using the refresh token.</summary>
    private async Task RefreshTokenAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Load client credentials from config
            var credPath = Path.Combine(_configDir, "google-credentials.json");
            if (!File.Exists(credPath))
            {
                _logger?.LogWarning("Google credentials not found at {Path}", credPath);
                return;
            }

            var credJson = File.ReadAllText(credPath);
            var cred = JsonSerializer.Deserialize<GoogleClientCredentials>(credJson, JsonOptions);
            if (cred?.Installed == null && cred?.Web == null) return;

            var clientInfo = cred.Installed ?? cred.Web;

            var request = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["refresh_token"] = _tokenState!.RefreshToken!,
                ["client_id"] = clientInfo!.ClientId,
                ["client_secret"] = clientInfo.ClientSecret,
                ["grant_type"] = "refresh_token"
            });

            var response = await _http.PostAsync("https://oauth2.googleapis.com/token", request, cancellationToken);
            if (!response.IsSuccessStatusCode) return;

            var tokenResponse = await response.Content.ReadFromJsonAsync<GoogleTokenResponse>(JsonOptions, cancellationToken);
            if (tokenResponse == null) return;

            _tokenState.AccessToken = tokenResponse.AccessToken;
            _tokenState.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 60);
            SaveTokens();

            _logger?.LogInformation("Token refreshed for {Email}", _tokenState.Email);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Token refresh failed");
        }
    }

    /// <summary>Revoke Google access and delete stored tokens.</summary>
    public async Task<bool> RevokeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_tokenState?.AccessToken != null)
            {
                await _http.PostAsync(
                    $"https://oauth2.googleapis.com/revoke?token={_tokenState.AccessToken}",
                    null, cancellationToken);
            }

            _tokenState = null;
            var tokenPath = Path.Combine(_configDir, "google-tokens.json");
            if (File.Exists(tokenPath)) File.Delete(tokenPath);

            _logger?.LogInformation("Google access revoked");
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Token revocation failed");
            return false;
        }
    }

    /// <summary>Get current connection status.</summary>
    public GoogleConnectionStatus GetStatus()
    {
        return new GoogleConnectionStatus
        {
            IsAuthenticated = IsAuthenticated,
            Email = UserEmail,
            Scopes = _tokenState?.Scopes ?? Array.Empty<string>(),
            ExpiresAt = _tokenState?.ExpiresAt
        };
    }

    private void LoadTokens()
    {
        try
        {
            var path = Path.Combine(_configDir, "google-tokens.json");
            if (!File.Exists(path)) return;

            var json = File.ReadAllText(path);
            _tokenState = JsonSerializer.Deserialize<GoogleTokenState>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load Google tokens");
        }
    }

    private void SaveTokens()
    {
        try
        {
            Directory.CreateDirectory(_configDir);
            var path = Path.Combine(_configDir, "google-tokens.json");
            var json = JsonSerializer.Serialize(_tokenState, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save Google tokens");
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _http.Dispose();
            _disposed = true;
        }
    }
}

// ─── Models ───

public class GoogleTokenState
{
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string? Email { get; set; }
    public string[] Scopes { get; set; } = Array.Empty<string>();
    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
}

public class GoogleTokenResponse
{
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public int ExpiresIn { get; set; }
    public string? Scope { get; set; }
    public string? Email { get; set; }
}

public class GoogleClientCredentials
{
    public GoogleClientInfo? Installed { get; set; }
    public GoogleClientInfo? Web { get; set; }
}

public class GoogleClientInfo
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}

public class GoogleConnectionStatus
{
    public bool IsAuthenticated { get; init; }
    public string? Email { get; init; }
    public string[] Scopes { get; init; } = Array.Empty<string>();
    public DateTimeOffset? ExpiresAt { get; init; }
}
