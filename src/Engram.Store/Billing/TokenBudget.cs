using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Billing;

/// <summary>
/// Manages monthly Pro token budgets for users.
/// Thread-safe. Persisted to .engram/config/token-budget.json.
///
/// Budget model:
///   - Each user gets a monthly token allowance (default 500,000)
///   - Different models cost different amounts of tokens
///   - Tokens reset on the 1st of each month
///   - Token packs can be purchased for additional tokens
///   - Usage is tracked per-provider for transparency
/// </summary>
public class TokenBudget : IDisposable
{
    private readonly string _budgetPath;
    private readonly ILogger<TokenBudget>? _logger;
    private readonly object _lock = new();
    private BudgetState _state;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    // Default monthly allowance by tier
    public const long FreeTierWeeklyTokens = 15_000;  // 3 Energy Units ≈ 15K tokens
    public const long ProTierMonthlyTokens = 500_000;
    public const long TokenPackSmall = 100_000;
    public const long TokenPackLarge = 500_000;

    public TokenBudget(string configDirectory, ILogger<TokenBudget>? logger = null)
    {
        _budgetPath = Path.Combine(configDirectory, "token-budget.json");
        _logger = logger;
        _state = LoadState();
    }

    /// <summary>
    /// Get current budget status.
    /// </summary>
    public BudgetStatus GetStatus()
    {
        lock (_lock)
        {
            EnsureCurrentMonth();
            return new BudgetStatus
            {
                Tier = _state.Tier,
                MonthlyAllowance = _state.MonthlyAllowance,
                TokensRemaining = _state.TokensRemaining,
                TokensUsedThisMonth = _state.TokensUsedThisMonth,
                BonusTokens = _state.BonusTokens,
                CycleStart = _state.CycleStart,
                CycleEnd = _state.CycleEnd,
                UsageByProvider = new Dictionary<string, long>(_state.UsageByProvider),
                History = _state.History.TakeLast(10).ToList()
            };
        }
    }

    /// <summary>
    /// Check if a token spend is within budget.
    /// </summary>
    public BudgetCheckResult CheckBudget(long tokenCost)
    {
        lock (_lock)
        {
            EnsureCurrentMonth();

            if (tokenCost <= 0)
                return BudgetCheckResult.Allowed(GetAvailableTokens());

            var available = GetAvailableTokens();
            if (tokenCost > available)
                return BudgetCheckResult.Denied(
                    $"Insufficient tokens. Need {tokenCost:N0}, have {available:N0}. " +
                    $"Resets on {_state.CycleEnd:yyyy-MM-dd}.",
                    available);

            return BudgetCheckResult.Allowed(available - tokenCost);
        }
    }

    /// <summary>
    /// Atomically check budget and reserve tokens. Returns null if denied.
    /// This prevents race conditions between check and record.
    /// </summary>
    public BudgetCheckResult? TryReserve(long tokenCost, string provider, int inputTokens, int outputTokens)
    {
        if (tokenCost <= 0) return null;

        lock (_lock)
        {
            var check = CheckBudget(tokenCost);
            if (!check.IsAllowed)
                return check;

            // Reserve tokens immediately
            RecordUsageInternal(tokenCost, provider, inputTokens, outputTokens);
            return check;
        }
    }

    /// <summary>
    /// Record a token spend. Call AFTER a successful API call.
    /// </summary>
    public void RecordUsage(long tokenCost, string provider, int inputTokens, int outputTokens)
    {
        if (tokenCost <= 0) return;

        lock (_lock)
        {
            RecordUsageInternal(tokenCost, provider, inputTokens, outputTokens);
        }
    }

    private void RecordUsageInternal(long tokenCost, string provider, int inputTokens, int outputTokens)
    {
        EnsureCurrentMonth();

        _state.TokensUsedThisMonth += tokenCost;
        _state.TokensRemaining = Math.Max(0, _state.TokensRemaining - tokenCost);

        // Track per-provider usage
        var providerKey = provider?.ToLowerInvariant() ?? "unknown";
        if (!_state.UsageByProvider.ContainsKey(providerKey))
            _state.UsageByProvider[providerKey] = 0;
        _state.UsageByProvider[providerKey] += tokenCost;

        // Add to history
        _state.History.Add(new UsageRecord
        {
            Timestamp = DateTimeOffset.UtcNow,
            Provider = provider ?? "unknown",
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            ProTokensCost = tokenCost,
            BalanceAfter = _state.TokensRemaining
        });

        // Keep history manageable (last 1000 records)
        if (_state.History.Count > 1000)
            _state.History = _state.History.TakeLast(500).ToList();

        SaveState();

        _logger?.LogInformation(
            "Token usage: {Cost} tokens ({Provider}). Remaining: {Remaining}/{Total}",
            tokenCost, provider, _state.TokensRemaining, GetTotalAllowance());
    }

    /// <summary>
    /// Add bonus tokens (from token pack purchase).
    /// </summary>
    public void AddBonusTokens(long amount, string reason = "Token pack")
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be positive.");

