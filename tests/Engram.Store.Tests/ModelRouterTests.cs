using Engram.Store;
using Engram.Store.Cloud;

namespace Engram.Store.Tests;

/// <summary>
/// Test contracts for ModelRouter — derived from PRD Phase 8 requirements:
/// - Routine ingestion remains local by default (SC-1)
/// - Model routing selects correct tier (Quality Gate)
/// - Cloud features blocked for Free tier
/// </summary>
public class ModelRouterTests
{
    private static ModelRouter CreateRouter(TierLevel tier = TierLevel.Pro, bool cloudEnabled = true)
    {
        var config = new EngramConfig { Tier = tier, CloudEnabled = cloudEnabled };
        var guard = new TierGuard(config);
        return new ModelRouter(guard);
    }

    // --- Routing by complexity ---

    [Fact]
    public void Low_Complexity_Routes_To_Local()
    {
        var router = CreateRouter();
        var decision = router.Route(TaskComplexity.Low);

        Assert.False(decision.IsCloud);
        Assert.Equal(ComputeTarget.Local, decision.Target);
        Assert.Contains("local", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Medium_Complexity_Routes_To_GeminiFlash()
    {
        var router = CreateRouter();
        var decision = router.Route(TaskComplexity.Medium);

        Assert.True(decision.IsCloud);
        Assert.Equal(ComputeTarget.GeminiFlash, decision.Target);
        Assert.Contains("Gemini", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void High_Complexity_Routes_To_ClaudeSonnet()
    {
        var router = CreateRouter();
        var decision = router.Route(TaskComplexity.High);

        Assert.True(decision.IsCloud);
        Assert.Equal(ComputeTarget.ClaudeSonnet, decision.Target);
        Assert.Contains("Claude", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    // --- Free tier fallback ---

    [Fact]
    public void Free_Tier_Medium_Falls_Back_To_Local()
    {
        var router = CreateRouter(tier: TierLevel.Free);
        var decision = router.Route(TaskComplexity.Medium);

        Assert.False(decision.IsCloud);
        Assert.Equal(ComputeTarget.Local, decision.Target);
        Assert.Contains("Pro tier", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Free_Tier_High_Falls_Back_To_Local()
    {
        var router = CreateRouter(tier: TierLevel.Free);
        var decision = router.Route(TaskComplexity.High);

        Assert.False(decision.IsCloud);
        Assert.Equal(ComputeTarget.Local, decision.Target);
    }

    // --- Cloud disabled ---

    [Fact]
    public void Cloud_Disabled_Medium_Falls_Back_To_Local()
    {
        var router = CreateRouter(tier: TierLevel.Pro, cloudEnabled: false);
        var decision = router.Route(TaskComplexity.Medium);

        Assert.False(decision.IsCloud);
        Assert.Equal(ComputeTarget.Local, decision.Target);
        Assert.Contains("disabled", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cloud_Disabled_High_Falls_Back_To_Local()
    {
        var router = CreateRouter(tier: TierLevel.Pro, cloudEnabled: false);
        var decision = router.Route(TaskComplexity.High);

        Assert.False(decision.IsCloud);
        Assert.Equal(ComputeTarget.Local, decision.Target);
    }

    // --- Local always stays local regardless of tier ---

    [Fact]
    public void Low_Complexity_Always_Local_Even_With_Pro_Tier()
    {
        var router = CreateRouter(tier: TierLevel.Pro, cloudEnabled: true);
        var decision = router.Route(TaskComplexity.Low);

        Assert.False(decision.IsCloud);
        Assert.Equal(ComputeTarget.Local, decision.Target);
    }

    // --- Decision includes reason ---

    [Fact]
    public void Routing_Decision_Always_Includes_Reason()
    {
        var router = CreateRouter();

        var low = router.Route(TaskComplexity.Low);
        var med = router.Route(TaskComplexity.Medium);
        var high = router.Route(TaskComplexity.High);

        Assert.False(string.IsNullOrWhiteSpace(low.Reason));
        Assert.False(string.IsNullOrWhiteSpace(med.Reason));
        Assert.False(string.IsNullOrWhiteSpace(high.Reason));
    }

    // --- Constructor validation ---

    [Fact]
    public void Constructor_Null_Guard_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ModelRouter(null!));
    }
}
