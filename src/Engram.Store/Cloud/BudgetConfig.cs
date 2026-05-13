namespace Engram.Store.Cloud;

/// <summary>
/// Budget configuration for cloud spending limits.
/// </summary>
public class BudgetConfig
{
    /// <summary>Maximum daily spend in USD.</summary>
    public decimal DailyLimitUsd { get; init; } = 1.00m;

    /// <summary>Maximum monthly spend in USD.</summary>
    public decimal MonthlyLimitUsd { get; init; } = 25.00m;

    /// <summary>Maximum cost per single call in USD.</summary>
    public decimal PerCallLimitUsd { get; init; } = 0.50m;

    /// <summary>Create from EngramConfig.</summary>
    public static BudgetConfig FromConfig(EngramConfig config) => new()
    {
        DailyLimitUsd = config.DailyBudgetUsd,
        MonthlyLimitUsd = config.MonthlyBudgetUsd,
        PerCallLimitUsd = config.PerCallLimitUsd
    };
}
