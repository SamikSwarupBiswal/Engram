namespace Engram.Store.Cloud;

/// <summary>
/// Response from a cloud model provider.
/// </summary>
public class CloudModelResponse
{
    /// <summary>The model's response text.</summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>Which provider handled this request.</summary>
    public string Provider { get; init; } = string.Empty;

    /// <summary>Which specific model was used.</summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>Estimated cost in USD for this call.</summary>
    public decimal CostEstimate { get; init; }

    /// <summary>Input tokens consumed.</summary>
    public int InputTokens { get; init; }

    /// <summary>Output tokens generated.</summary>
    public int OutputTokens { get; init; }

    /// <summary>Whether the call succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>Error message if failed.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Whether the response was served from cache.</summary>
    public bool FromCache { get; init; }
}
