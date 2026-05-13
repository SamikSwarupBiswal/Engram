namespace Engram.Store.Cloud;

/// <summary>
/// Provider interface for cloud model inference.
/// Production: Gemini 3 Flash / Claude 4.5 Sonnet (managed credit pooling).
/// Dev: mock/fallback. Users NEVER provide API keys.
/// </summary>
public interface ICloudModelProvider
{
    /// <summary>Send a sanitized request to the cloud model.</summary>
    Task<CloudModelResponse> SendAsync(CloudModelRequest request, CancellationToken cancellationToken = default);

    /// <summary>Whether this provider is available (tier check, network, etc.).</summary>
    bool IsAvailable { get; }

    /// <summary>Provider name for audit logging.</summary>
    string ProviderName { get; }

    /// <summary>Specific model name for audit logging.</summary>
    string ModelName { get; }
}
