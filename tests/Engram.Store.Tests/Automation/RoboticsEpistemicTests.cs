using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Engram.Store.Automation;
using Engram.Store.Events;
using ExecutionContext = Engram.Store.Automation.ExecutionContext;

namespace Engram.Store.Tests.Automation;

public class MockCompensationAction : ICompensationAction
{
    public int ExecuteCount { get; private set; }
    public bool ShouldThrow { get; set; }

    public Task ExecuteCompensationAsync(ExecutionContext context, CancellationToken ct)
    {
        ExecuteCount++;
        if (ShouldThrow)
        {
            throw new InvalidOperationException("Mock compensation failed!");
        }
        return Task.CompletedTask;
    }
}

public class MockRollbackHandler : IStepRollback
{
    public int ExecuteCount { get; private set; }

    public Task RollbackAsync(ExecutionContext context, CancellationToken ct)
    {
        ExecuteCount++;
        return Task.CompletedTask;
    }
}

public class MockVerifier : IStepVerifier
{
    public bool Result { get; set; } = true;

    public Task<bool> VerifyAsync(ExecutionContext context, CancellationToken ct)
    {
        return Task.FromResult(Result);
    }
}

public class RoboticsEpistemicTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ActionExecutor _executor;
    private readonly PermissionGate _permissionGate;

    public RoboticsEpistemicTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram_epistemic_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _executor = new ActionExecutor();
        _permissionGate = new PermissionGate();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    [Fact]
    public async Task IdentityEnvelopeContinuity_ShouldPersistAndRestoreLineage()
    {
        // Arrange
        var persistenceStore = new WorkflowPersistenceStore(_tempDir);
        using var runtime = new ActionRuntime(_executor, _permissionGate);
        var worldModel = new OperationalWorldModel(new InMemoryEventBus());
        var wr = new WorkflowRuntime(persistenceStore, runtime, worldModel);

        var plan = new ExecutionPlan
        {
            PlanId = "test_workflow_123",
            Goal = "Test Identity Continuity"
        };
        var step1 = new ExecutionStep
        {
            Id = "step1",
            Action = new AutomationAction
            {
                ActionId = "act1",
                Type = ActionType.Wait,
                Value = "10",
                Description = "Wait for 10ms",
                Permission = ActionPermission.AutoApproved
            }
        };
        plan.Steps[step1.Id] = step1;

        var context = new ExecutionContext();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act - Run with cancelled token to stop after initial checkpoint
        await Assert.ThrowsAnyAsync<Exception>(() => wr.StartWorkflowAsync("test_workflow_123", plan, context, cts.Token));

        var filePath = Path.Combine(persistenceStore.StoreDirectory, "test_workflow_123.json");
        if (File.Exists(filePath))
        {
            Console.WriteLine("CHECKPOINT JSON CONTENT: " + File.ReadAllText(filePath));
        }
        else
        {
            Console.WriteLine("CHECKPOINT JSON FILE NOT FOUND AT: " + filePath);
        }

        // Verify it ran and saved checkpoint
        var checkpoint = await persistenceStore.LoadCheckpointAsync("test_workflow_123");
        Assert.NotNull(checkpoint);
        Assert.NotNull(checkpoint.IdentityEnvelope);
        Assert.Equal("test_workflow_123", checkpoint.IdentityEnvelope.WorkflowId);
        Assert.Contains("Test Identity Continuity", checkpoint.IdentityEnvelope.IntentHistory);

        // Reset runtime envelope to simulate clean restore
        runtime.IdentityEnvelope = null;

        // Restore - also using cancelled token to avoid running/deleting checkpoint on success
        var newContext = new ExecutionContext();
        await Assert.ThrowsAnyAsync<Exception>(() => wr.RestoreWorkflowAsync("test_workflow_123", newContext, cts.Token));

        // Assert restored identity envelope is intact
        Assert.NotNull(runtime.IdentityEnvelope);
        Assert.Equal("test_workflow_123", runtime.IdentityEnvelope.WorkflowId);
        Assert.Contains("Test Identity Continuity", runtime.IdentityEnvelope.IntentHistory);
    }

    [Fact]
    public async Task CompensationVsRollback_ShouldExecuteCompensation_WhenCompensatable()
    {
        // Arrange
        using var runtime = new ActionRuntime(_executor, _permissionGate);
        var mockCompensation = new MockCompensationAction();
        var mockRollback = new MockRollbackHandler();

        var plan = new ExecutionPlan { PlanId = "comp_test", Goal = "Test Compensation" };
        
        var step1 = new ExecutionStep
        {
            Id = "step1",
            Action = new AutomationAction { ActionId = "act1", Type = ActionType.Wait, Value = "10", Permission = ActionPermission.AutoApproved },
            RollbackHandler = mockRollback,
            Status = StepStatus.Completed,
            Semantics = new MutationBoundarySemantics
            {
                IsCompensatable = true,
                CompensationAction = mockCompensation
            }
        };

        var step2 = new ExecutionStep
        {
            Id = "step2",
            Action = new AutomationAction { ActionId = "act2", Type = ActionType.Wait, Value = "10", Permission = ActionPermission.AutoApproved },
            Verifier = new MockVerifier { Result = false } // verification failure triggers rollback
        };

        plan.Steps[step1.Id] = step1;
        plan.Steps[step2.Id] = step2;
        step2.DependsOn.Add(step1.Id);

        var context = new ExecutionContext();

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(() => runtime.ExecutePlanAsync(plan, context));

        // step1's compensation action should run, NOT its rollback handler
        Assert.Equal(1, mockCompensation.ExecuteCount);
        Assert.Equal(0, mockRollback.ExecuteCount);
    }

    [Fact]
    public async Task CompensationVsRollback_ShouldSuspend_WhenIrrecoverable()
    {
        // Arrange
        using var runtime = new ActionRuntime(_executor, _permissionGate);
        var mockRollback = new MockRollbackHandler();

        var plan = new ExecutionPlan { PlanId = "irrecoverable_test", Goal = "Test Rollback Bypass" };
        
        var step1 = new ExecutionStep
        {
            Id = "step1",
            Action = new AutomationAction { ActionId = "act1", Type = ActionType.Wait, Value = "10", Permission = ActionPermission.AutoApproved },
            RollbackHandler = mockRollback,
            Status = StepStatus.Completed,
            Semantics = new MutationBoundarySemantics
            {
                IsIrrecoverable = true
            }
        };

        var step2 = new ExecutionStep
        {
            Id = "step2",
            Action = new AutomationAction { ActionId = "act2", Type = ActionType.Wait, Value = "10", Permission = ActionPermission.AutoApproved },
            Verifier = new MockVerifier { Result = false }
        };

        plan.Steps[step1.Id] = step1;
        plan.Steps[step2.Id] = step2;
        step2.DependsOn.Add(step1.Id);

        var context = new ExecutionContext();

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(() => runtime.ExecutePlanAsync(plan, context));

        // Rollback should be bypassed entirely, leaving step1 untouched
        Assert.Equal(0, mockRollback.ExecuteCount);
        Assert.Equal(WorkflowState.Suspended, runtime.StateMachine.CurrentState);
    }

    [Fact]
    public async Task CompensationVsRollback_ShouldSuspend_WhenCompensationThrows()
    {
        // Arrange
        using var runtime = new ActionRuntime(_executor, _permissionGate);
        var mockCompensation = new MockCompensationAction { ShouldThrow = true };

        var plan = new ExecutionPlan { PlanId = "failed_comp_test", Goal = "Test Compensation Failure" };
        
        var step1 = new ExecutionStep
        {
            Id = "step1",
            Action = new AutomationAction { ActionId = "act1", Type = ActionType.Wait, Value = "10", Permission = ActionPermission.AutoApproved },
            Status = StepStatus.Completed,
            Semantics = new MutationBoundarySemantics
            {
                IsCompensatable = true,
                CompensationAction = mockCompensation
            }
        };

        var step2 = new ExecutionStep
        {
            Id = "step2",
            Action = new AutomationAction { ActionId = "act2", Type = ActionType.Wait, Value = "10", Permission = ActionPermission.AutoApproved },
            Verifier = new MockVerifier { Result = false }
        };

        plan.Steps[step1.Id] = step1;
        plan.Steps[step2.Id] = step2;
        step2.DependsOn.Add(step1.Id);

        var context = new ExecutionContext();

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(() => runtime.ExecutePlanAsync(plan, context));

        Assert.Equal(1, mockCompensation.ExecuteCount);
        Assert.Equal(WorkflowState.Suspended, runtime.StateMachine.CurrentState);
    }

    [Fact]
    public async Task ContextualOverlays_ShouldEnforceAppScopedRules()
    {
        // Arrange
        var interruptGraph = new EnvironmentalInterruptGraph();
        var context = new ExecutionContext();

        // Policy 1: Banking scope -> Suspend on "Confirm closure"
        interruptGraph.RegisterPolicy(new ContextualOverlayPolicy
        {
            AppScope = "banking.com",
            Rule = SafetyRule.Suspend,
            Keywords = new List<string> { "Confirm closure" }
        });

        // Policy 2: Developer sandbox scope -> AutoDismiss on "Confirm closure"
        interruptGraph.RegisterPolicy(new ContextualOverlayPolicy
        {
            AppScope = "devenv",
            Rule = SafetyRule.AutoDismiss,
            Keywords = new List<string> { "Confirm closure" }
        });

        // Act & Assert 1: Banking context
        context.SetVariable("current_url", "https://banking.com/dashboard");
        var resultBanking = await interruptGraph.AssessAndHandleInterruptAsync(
            "chrome", "Confirm closure of accounts", context, CancellationToken.None);
        Assert.False(resultBanking); // Should Suspend / Human Required

        // Act & Assert 2: Devenv context
        context.SetVariable("current_url", "");
        context.SetVariable("AppName", "devenv");
        var resultDevenv = await interruptGraph.AssessAndHandleInterruptAsync(
            "devenv", "Confirm closure of file", context, CancellationToken.None);
        Assert.True(resultDevenv); // Should AutoDismiss
    }

    [Fact]
    public async Task DriftCorrelation_ShouldIsolateConfidenceDegradationByScope()
    {
        // Arrange
        using var runtime = new ActionRuntime(_executor, _permissionGate);
        var driftEngine = runtime.DriftCorrelationEngine;

        // Act: Degrade Chrome scope multiple times
        driftEngine.RecordObservation("step1", "Chrome", DriftType.Environmental, 0.9);
        driftEngine.RecordObservation("step2", "Chrome", DriftType.Environmental, 0.8);
        driftEngine.RecordObservation("step3", "Chrome", DriftType.Environmental, 0.9);

        // Assert: Chrome scope confidence is degraded
        double chromeConfidence = driftEngine.GetScopeConfidence("Chrome");
        Assert.True(chromeConfidence < 0.5);
        Assert.True(driftEngine.ShouldRecalibrate("Chrome"));

        // Assert: Word scope confidence remains untouched
        double wordConfidence = driftEngine.GetScopeConfidence("Word");
        Assert.Equal(1.0, wordConfidence);
        Assert.False(driftEngine.ShouldRecalibrate("Word"));

        // Assert: Running a step in degraded "Chrome" scope triggers immediate suspension
        var plan = new ExecutionPlan { PlanId = "drift_halt_test", Goal = "Halt degraded scope" };
        var step1 = new ExecutionStep
        {
            Id = "step1",
            Action = new AutomationAction { ActionId = "act1", Type = ActionType.Wait, Value = "10", Permission = ActionPermission.AutoApproved }
        };
        plan.Steps[step1.Id] = step1;

        var context = new ExecutionContext();
        context.SetVariable("AppName", "Chrome");

        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.ExecutePlanAsync(plan, context));
        Assert.Equal(WorkflowState.Suspended, runtime.StateMachine.CurrentState);
    }
}
