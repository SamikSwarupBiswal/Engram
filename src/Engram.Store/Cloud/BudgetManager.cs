namespace Engram.Store.Cloud;

/// <summary>
/// Enforces per-user cloud spending budgets.
/// Checks daily limits, monthly limits, and per-call limits.
/// </summary>
public class BudgetManager
{
    private readonly BudgetConfig _config;
    private readonly CloudAuditLog _auditLog;

    public BudgetManager(BudgetConfig config, CloudAuditLog auditLog)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _auditLog = auditLog ?? throw new ArgumentNullException(nameof(auditLog));
    }

    /// <summary>
    /// Check if a cloud call with the given estimated cost is within budget.
    /// </summary>
    public BudgetCheckResult CheckBudget(decimal estimatedCostUsd)
    {
        // Per-call limit
        if (estimatedCostUsd > _config.PerCallLimitUsd)
            return BudgetCheckResult.Denied($"Estimated cost ${estimatedCostUsd:F4} exceeds per-call limit ${_config.PerCallLimitUsd:F2}.");

        // Daily limit
        var dailySpent = GetDailySpent();
        if (dailySpent + estimatedCostUsd > _config.DailyLimitUsd)
            return BudgetCheckResult.Denied($"Daily budget exhausted. Spent: ${dailySpent:F4}, Limit: ${_config.DailyLimitUsd:F2}, Requested: ${estimatedCostUsd:F4}.");

        // Monthly limit
        var monthlySpent = GetMonthlySpent();
        if (monthlySpent + estimatedCostUsd > _config.MonthlyLimitUsd)
            return BudgetCheckResult.Denied($"Monthly budget exhausted. Spent: ${monthlySpent:F4}, Limit: ${_config.MonthlyLimitUsd:F2}, Requested: ${estimatedCostUsd:F4}.");

        return BudgetCheckResult.Allowed(dailySpent, monthlySpent);
    }

    private decimal GetDailySpent()
    {
        var today = DateTimeOffset.UtcNow.Date;
        var tomorrow = today.AddDays(1);
        return _auditLog.GetEntriesInRange(
            new DateTimeOffset(today, TimeSpan.Zero),
            new DateTimeOffset(tomorrow, TimeSpan.Zero)
        ).Sum(e => e.CostUsd);
    }

    private decimal GetMonthlySpent()
    {
        var now = DateTimeOffset.UtcNow;
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var monthEnd = monthStart.AddMonths(1);
        return _auditLog.GetEntriesInRange(monthStart, monthEnd).Sum(e => e.CostUsd);
    }
}

public class BudgetCheckResult
{
    public bool IsAllowed { get; init; }
    public string? DenyReason { get; init; }
    public decimal DailySpentSoFar { get; init; }
    public decimal MonthlySpentSoFar { get; init; }

    public static BudgetCheckResult Allowed(decimal dailySpent, decimal monthlySpent) => new()
    {
        IsAllowed = true,
        DailySpentSoFar = dailySpent,
        MonthlySpentSoFar = monthlySpent
    };

    public static BudgetCheckResult Denied(string reason) => new()
    {
        IsAllowed = false,
        DenyReason = reason
    };
}