        lock (_lock)
        {
            _state.BonusTokens += amount;
            _state.TokensRemaining += amount;

            _state.History.Add(new UsageRecord
            {
                Timestamp = DateTimeOffset.UtcNow,
                Provider = "system",
                InputTokens = 0,
                OutputTokens = 0,
                ProTokensCost = -amount, // Negative = credit
                BalanceAfter = _state.TokensRemaining
            });

            SaveState();
            _logger?.LogInformation("Added {Amount} bonus tokens ({Reason}). Total: {Total}",
                amount, reason, _state.TokensRemaining);
        }
    }

    /// <summary>
    /// Change tier. Resets monthly allowance.
    /// </summary>
    public void SetTier(string tier)
    {
        lock (_lock)
        {
            _state.Tier = tier.ToLowerInvariant();
            _state.MonthlyAllowance = _state.Tier switch
            {
                "pro" => ProTierMonthlyTokens,
                "free" => FreeTierWeeklyTokens * 4, // ~60K/month for free
                _ => FreeTierWeeklyTokens * 4
            };

            // Reset tokens for new tier
            _state.TokensRemaining = _state.MonthlyAllowance + _state.BonusTokens;
            _state.TokensUsedThisMonth = 0;
            _state.CycleStart = DateTimeOffset.UtcNow;
            _state.CycleEnd = GetMonthEnd(_state.CycleStart);

            SaveState();
            _logger?.LogInformation("Tier changed to {Tier}. Allowance: {Allowance} tokens",
                _state.Tier, _state.MonthlyAllowance);
        }
    }

    /// <summary>
    /// Get available tokens (remaining + bonus).
    /// </summary>
    private long GetAvailableTokens()
    {
        return Math.Max(0, _state.TokensRemaining);
    }

    /// <summary>
    /// Get total allowance (monthly + bonus).
    /// </summary>
    private long GetTotalAllowance()
    {
        return _state.MonthlyAllowance + _state.BonusTokens;
    }

    /// <summary>
    /// Ensure we're in the current billing cycle. Reset if month rolled over.
    /// </summary>
    private void EnsureCurrentMonth()
    {
        var now = DateTimeOffset.UtcNow;
        if (now >= _state.CycleEnd)
        {
            _logger?.LogInformation("Monthly cycle reset. Previous usage: {Used} tokens",
                _state.TokensUsedThisMonth);

            // Carry over bonus tokens, reset monthly
            _state.TokensUsedThisMonth = 0;
            _state.TokensRemaining = _state.MonthlyAllowance + _state.BonusTokens;
            _state.CycleStart = now;
            _state.CycleEnd = GetMonthEnd(now);
            _state.UsageByProvider.Clear();

            SaveState();
        }
    }

    private static DateTimeOffset GetMonthEnd(DateTimeOffset from)
    {
        var nextMonth = from.AddMonths(1);
        return new DateTimeOffset(nextMonth.Year, nextMonth.Month, 1, 0, 0, 0, from.Offset).AddTicks(-1);
    }

    private BudgetState LoadState()
    {
        try
        {
            if (File.Exists(_budgetPath))
            {
                var json = File.ReadAllText(_budgetPath);
                var state = JsonSerializer.Deserialize<BudgetState>(json, JsonOptions);
                if (state != null) return state;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load token budget state, creating default");
        }

        // Default: Free tier
        return new BudgetState
        {
            Tier = "free",
            MonthlyAllowance = FreeTierWeeklyTokens * 4,
            TokensRemaining = FreeTierWeeklyTokens * 4,
            TokensUsedThisMonth = 0,
            BonusTokens = 0,
            CycleStart = DateTimeOffset.UtcNow,
            CycleEnd = GetMonthEnd(DateTimeOffset.UtcNow),
            UsageByProvider = new Dictionary<string, long>(),
            History = new List<UsageRecord>()
        };
    }

    private void SaveState()
    {
        try
        {
            var dir = Path.GetDirectoryName(_budgetPath);
            if (dir != null) Directory.CreateDirectory(dir);

            var tmpPath = _budgetPath + ".tmp";
            var json = JsonSerializer.Serialize(_state, JsonOptions);
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, _budgetPath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save token budget state");
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            SaveState();
            _disposed = true;
        }
    }
}

// ─── Models ───

public class BudgetState
{
    public string Tier { get; set; } = "free";
    public long MonthlyAllowance { get; set; }
    public long TokensRemaining { get; set; }
    public long TokensUsedThisMonth { get; set; }
    public long BonusTokens { get; set; }
    public DateTimeOffset CycleStart { get; set; }
    public DateTimeOffset CycleEnd { get; set; }
    public Dictionary<string, long> UsageByProvider { get; set; } = new();
    public List<UsageRecord> History { get; set; } = new();
}

public class UsageRecord
{
    public DateTimeOffset Timestamp { get; set; }
    public string Provider { get; set; } = string.Empty;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public long ProTokensCost { get; set; }
    public long BalanceAfter { get; set; }
}

public class BudgetStatus
{
    public string Tier { get; init; } = "free";
    public long MonthlyAllowance { get; init; }
    public long TokensRemaining { get; init; }
    public long TokensUsedThisMonth { get; init; }
    public long BonusTokens { get; init; }
    public DateTimeOffset CycleStart { get; init; }
    public DateTimeOffset CycleEnd { get; init; }
    public Dictionary<string, long> UsageByProvider { get; init; } = new();
    public List<UsageRecord> History { get; init; } = new();

    /// <summary>Usage percentage (0-100).</summary>
    public double UsagePercent => MonthlyAllowance > 0
        ? Math.Min(100, (double)TokensUsedThisMonth / MonthlyAllowance * 100)
        : 0;

    /// <summary>Days remaining in current cycle.</summary>
    public int DaysRemaining => Math.Max(0, (int)(CycleEnd - DateTimeOffset.UtcNow).TotalDays);
}

public class BudgetCheckResult
{
    public bool IsAllowed { get; init; }
    public string? DenyReason { get; init; }
    public long RemainingAfter { get; init; }

    public static BudgetCheckResult Allowed(long remainingAfter) => new()
    {
        IsAllowed = true,
        RemainingAfter = remainingAfter
    };

    public static BudgetCheckResult Denied(string reason, long remaining) => new()
    {
        IsAllowed = false,
        DenyReason = reason,
        RemainingAfter = remaining
    };
}
