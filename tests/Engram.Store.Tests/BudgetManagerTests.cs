using Engram.Store;
using Engram.Store.Cloud;

namespace Engram.Store.Tests;

/// <summary>
/// Test contracts for BudgetManager — derived from PRD Phase 8 requirements:
/// - Budget limit enforced, no runaway costs (Quality Gate)
/// - Per-user budget accounting
/// - Daily and monthly limits
/// </summary>
public class BudgetManagerTests : IDisposable
{
    private readonly string _tempDir;

    public BudgetManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"engram_budget_{Guid.NewGuid():n}");
        Directory.CreateDirectory(_tempDir);
    }

    // --- Within budget ---

    [Fact]
    public void Call_Within_Daily_Budget_Is_Allowed()
    {
        var config = new BudgetConfig { DailyLimitUsd = 1.00m, MonthlyLimitUsd = 25.00m, PerCallLimitUsd = 0.50m };
        using var auditLog = new CloudAuditLog(_tempDir);
        var manager = new BudgetManager(config, auditLog);

        var result = manager.CheckBudget(0.10m);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void Call_Within_Per_Call_Limit_Is_Allowed()
    {
        var config = new BudgetConfig { DailyLimitUsd = 10.00m, MonthlyLimitUsd = 100.00m, PerCallLimitUsd = 0.50m };
        using var auditLog = new CloudAuditLog(_tempDir);
        var manager = new BudgetManager(config, auditLog);

        var result = manager.CheckBudget(0.49m);

        Assert.True(result.IsAllowed);
    }

    // --- Per-call limit ---

    [Fact]
    public void Call_Exceeding_Per_Call_Limit_Is_Denied()
    {
        var config = new BudgetConfig { DailyLimitUsd = 10.00m, MonthlyLimitUsd = 100.00m, PerCallLimitUsd = 0.50m };
        using var auditLog = new CloudAuditLog(_tempDir);
        var manager = new BudgetManager(config, auditLog);

        var result = manager.CheckBudget(0.51m);

        Assert.False(result.IsAllowed);
        Assert.Contains("per-call", result.DenyReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Call_Exactly_At_Per_Call_Limit_Is_Allowed()
    {
        var config = new BudgetConfig { DailyLimitUsd = 10.00m, MonthlyLimitUsd = 100.00m, PerCallLimitUsd = 0.50m };
        using var auditLog = new CloudAuditLog(_tempDir);
        var manager = new BudgetManager(config, auditLog);

        var result = manager.CheckBudget(0.50m);

        Assert.True(result.IsAllowed);
    }

    // --- Daily limit ---

    [Fact]
    public void Call_Exceeding_Daily_Limit_Is_Denied()
    {
        var config = new BudgetConfig { DailyLimitUsd = 0.20m, MonthlyLimitUsd = 25.00m, PerCallLimitUsd = 1.00m };
        using var auditLog = new CloudAuditLog(_tempDir);

        // Log prior spending today
        auditLog.Log(CreateAuditEntry(cost: 0.15m));

        var manager = new BudgetManager(config, auditLog);
        var result = manager.CheckBudget(0.10m); // 0.15 + 0.10 > 0.20

        Assert.False(result.IsAllowed);
        Assert.Contains("Daily", result.DenyReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Daily_Spending_Accumulates()
    {
        var config = new BudgetConfig { DailyLimitUsd = 0.50m, MonthlyLimitUsd = 25.00m, PerCallLimitUsd = 1.00m };
        using var auditLog = new CloudAuditLog(_tempDir);

        auditLog.Log(CreateAuditEntry(cost: 0.20m));
        auditLog.Log(CreateAuditEntry(cost: 0.20m));

        var manager = new BudgetManager(config, auditLog);
        var result = manager.CheckBudget(0.15m); // 0.20 + 0.20 + 0.15 = 0.55 > 0.50

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void Daily_Spending_Reported_In_Allowed_Result()
    {
        var config = new BudgetConfig { DailyLimitUsd = 1.00m, MonthlyLimitUsd = 25.00m, PerCallLimitUsd = 1.00m };
        using var auditLog = new CloudAuditLog(_tempDir);

        auditLog.Log(CreateAuditEntry(cost: 0.30m));

        var manager = new BudgetManager(config, auditLog);
        var result = manager.CheckBudget(0.10m);

        Assert.True(result.IsAllowed);
        Assert.Equal(0.30m, result.DailySpentSoFar);
    }

    // --- Monthly limit ---

    [Fact]
    public void Call_Exceeding_Monthly_Limit_Is_Denied()
    {
        var config = new BudgetConfig { DailyLimitUsd = 10.00m, MonthlyLimitUsd = 0.50m, PerCallLimitUsd = 1.00m };
        using var auditLog = new CloudAuditLog(_tempDir);

        auditLog.Log(CreateAuditEntry(cost: 0.40m));

        var manager = new BudgetManager(config, auditLog);
        var result = manager.CheckBudget(0.20m); // 0.40 + 0.20 > 0.50

        Assert.False(result.IsAllowed);
        Assert.Contains("Monthly", result.DenyReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Monthly_Spending_Reported_In_Allowed_Result()
    {
        var config = new BudgetConfig { DailyLimitUsd = 10.00m, MonthlyLimitUsd = 25.00m, PerCallLimitUsd = 1.00m };
        using var auditLog = new CloudAuditLog(_tempDir);

        auditLog.Log(CreateAuditEntry(cost: 0.50m));

        var manager = new BudgetManager(config, auditLog);
        var result = manager.CheckBudget(0.10m);

        Assert.True(result.IsAllowed);
        Assert.Equal(0.50m, result.MonthlySpentSoFar);
    }

    // --- Per-call limit checked first ---

    [Fact]
    public void Per_Call_Limit_Checked_Before_Daily()
    {
        var config = new BudgetConfig { DailyLimitUsd = 100.00m, MonthlyLimitUsd = 100.00m, PerCallLimitUsd = 0.10m };
        using var auditLog = new CloudAuditLog(_tempDir);
        var manager = new BudgetManager(config, auditLog);

        var result = manager.CheckBudget(0.50m); // Exceeds per-call but not daily

        Assert.False(result.IsAllowed);
        Assert.Contains("per-call", result.DenyReason, StringComparison.OrdinalIgnoreCase);
    }

    // --- FromConfig ---

    [Fact]
    public void FromConfig_Copies_Budget_Settings()
    {
        var config = new EngramConfig
        {
            DailyBudgetUsd = 2.00m,
            MonthlyBudgetUsd = 50.00m,
            PerCallLimitUsd = 1.00m
        };

        var budget = BudgetConfig.FromConfig(config);

        Assert.Equal(2.00m, budget.DailyLimitUsd);
        Assert.Equal(50.00m, budget.MonthlyLimitUsd);
        Assert.Equal(1.00m, budget.PerCallLimitUsd);
    }

    // --- Constructor validation ---

    [Fact]
    public void Constructor_Null_Config_Throws()
    {
        using var auditLog = new CloudAuditLog(_tempDir);
        Assert.Throws<ArgumentNullException>(() => new BudgetManager(null!, auditLog));
    }

    [Fact]
    public void Constructor_Null_AuditLog_Throws()
    {
        var config = new BudgetConfig();
        Assert.Throws<ArgumentNullException>(() => new BudgetManager(config, null!));
    }

    // --- Helpers ---

    private static CloudAuditEntry CreateAuditEntry(decimal cost) => new()
    {
        Reason = "test",
        Provider = "gemini-flash",
        Model = "gemini-3-flash",
        CostUsd = cost,
        Success = true,
        Timestamp = DateTimeOffset.UtcNow
    };

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
