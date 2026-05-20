using Engram.Store.Billing;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Industry-level tests for the Token Budget system.
/// Tests real-world scenarios: concurrency, edge cases, budget exhaustion,
/// cycle rollover, tier changes, token packs, provider pricing.
/// </summary>
public class TokenBudgetTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configDir;

    public TokenBudgetTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram-budget-" + Guid.NewGuid().ToString("N")[..8]);
        _configDir = Path.Combine(_tempDir, "config");
        Directory.CreateDirectory(_configDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ─── Initial State ───

    [Fact]
    public void NewBudget_DefaultsToFreeTier()
    {
        var budget = new TokenBudget(_configDir);
        var status = budget.GetStatus();

        Assert.Equal("free", status.Tier);
        Assert.Equal(TokenBudget.FreeTierWeeklyTokens * 4, status.MonthlyAllowance);
        Assert.Equal(status.MonthlyAllowance, status.TokensRemaining);
        Assert.Equal(0, status.TokensUsedThisMonth);
    }

    [Fact]
    public void NewBudget_HasCorrectCycleDates()
    {
        var budget = new TokenBudget(_configDir);
        var status = budget.GetStatus();

        Assert.True(status.CycleStart <= DateTimeOffset.UtcNow);
        Assert.True(status.CycleEnd > DateTimeOffset.UtcNow);
        Assert.True(status.DaysRemaining >= 0);
        Assert.True(status.DaysRemaining <= 31);
    }

    [Fact]
    public void NewBudget_EmptyHistory()
    {
        var budget = new TokenBudget(_configDir);
        var status = budget.GetStatus();

        Assert.Empty(status.History);
        Assert.Empty(status.UsageByProvider);
    }

    // ─── Budget Checking ───

    [Fact]
    public void CheckBudget_AllowsWithinLimit()
    {
        var budget = new TokenBudget(_configDir);
        var result = budget.CheckBudget(1000);

        Assert.True(result.IsAllowed);
        Assert.Null(result.DenyReason);
        Assert.True(result.RemainingAfter > 0);
    }

    [Fact]
    public void CheckBudget_DeniesOverLimit()
    {
        var budget = new TokenBudget(_configDir);
        var status = budget.GetStatus();

        var result = budget.CheckBudget(status.TokensRemaining + 1);

        Assert.False(result.IsAllowed);
        Assert.NotNull(result.DenyReason);
        Assert.Contains("Insufficient tokens", result.DenyReason);
    }

    [Fact]
    public void CheckBudget_ExactlyAtLimit_Allows()
    {
        var budget = new TokenBudget(_configDir);
        var status = budget.GetStatus();

        var result = budget.CheckBudget(status.TokensRemaining);

        Assert.True(result.IsAllowed);
        Assert.Equal(0, result.RemainingAfter);
    }

    [Fact]
    public void CheckBudget_ZeroCost_Allows()
    {
        var budget = new TokenBudget(_configDir);
        var result = budget.CheckBudget(0);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void CheckBudget_NegativeCost_Allows()
    {
        var budget = new TokenBudget(_configDir);
        var result = budget.CheckBudget(-100);

        Assert.True(result.IsAllowed);
    }

    // ─── Usage Recording ───

    [Fact]
    public void RecordUsage_DeductsTokens()
    {
        var budget = new TokenBudget(_configDir);
        var before = budget.GetStatus().TokensRemaining;

        budget.RecordUsage(1000, "gemini-flash", 200, 100);

        var after = budget.GetStatus();
        Assert.Equal(before - 1000, after.TokensRemaining);
        Assert.Equal(1000, after.TokensUsedThisMonth);
    }

    [Fact]
    public void RecordUsage_TracksPerProvider()
    {
        var budget = new TokenBudget(_configDir);

        budget.RecordUsage(500, "gemini-flash", 100, 50);
        budget.RecordUsage(2000, "claude-sonnet", 100, 50);

        var status = budget.GetStatus();
        Assert.Equal(500, status.UsageByProvider["gemini-flash"]);
        Assert.Equal(2000, status.UsageByProvider["claude-sonnet"]);
    }

    [Fact]
    public void RecordUsage_AddsToHistory()
    {
        var budget = new TokenBudget(_configDir);

        budget.RecordUsage(500, "gemini-flash", 100, 50);

        var status = budget.GetStatus();
        Assert.Single(status.History);
        Assert.Equal("gemini-flash", status.History[0].Provider);
        Assert.Equal(100, status.History[0].InputTokens);
        Assert.Equal(50, status.History[0].OutputTokens);
        Assert.Equal(500, status.History[0].ProTokensCost);
    }

    [Fact]
    public void RecordUsage_ZeroCost_DoesNothing()
    {
        var budget = new TokenBudget(_configDir);
        var before = budget.GetStatus();

        budget.RecordUsage(0, "local", 100, 50);

        var after = budget.GetStatus();
        Assert.Equal(before.TokensRemaining, after.TokensRemaining);
        Assert.Equal(before.TokensUsedThisMonth, after.TokensUsedThisMonth);
        Assert.Empty(after.History);
    }

    [Fact]
    public void RecordUsage_NegativeCost_DoesNothing()
    {
        var budget = new TokenBudget(_configDir);
        var before = budget.GetStatus();

        budget.RecordUsage(-100, "test", 0, 0);

        var after = budget.GetStatus();
        Assert.Equal(before.TokensRemaining, after.TokensRemaining);
    }

    // ─── Budget Exhaustion ───

    [Fact]
    public void ExhaustBudget_SubsequentChecksDeny()
    {
        var budget = new TokenBudget(_configDir);
        var total = budget.GetStatus().TokensRemaining;

        // Use all tokens
        budget.RecordUsage(total, "gemini-flash", (int)(total / 2), (int)(total / 4));

        var check = budget.CheckBudget(1);
        Assert.False(check.IsAllowed);
    }

    [Fact]
    public void ExhaustBudget_ExactExhaustion_AllowsZero()
    {
        var budget = new TokenBudget(_configDir);
        var total = budget.GetStatus().TokensRemaining;

        budget.RecordUsage(total, "test", 0, 0);

        var check = budget.CheckBudget(0);
        Assert.True(check.IsAllowed);

        var check1 = budget.CheckBudget(1);
        Assert.False(check1.IsAllowed);
    }

    [Fact]
    public void Overdraft_UsageGoesNegative_ClampsToZero()
    {
        var budget = new TokenBudget(_configDir);
        var total = budget.GetStatus().TokensRemaining;

        // Try to use more than available
        budget.RecordUsage(total + 1000, "test", 0, 0);

        var status = budget.GetStatus();
        Assert.Equal(0, status.TokensRemaining); // Clamped to 0
        Assert.Equal(total + 1000, status.TokensUsedThisMonth); // Overdraft recorded
    }

    // ─── Token Packs ───

    [Fact]
    public void AddBonusTokens_IncreasesRemaining()
    {
        var budget = new TokenBudget(_configDir);
        var before = budget.GetStatus().TokensRemaining;

        budget.AddBonusTokens(100_000, "Small pack");

        var after = budget.GetStatus();
        Assert.Equal(before + 100_000, after.TokensRemaining);
        Assert.Equal(100_000, after.BonusTokens);
    }

    [Fact]
    public void AddBonusTokens_AddsCreditToHistory()
    {
        var budget = new TokenBudget(_configDir);

        budget.AddBonusTokens(100_000, "Test pack");

        var status = budget.GetStatus();
        Assert.Single(status.History);
        Assert.Equal("system", status.History[0].Provider);
        Assert.Equal(-100_000, status.History[0].ProTokensCost); // Negative = credit
    }

    [Fact]
    public void AddBonusTokens_ZeroAmount_Throws()
    {
        var budget = new TokenBudget(_configDir);
        Assert.Throws<ArgumentOutOfRangeException>(() => budget.AddBonusTokens(0));
    }

    [Fact]
    public void AddBonusTokens_NegativeAmount_Throws()
    {
        var budget = new TokenBudget(_configDir);
        Assert.Throws<ArgumentOutOfRangeException>(() => budget.AddBonusTokens(-100));
    }

    [Fact]
    public void BonusTokens_SurviveExhaustion()
    {
        var budget = new TokenBudget(_configDir);
        var monthly = budget.GetStatus().MonthlyAllowance;

        // Add bonus, then exhaust monthly
        budget.AddBonusTokens(50_000, "Pack");
        budget.RecordUsage(monthly, "test", 0, 0);

        // Bonus tokens should still be available
        var status = budget.GetStatus();
        Assert.Equal(50_000, status.TokensRemaining);
    }

    // ─── Tier Changes ───

    [Fact]
    public void SetTier_ToPro_IncreasesAllowance()
    {
        var budget = new TokenBudget(_configDir);
        var freeAllowance = budget.GetStatus().MonthlyAllowance;

        budget.SetTier("pro");

        var status = budget.GetStatus();
        Assert.Equal("pro", status.Tier);
        Assert.Equal(TokenBudget.ProTierMonthlyTokens, status.MonthlyAllowance);
        Assert.True(status.MonthlyAllowance > freeAllowance);
    }

    [Fact]
    public void SetTier_ToPro_ResetsUsage()
    {
        var budget = new TokenBudget(_configDir);
        budget.RecordUsage(1000, "test", 0, 0);

        budget.SetTier("pro");

        var status = budget.GetStatus();
        Assert.Equal(0, status.TokensUsedThisMonth);
    }

    [Fact]
    public void SetTier_PreservesBonusTokens()
    {
        var budget = new TokenBudget(_configDir);
        budget.AddBonusTokens(50_000, "Pack");

        budget.SetTier("pro");

        var status = budget.GetStatus();
        Assert.Equal(50_000, status.BonusTokens);
        Assert.Equal(TokenBudget.ProTierMonthlyTokens + 50_000, status.TokensRemaining);
    }

    [Fact]
    public void SetTier_CaseInsensitive()
    {
        var budget = new TokenBudget(_configDir);
        budget.SetTier("PRO");
        Assert.Equal("pro", budget.GetStatus().Tier);

        budget.SetTier("Pro");
        Assert.Equal("pro", budget.GetStatus().Tier);
    }

    // ─── Pricing ───

    [Fact]
    public void Pricing_GeminiFlash_IsCheap()
    {
        var cost = TokenPricing.CalculateCost("gemini-flash", 1000, 500);
        // 1000 * 1 + 500 * 3 = 2500
        Assert.Equal(2500, cost);
    }

    [Fact]
    public void Pricing_ClaudeSonnet_IsExpensive()
    {
        var cost = TokenPricing.CalculateCost("claude-sonnet", 1000, 500);
        // 1000 * 10 + 500 * 30 = 25000
        Assert.Equal(25000, cost);
    }

    [Fact]
    public void Pricing_Local_IsFree()
    {
        var cost = TokenPricing.CalculateCost("local", 10000, 5000);
        Assert.Equal(0, cost);
    }

    [Fact]
    public void Pricing_UnknownProvider_UsesDefaultRates()
    {
        var cost = TokenPricing.CalculateCost("unknown-provider", 1000, 500);
        // 1000 * 5 + 500 * 15 = 12500
        Assert.Equal(12500, cost);
    }

    [Fact]
    public void Pricing_CaseInsensitive()
    {
        var cost1 = TokenPricing.CalculateCost("GEMINI-FLASH", 1000, 500);
        var cost2 = TokenPricing.CalculateCost("gemini-flash", 1000, 500);
        Assert.Equal(cost1, cost2);
    }

    [Fact]
    public void Pricing_NullProvider_UsesDefault()
    {
        var cost = TokenPricing.CalculateCost(null!, 1000, 500);
        Assert.True(cost > 0);
    }

    [Fact]
    public void Pricing_CompareModels_GeminiIsCheaperThanClaude()
    {
        var geminiCost = TokenPricing.CalculateCost("gemini-flash", 1000, 500);
        var claudeCost = TokenPricing.CalculateCost("claude-sonnet", 1000, 500);

        Assert.True(geminiCost < claudeCost);
        Assert.Equal(10, claudeCost / geminiCost); // Claude is exactly 10x more
    }

    // ─── Persistence ───

    [Fact]
    public void Persistence_SurvivesRestart()
    {
        var budget1 = new TokenBudget(_configDir);
        budget1.SetTier("pro");
        budget1.RecordUsage(5000, "gemini-flash", 1000, 500);
        budget1.AddBonusTokens(100_000, "Pack");
        budget1.Dispose();

        var budget2 = new TokenBudget(_configDir);
        var status = budget2.GetStatus();

        Assert.Equal("pro", status.Tier);
        Assert.Equal(5000, status.TokensUsedThisMonth);
        Assert.Equal(100_000, status.BonusTokens);
        Assert.Equal(2, status.History.Count); // RecordUsage + AddBonusTokens
        Assert.Single(status.UsageByProvider);
    }

    [Fact]
    public void Persistence_CorruptedFile_CreatesDefault()
    {
        // Write garbage to budget file
        File.WriteAllText(Path.Combine(_configDir, "token-budget.json"), "not valid json{{{");

        var budget = new TokenBudget(_configDir);
        var status = budget.GetStatus();

        // Should create default free tier
        Assert.Equal("free", status.Tier);
        Assert.True(status.TokensRemaining > 0);
    }

    [Fact]
    public void Persistence_MissingFile_CreatesDefault()
    {
        var budget = new TokenBudget(_configDir);
        var status = budget.GetStatus();

        Assert.Equal("free", status.Tier);
        Assert.True(status.TokensRemaining > 0);
    }

    // ─── Concurrency ───

    [Fact]
    public async Task ConcurrentUsage_ThreadSafe()
    {
        var budget = new TokenBudget(_configDir);
        var tasks = new List<Task>();

        // 10 concurrent writers, each recording 100 tokens
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < 100; j++)
                {
                    budget.RecordUsage(10, "test", 2, 2);
                }
            }));
        }

        await Task.WhenAll(tasks);

        var status = budget.GetStatus();
        // 10 threads * 100 iterations * 10 tokens = 10,000
        Assert.Equal(10_000, status.TokensUsedThisMonth);
    }

    [Fact]
    public async Task ConcurrentCheckAndRecord_NoRaceCondition()
    {
        var budget = new TokenBudget(_configDir);
        var total = budget.GetStatus().TokensRemaining;
        var tasks = new List<Task>();
        var deniedCount = 0;

        // Concurrent checks and records using atomic TryReserve
        for (int i = 0; i < 20; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < 100; j++)
                {
                    var result = budget.TryReserve(100, "test", 25, 10);
                    if (result == null || !result.IsAllowed)
                    {
                        System.Threading.Interlocked.Increment(ref deniedCount);
                    }
                }
            }));
        }

        await Task.WhenAll(tasks);

        var finalStatus = budget.GetStatus();
        // Total used + remaining should equal initial total (no double-spend)
        Assert.Equal(total, finalStatus.TokensUsedThisMonth + finalStatus.TokensRemaining);
    }

    // ─── History ───

    [Fact]
    public void History_TracksAllUsage()
    {
        var budget = new TokenBudget(_configDir);

        budget.RecordUsage(100, "gemini", 20, 10);
        budget.RecordUsage(200, "claude", 20, 10);
        budget.AddBonusTokens(50000, "Pack");
        budget.RecordUsage(300, "gemini", 20, 10);

        var status = budget.GetStatus();
        Assert.Equal(4, status.History.Count);
    }

    [Fact]
    public void History_CapsAt1000_ThenTrims()
    {
        var budget = new TokenBudget(_configDir);

        // Add 1100 records
        for (int i = 0; i < 1100; i++)
        {
            budget.RecordUsage(1, "test", 0, 0);
        }

        var status = budget.GetStatus();
        Assert.True(status.History.Count <= 1000);
    }

    // ─── Usage Percent ───

    [Fact]
    public void UsagePercent_StartsAtZero()
    {
        var budget = new TokenBudget(_configDir);
        Assert.Equal(0, budget.GetStatus().UsagePercent);
    }

    [Fact]
    public void UsagePercent_IncreasesWithUsage()
    {
        var budget = new TokenBudget(_configDir);
        var allowance = budget.GetStatus().MonthlyAllowance;

        budget.RecordUsage(allowance / 2, "test", 0, 0);

        var percent = budget.GetStatus().UsagePercent;
        Assert.InRange(percent, 49, 51); // ~50%
    }

    [Fact]
    public void UsagePercent_CapsAt100()
    {
        var budget = new TokenBudget(_configDir);
        var allowance = budget.GetStatus().MonthlyAllowance;

        budget.RecordUsage(allowance * 2, "test", 0, 0); // Overdraft

        Assert.Equal(100, budget.GetStatus().UsagePercent);
    }

    // ─── Edge Cases ───

    [Fact]
    public void LargeUsage_DoesNotOverflow()
    {
        var budget = new TokenBudget(_configDir);

        // Use max long value / 2 to avoid overflow
        budget.RecordUsage(long.MaxValue / 2, "test", 0, 0);

        var status = budget.GetStatus();
        Assert.Equal(0, status.TokensRemaining); // Clamped
    }

    [Fact]
    public void MultipleProviders_UsageTracked()
    {
        var budget = new TokenBudget(_configDir);

        budget.RecordUsage(100, "gemini-flash", 20, 10);
        budget.RecordUsage(200, "claude-sonnet", 20, 10);
        budget.RecordUsage(300, "groq", 20, 10);
        budget.RecordUsage(400, "gemini-flash", 20, 10);

        var status = budget.GetStatus();
        Assert.Equal(3, status.UsageByProvider.Count);
        Assert.Equal(500, status.UsageByProvider["gemini-flash"]);
        Assert.Equal(200, status.UsageByProvider["claude-sonnet"]);
        Assert.Equal(300, status.UsageByProvider["groq"]);
    }

    [Fact]
    public void Dispose_SavesState()
    {
        var budget = new TokenBudget(_configDir);
        budget.RecordUsage(1000, "test", 0, 0);
        budget.Dispose();

        // Load from disk
        var budget2 = new TokenBudget(_configDir);
        Assert.Equal(1000, budget2.GetStatus().TokensUsedThisMonth);
    }

    [Fact]
    public void DoubleDispose_DoesNotThrow()
    {
        var budget = new TokenBudget(_configDir);
        budget.Dispose();
        var ex = Record.Exception(() => budget.Dispose());
        Assert.Null(ex);
    }

    // ─── Constants ───

    [Fact]
    public void Constants_AreReasonable()
    {
        Assert.True(TokenBudget.FreeTierWeeklyTokens > 0);
        Assert.True(TokenBudget.ProTierMonthlyTokens > TokenBudget.FreeTierWeeklyTokens * 4);
        Assert.True(TokenBudget.TokenPackSmall > 0);
        Assert.True(TokenBudget.TokenPackLarge > TokenBudget.TokenPackSmall);
    }

    // ─── Integration with Pricing ───

    [Fact]
    public void Integration_BudgetCheckThenRecord_Consistent()
    {
        var budget = new TokenBudget(_configDir);
        var before = budget.GetStatus().TokensRemaining;

        var cost = TokenPricing.CalculateCost("gemini-flash", 1000, 500);
        var check = budget.CheckBudget(cost);
        Assert.True(check.IsAllowed);

        budget.RecordUsage(cost, "gemini-flash", 1000, 500);

        var after = budget.GetStatus();
        Assert.Equal(before - cost, after.TokensRemaining);
    }

    [Fact]
    public void Integration_ClaudeCostsMoreThanGemini_ForSameInput()
    {
        var budget = new TokenBudget(_configDir);
        var allowance = budget.GetStatus().MonthlyAllowance;

        var geminiCost = TokenPricing.CalculateCost("gemini-flash", 1000, 500);
        var claudeCost = TokenPricing.CalculateCost("claude-sonnet", 1000, 500);

        // Claude should eat into budget 10x faster
        Assert.True(claudeCost > geminiCost);

        // Same input, Claude uses more budget
        var budget1 = new TokenBudget(Path.Combine(_tempDir, "test1"));
        budget1.RecordUsage(geminiCost, "gemini-flash", 1000, 500);
        var remaining1 = budget1.GetStatus().TokensRemaining;

        var budget2 = new TokenBudget(Path.Combine(_tempDir, "test2"));
        budget2.RecordUsage(claudeCost, "claude-sonnet", 1000, 500);
        var remaining2 = budget2.GetStatus().TokensRemaining;

        Assert.True(remaining1 > remaining2);
    }
}
