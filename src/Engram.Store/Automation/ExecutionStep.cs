using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Engram.Store.Automation;

public enum StepStatus
{
    Pending,
    Executing,
    Completed,
    Failed,
    Skipped,
    RolledBack
}

public interface IStepVerifier
{
    Task<bool> VerifyAsync(ExecutionContext context, CancellationToken ct);
}

public interface IStepRollback
{
    Task RollbackAsync(ExecutionContext context, CancellationToken ct);
}

public interface IStepRecovery
{
    Task<bool> RecoverAsync(ExecutionContext context, Exception exception, CancellationToken ct);
}

public class ExecutionStep
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public AutomationAction Action { get; init; } = null!;
    public List<string> DependsOn { get; init; } = new();
    public IStepVerifier? Verifier { get; init; }
    public IStepRollback? RollbackHandler { get; init; }
    public IStepRecovery? RecoveryPolicy { get; init; }
    
    public StepStatus Status { get; set; } = StepStatus.Pending;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Error { get; set; }
}
