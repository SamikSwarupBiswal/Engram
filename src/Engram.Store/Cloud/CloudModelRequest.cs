namespace Engram.Store.Cloud;

/// <summary>
/// Request to a cloud model provider.
/// Contains the sanitized payload — private data must be stripped by LocalFilter before this.
/// </summary>
public class CloudModelRequest
{
    /// <summary>Human-readable reason for this cloud call (audit trail).</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>The task complexity classification.</summary>
    public TaskComplexity Complexity { get; init; } = TaskComplexity.Low;

    /// <summary>Sanitized text payload. Must NOT contain private data.</summary>
    public string Payload { get; init; } = string.Empty;

    /// <summary>Maximum tokens to generate.</summary>
    public int MaxTokens { get; init; } = 1024;

    /// <summary>Privacy class of the original data (before filtering).</summary>
    public PrivacyClass OriginalPrivacyClass { get; init; } = PrivacyClass.Public;

    /// <summary>Metadata tags for routing and caching.</summary>
    public Dictionary<string, string> Metadata { get; init; } = new();
}
