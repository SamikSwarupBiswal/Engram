using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Google;

/// <summary>
/// Fetches Google Drive METADATA only — file name, type, modified date, size.
/// Uses drive.metadata.readonly scope. NEVER downloads file content.
/// </summary>
public class DriveMetadataProvider
{
    private readonly GoogleOAuthManager _oauth;
    private readonly HttpClient _http;
    private readonly ILogger<DriveMetadataProvider>? _logger;

    public DriveMetadataProvider(GoogleOAuthManager oauth, HttpClient http, ILogger<DriveMetadataProvider>? logger = null)
    {
        _oauth = oauth;
        _http = http;
        _logger = logger;
    }

    /// <summary>
    /// Fetch recent Drive file metadata.
    /// Returns file name, MIME type, modified date, size — NO file content.
    /// </summary>
    public async Task<List<DriveFileMetadata>> GetRecentFilesAsync(int maxResults = 50, CancellationToken cancellationToken = default)
    {
        var token = await _oauth.GetAccessTokenAsync(cancellationToken);
        if (token == null) return new List<DriveFileMetadata>();

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            var url = $"https://www.googleapis.com/drive/v3/files?pageSize={maxResults}&orderBy=modifiedTime%20desc&fields=files(id,name,mimeType,modifiedTime,size,owners,webViewLink)";
            var response = await _http.GetFromJsonAsync<DriveFileList>(url, cancellationToken);
            if (response?.Files == null) return new List<DriveFileMetadata>();

            var files = response.Files.Select(f => new DriveFileMetadata
            {
                FileId = f.Id ?? "",
                Name = f.Name ?? "",
                MimeType = f.MimeType ?? "",
                ModifiedTime = f.ModifiedTime ?? "",
                SizeBytes = long.TryParse(f.Size, out var s) ? s : 0,
                Owner = f.Owners?.FirstOrDefault()?.DisplayName ?? "",
                WebViewLink = f.WebViewLink ?? ""
            }).ToList();

            _logger?.LogInformation("Fetched {Count} Drive file metadata entries", files.Count);
            return files;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to fetch Drive metadata");
            return new List<DriveFileMetadata>();
        }
    }
}

public class DriveFileMetadata
{
    public string FileId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string MimeType { get; init; } = string.Empty;
    public string ModifiedTime { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string Owner { get; init; } = string.Empty;
    public string WebViewLink { get; init; } = string.Empty;
}

// Drive API response models
public class DriveFileList
{
    public List<DriveFile>? Files { get; set; }
}

public class DriveFile
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? MimeType { get; set; }
    public string? ModifiedTime { get; set; }
    public string? Size { get; set; }
    public List<DriveOwner>? Owners { get; set; }
    public string? WebViewLink { get; set; }
}

public class DriveOwner
{
    public string? DisplayName { get; set; }
    public string? EmailAddress { get; set; }
}
