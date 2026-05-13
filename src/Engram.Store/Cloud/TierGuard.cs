using Engram.Store;

namespace Engram.Store.Cloud;

/// <summary>
/// Gates cloud features behind tier status.
/// Free tier: all cloud calls blocked (returns not-available).
/// Pro tier: cloud calls allowed (subject to budget and policy).
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
    /// Returns a gate result with allowed status and reason.
    /// </summary>
    public TierGateResult CheckCloudAccess()
    {
        if (!_config.CloudEnabled)
            return TierGateResult.Blocked("Cloud features are disabled in configuration.");

        if (_config.Tier != TierLevel.Pro)
            return TierGateResult.Blocked("Cloud features require Pro tier. Current tier: " + _config.Tier);

        return TierGateResult.Allowed();
    }
}

public class TierGateResult
{
    public bool IsAllowed { get; init; }
    public string? BlockReason { get; init; }

    public static TierGateResult Allowed() => new() { IsAllowed = true };
    public static TierGateResult Blocked(string reason) => new() { IsAllowed = false, BlockReason = reason };
}
