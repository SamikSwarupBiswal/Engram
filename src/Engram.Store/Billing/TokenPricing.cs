namespace Engram.Store.Billing;

/// <summary>
/// Defines how many Pro tokens each model costs.
/// Different models have different costs — users choose how to spend their budget.
///
/// Example: 500,000 Pro tokens/month
///   Gemini Flash:  1 Pro token per 1 input + 3 per output (cheap)
///   Claude Sonnet: 10 Pro tokens per 1 input + 30 per output (expensive)
///
/// So 500K Pro tokens ≈ 400K Gemini tokens OR 40K Claude tokens.
/// </summary>
public static class TokenPricing
{
    /// <summary>
    /// Get the Pro token cost for a given provider and token counts.
    /// Returns the number of Pro tokens consumed.
    /// </summary>
    public static long CalculateCost(string provider, int inputTokens, int outputTokens)
    {
        var rates = GetRates(provider);
        return (long)(inputTokens * rates.InputMultiplier + outputTokens * rates.OutputMultiplier);
    }

    /// <summary>
    /// Get pricing rates for a provider.
    /// </summary>
    public static PricingRates GetRates(string provider)
    {
        return provider?.ToLowerInvariant() switch
        {
            "gemini-flash" or "gemini" => new PricingRates
            {
                InputMultiplier = 1.0,
                OutputMultiplier = 3.0,
                DisplayName = "Gemini Flash",
                Description = "Cheap, fast, good for routine tasks"
            },
            "claude-sonnet" or "claude" => new PricingRates
            {
                InputMultiplier = 10.0,
                OutputMultiplier = 30.0,
                DisplayName = "Claude Sonnet",
                Description = "Expensive, powerful, good for complex reasoning"
            },
            "mock" or "local" => new PricingRates
            {
                InputMultiplier = 0,
                OutputMultiplier = 0,
                DisplayName = "Local/Free",
                Description = "Local inference — no token cost"
            },
            _ => new PricingRates
            {
                InputMultiplier = 5.0,
                OutputMultiplier = 15.0,
                DisplayName = provider ?? "Unknown",
                Description = "Default pricing for unknown provider"
            }
        };
    }

    /// <summary>
    /// Estimate how many Pro tokens a message of given character count would cost.
    /// Rough estimate: 4 chars ≈ 1 token.
    /// </summary>
    public static long EstimateMessageCost(string provider, int inputChars, int estimatedOutputChars = 500)
    {
        var inputTokens = inputChars / 4;
        var outputTokens = estimatedOutputChars / 4;
        return CalculateCost(provider, inputTokens, outputTokens);
    }
}

public class PricingRates
{
    /// <summary>Pro tokens consumed per 1 input token from this model.</summary>
    public double InputMultiplier { get; init; }

    /// <summary>Pro tokens consumed per 1 output token from this model.</summary>
    public double OutputMultiplier { get; init; }

    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}
