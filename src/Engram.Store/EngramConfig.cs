using Engram.Store.Cloud;

namespace Engram.Store;

/// <summary>
/// Configuration model for the Engram workspace.
/// </summary>
public class EngramConfig
{
    public string Version { get; set; } = "1.0.0";
    public bool ClipboardCaptureEnabled { get; set; } = false;
    public bool ActiveWindowCaptureEnabled { get; set; } = false;
    public bool FileWatcherEnabled { get; set; } = false;
    public List<string> ExcludedApps { get; set; } = new();
    public List<string> WatchedPaths { get; set; } = new();

    // --- Pro Tier / Cloud Settings ---
    /// <summary>Current subscription tier.</summary>
    public TierLevel Tier { get; set; } = TierLevel.Free;

    /// <summary>Whether cloud features are enabled (requires Pro tier).</summary>
    public bool CloudEnabled { get; set; } = false;

    /// <summary>Daily cloud cost budget in USD.</summary>
    public decimal DailyBudgetUsd { get; set; } = 1.00m;

    /// <summary>Monthly cloud cost budget in USD.</summary>
    public decimal MonthlyBudgetUsd { get; set; } = 25.00m;

    /// <summary>Maximum cost per single cloud call in USD.</summary>
    public decimal PerCallLimitUsd { get; set; } = 0.50m;

    // --- Custom Provider Settings ---
    /// <summary>Custom provider API key (OpenAI, Groq, Together, etc.)</summary>
    public string? CustomProviderApiKey { get; set; }

    /// <summary>Custom provider base URL (e.g., "https://api.openai.com/v1")</summary>
    public string? CustomProviderBaseUrl { get; set; }

    /// <summary>Custom provider model name (e.g., "gpt-4o")</summary>
    public string? CustomProviderModel { get; set; }

    /// <summary>Custom provider friendly name (e.g., "openai", "groq")</summary>
    public string? CustomProviderName { get; set; }
}

/// <summary>
/// Subscription tier level.
/// </summary>
public enum TierLevel
{
    Free = 0,
    Pro = 1
}
