using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Engram.Store.Automation;

public enum TransitionalState
{
    Stable,                  // Stable state (requires full process validation, UIA tree scanning, fingerprinting, and SLM)
    StartMenuVisible,        // Windows Start Menu is visible / Search app active
    LauncherSearchFocused,   // Launcher input is active/ready for typing
    SearchResultsVisible,    // Search results are updating
    AppLaunching,            // Intermediate state while process starts up
    DocumentReady,           // A document editor surface is active and ready for input
    DirectInput,             // Deterministic text entry into the verified active window
    TransitionUncertain      // Transition verification failed or uncertain
}

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
    public MutationBoundarySemantics? Semantics { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Error { get; set; }
}
