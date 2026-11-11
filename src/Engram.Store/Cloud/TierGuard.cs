using Engram.Store;

namespace Engram.Store.Cloud;

/// <summary>
/// Gates cloud features behind tier status.
/// Free tier: cloud calls blocked (except localhost/local APIs).
/// Pro tier: all cloud calls allowed (subject to budget and policy).
///
/// Localhost APIs (Ollama, LM Studio, vLLM) are ALWAYS free — they run
/// on the user's machine and should never be gated by tier.
/// </summary>
public class TierGuard
{
    private readonly EngramConfig _config;

    public TierGuard(EngramConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// Check if cloud calls are allowed for this workspace.
    /// Localhost/local APIs are always allowed regardless of tier.
    /// </summary>
    public TierGateResult CheckCloudAccess()
    {
        // Localhost APIs are always free — they run on the user's machine
        if (IsLocalProvider())
            return TierGateResult.Allowed();

        if (_config.DevelopmentCloudEnabled)
            return TierGateResult.Allowed();

        if (!_config.CloudEnabled)
            return TierGateResult.Blocked("Cloud features are disabled in configuration.");

        if (_config.Tier != TierLevel.Pro)
            return TierGateResult.Blocked("Cloud features require Pro tier. Current tier: " + _config.Tier);

        return TierGateResult.Allowed();
    }

    /// <summary>
    /// Check if the configured provider is a localhost/local API.
    /// Localhost APIs (Ollama, LM Studio, vLLM) run on the user's machine.
    /// </summary>
    private bool IsLocalProvider()
    {
        var baseUrl = _config.CustomProviderBaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
            return false;

        return Uri.TryCreate(baseUrl, UriKind.Absolute, out var endpoint) && IsLoopbackEndpoint(endpoint);
    }

    public static bool IsLoopbackEndpoint(Uri endpoint) =>
        endpoint.IsAbsoluteUri && endpoint.Scheme is "http" or "https" &&
        string.IsNullOrEmpty(endpoint.UserInfo) &&
        (endpoint.IdnHost.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
         System.Net.IPAddress.TryParse(endpoint.IdnHost.Trim('[', ']'), out var address) &&
         System.Net.IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address));
}

public class TierGateResult
{
    public bool IsAllowed { get; init; }
    public string? BlockReason { get; init; }

    public static TierGateResult Allowed() => new() { IsAllowed = true };
    public static TierGateResult Blocked(string reason) => new() { IsAllowed = false, BlockReason = reason };
}
