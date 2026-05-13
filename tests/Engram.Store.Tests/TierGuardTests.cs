using Engram.Store;
using Engram.Store.Cloud;

namespace Engram.Store.Tests;

/// <summary>
/// Test contracts for TierGuard — derived from PRD Phase 8 requirements:
/// - Free tier: cloud features blocked (Tier Architecture doc)
/// - Pro tier: cloud features allowed (subject to budget/policy)
/// - Cloud-enabled config required
/// </summary>
public class TierGuardTests
{
    // --- Pro tier access ---

    [Fact]
    public void Pro_Tier_CloudEnabled_Allows_Access()
    {
        var config = new EngramConfig { Tier = TierLevel.Pro, CloudEnabled = true };
        var guard = new TierGuard(config);

        var result = guard.CheckCloudAccess();

        Assert.True(result.IsAllowed);
        Assert.Null(result.BlockReason);
    }

    // --- Free tier blocks ---

    [Fact]
    public void Free_Tier_Blocks_Cloud_Access()
    {
        var config = new EngramConfig { Tier = TierLevel.Free, CloudEnabled = true };
        var guard = new TierGuard(config);

        var result = guard.CheckCloudAccess();

        Assert.False(result.IsAllowed);
        Assert.NotNull(result.BlockReason);
        Assert.Contains("Pro tier", result.BlockReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Free_Tier_BlockReason_Includes_Current_Tier()
    {
        var config = new EngramConfig { Tier = TierLevel.Free, CloudEnabled = true };
        var guard = new TierGuard(config);

        var result = guard.CheckCloudAccess();

        Assert.Contains("Free", result.BlockReason);
    }

    // --- Cloud disabled ---

    [Fact]
    public void Pro_Tier_CloudDisabled_Blocks_Access()
    {
        var config = new EngramConfig { Tier = TierLevel.Pro, CloudEnabled = false };
        var guard = new TierGuard(config);

        var result = guard.CheckCloudAccess();

        Assert.False(result.IsAllowed);
        Assert.NotNull(result.BlockReason);
        Assert.Contains("disabled", result.BlockReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Free_Tier_CloudDisabled_Blocks_Access()
    {
        var config = new EngramConfig { Tier = TierLevel.Free, CloudEnabled = false };
        var guard = new TierGuard(config);

        var result = guard.CheckCloudAccess();

        Assert.False(result.IsAllowed);
    }

    // --- Block reason prioritization ---

    [Fact]
    public void Cloud_Disabled_Message_Shown_Before_Tier_Check()
    {
        // When cloud is disabled, the reason should be about config, not tier
        var config = new EngramConfig { Tier = TierLevel.Free, CloudEnabled = false };
        var guard = new TierGuard(config);

        var result = guard.CheckCloudAccess();

        Assert.Contains("disabled", result.BlockReason, StringComparison.OrdinalIgnoreCase);
    }

    // --- Default config ---

    [Fact]
    public void Default_Config_Is_Free_Tier_With_Cloud_Disabled()
    {
        var config = new EngramConfig();
        var guard = new TierGuard(config);

        var result = guard.CheckCloudAccess();

        Assert.False(result.IsAllowed);
        Assert.Equal(TierLevel.Free, config.Tier);
        Assert.False(config.CloudEnabled);
    }

    // --- TierGateResult factory methods ---

    [Fact]
    public void Allowed_Result_Has_No_Reason()
    {
        var result = TierGateResult.Allowed();

        Assert.True(result.IsAllowed);
        Assert.Null(result.BlockReason);
    }

    [Fact]
    public void Blocked_Result_Has_Reason()
    {
        var result = TierGateResult.Blocked("test reason");

        Assert.False(result.IsAllowed);
        Assert.Equal("test reason", result.BlockReason);
    }

    // --- Constructor validation ---

    [Fact]
    public void Constructor_Null_Config_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new TierGuard(null!));
    }
}
