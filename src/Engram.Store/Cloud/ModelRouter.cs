namespace Engram.Store.Cloud;

/// <summary>
/// Routes tasks to local or cloud compute based on complexity and tier.
/// Local tasks never leave the device.
/// Cloud tasks go through LocalFilter, TierGuard, BudgetManager, and AuditLog.
/// </summary>
public class ModelRouter
{
    private readonly TierGuard _tierGuard;

    public ModelRouter(TierGuard tierGuard)
    {
        _tierGuard = tierGuard ?? throw new ArgumentNullException(nameof(tierGuard));
    }

    /// <summary>
    /// Determine the compute target for a given task complexity.
    /// </summary>
    public RoutingDecision Route(TaskComplexity complexity)
    {
        if (complexity == TaskComplexity.Low)
            return RoutingDecision.Local("Routine task — handled by local SLM.");

        var gate = _tierGuard.CheckCloudAccess();
        if (!gate.IsAllowed)
            return RoutingDecision.FallbackToLocal(gate.BlockReason!);

        return complexity switch
        {
            TaskComplexity.Medium => RoutingDecision.Cloud(ComputeTarget.GeminiFlash, "Medium complexity — routing to Gemini 3 Flash."),
            TaskComplexity.High => RoutingDecision.Cloud(ComputeTarget.ClaudeSonnet, "High complexity — routing to Claude 4.5 Sonnet."),
            _ => RoutingDecision.Local("Unknown complexity — defaulting to local.")
        };
    }
}

public enum ComputeTarget
{
    Local,
    GeminiFlash,
    ClaudeSonnet
}

public class RoutingDecision
{
    public ComputeTarget Target { get; init; }
    public bool IsCloud { get; init; }
    public string Reason { get; init; } = string.Empty;

    public static RoutingDecision Local(string reason) => new() { Target = ComputeTarget.Local, IsCloud = false, Reason = reason };
    public static RoutingDecision Cloud(ComputeTarget target, string reason) => new() { Target = target, IsCloud = true, Reason = reason };
    public static RoutingDecision FallbackToLocal(string reason) => new() { Target = ComputeTarget.Local, IsCloud = false, Reason = reason };
}
