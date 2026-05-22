using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Engram.Store.Automation;
using Xunit;
using ExecutionContext = Engram.Store.Automation.ExecutionContext;

namespace Engram.Store.Tests;

public class ActionGraphTests
{
    private readonly ActionExecutor _executor = new();
    private readonly PermissionGate _permissionGate = new();

    // Custom helper class to implement interfaces for testing
    private class DelegatingVerifier : IStepVerifier
    {
        private readonly Func<ExecutionContext, CancellationToken, Task<bool>> _verifyFunc;
        public DelegatingVerifier(Func<ExecutionContext, CancellationToken, Task<bool>> verifyFunc) => _verifyFunc = verifyFunc;
        public Task<bool> VerifyAsync(ExecutionContext context, CancellationToken ct) => _verifyFunc(context, ct);
    }

    private class DelegatingRollback : IStepRollback
    {
        private readonly Func<ExecutionContext, CancellationToken, Task> _rollbackFunc;
        public DelegatingRollback(Func<ExecutionContext, CancellationToken, Task> rollbackFunc) => _rollbackFunc = rollbackFunc;
        public Task RollbackAsync(ExecutionContext context, CancellationToken ct) => _rollbackFunc(context, ct);
    }

    private class DelegatingRecovery : IStepRecovery
    {
        private readonly Func<ExecutionContext, Exception, CancellationToken, Task<bool>> _recoverFunc;
        public DelegatingRecovery(Func<ExecutionContext, Exception, CancellationToken, Task<bool>> recoverFunc) => _recoverFunc = recoverFunc;
        public Task<bool> RecoverAsync(ExecutionContext context, Exception exception, CancellationToken ct) => _recoverFunc(context, exception, ct);
    }

    // ─── Graph Validation Tests ───

    [Fact]
    public void Validate_ValidDag_DoesNotThrow()
    {
        var plan = new ExecutionPlan();
        plan.Steps["D"] = new ExecutionStep { Id = "D", Action = new AutomationAction { Permission = ActionPermission.AutoApproved, Type = ActionType.Wait } };
        plan.Steps["B"] = new ExecutionStep { Id = "B", Action = new AutomationAction { Permission = ActionPermission.AutoApproved, Type = ActionType.Wait }, DependsOn = new() { "D" } };
        plan.Steps["C"] = new ExecutionStep { Id = "C", Action = new AutomationAction { Permission = ActionPermission.AutoApproved, Type = ActionType.Wait }, DependsOn = new() { "D" } };
        plan.Steps["A"] = new ExecutionStep { Id = "A", Action = new AutomationAction { Permission = ActionPermission.AutoApproved, Type = ActionType.Wait }, DependsOn = new() { "B", "C" } };

        // Diamond DAG (A depends on B & C, which depend on D). Valid.
        var exception = Record.Exception(() => plan.Validate());
        Assert.Null(exception);
    }

    [Fact]
    public void Validate_MissingDependency_Throws()
    {
        var plan = new ExecutionPlan();
        plan.Steps["A"] = new ExecutionStep 
        { 
            Id = "A", 
            Action = new AutomationAction { Permission = ActionPermission.AutoApproved, Type = ActionType.Wait }, 
            DependsOn = new() { "MissingStep" } 
        };

        var exception = Assert.Throws<InvalidOperationException>(() => plan.Validate());
        Assert.Contains("depends on missing step 'MissingStep'", exception.Message);
    }

    [Fact]
    public void Validate_SelfCycle_Throws()
    {
        var plan = new ExecutionPlan();
        plan.Steps["A"] = new ExecutionStep 
        { 
            Id = "A", 
            Action = new AutomationAction { Permission = ActionPermission.AutoApproved, Type = ActionType.Wait }, 
            DependsOn = new() { "A" } 
        };

        var exception = Assert.Throws<InvalidOperationException>(() => plan.Validate());
        Assert.Contains("Dependency cycle detected", exception.Message);
        Assert.Contains("A -> A", exception.Message);
    }

    [Fact]
    public void Validate_MultiStepCycle_Throws()
    {
        var plan = new ExecutionPlan();
        plan.Steps["A"] = new ExecutionStep { Id = "A", Action = new AutomationAction { Permission = ActionPermission.AutoApproved, Type = ActionType.Wait }, DependsOn = new() { "B" } };
        plan.Steps["B"] = new ExecutionStep { Id = "B", Action = new AutomationAction { Permission = ActionPermission.AutoApproved, Type = ActionType.Wait }, DependsOn = new() { "C" } };
        plan.Steps["C"] = new ExecutionStep { Id = "C", Action = new AutomationAction { Permission = ActionPermission.AutoApproved, Type = ActionType.Wait }, DependsOn = new() { "A" } };

        var exception = Assert.Throws<InvalidOperationException>(() => plan.Validate());
        Assert.Contains("Dependency cycle detected", exception.Message);
    }

    // ─── Topological Sort Tests ───

    [Fact]
    public void GetTopologicalOrder_DiamondGraph_ResolvesCorrectly()
    {
        var runtime = new ActionRuntime(_executor, _permissionGate);
        var plan = new ExecutionPlan();
        plan.Steps["D"] = new ExecutionStep { Id = "D", Action = new AutomationAction { Type = ActionType.Wait } };
        plan.Steps["B"] = new ExecutionStep { Id = "B", Action = new AutomationAction { Type = ActionType.Wait }, DependsOn = new() { "D" } };
        plan.Steps["C"] = new ExecutionStep { Id = "C", Action = new AutomationAction { Type = ActionType.Wait }, DependsOn = new() { "D" } };
        plan.Steps["A"] = new ExecutionStep { Id = "A", Action = new AutomationAction { Type = ActionType.Wait }, DependsOn = new() { "B", "C" } };

        var order = runtime.GetTopologicalOrder(plan);

        // Verification:
        // 'D' must appear before B and C.
        // B and C must appear before A.
        int idxA = order.FindIndex(s => s.Id == "A");
        int idxB = order.FindIndex(s => s.Id == "B");
        int idxC = order.FindIndex(s => s.Id == "C");
        int idxD = order.FindIndex(s => s.Id == "D");

        Assert.True(idxD < idxB);
        Assert.True(idxD < idxC);
        Assert.True(idxB < idxA);
        Assert.True(idxC < idxA);
    }

    // ─── Happy Path Execution Tests ───

    [Fact]
    public async Task ExecutePlanAsync_HappyPath_ExecutesAllSteps()
    {
        var runtime = new ActionRuntime(_executor, _permissionGate);
        var plan = new ExecutionPlan();
        
        var step1 = new ExecutionStep { Id = "S1", Action = new AutomationAction { Permission = ActionPermission.AutoApproved, Type = ActionType.Wait, Value = "10" } };
        var step2 = new ExecutionStep { Id = "S2", Action = new AutomationAction { Permission = ActionPermission.AutoApproved, Type = ActionType.Wait, Value = "10" }, DependsOn = new() { "S1" } };
        
        plan.Steps["S1"] = step1;
        plan.Steps["S2"] = step2;

        var context = new ExecutionContext();
        await runtime.ExecutePlanAsync(plan, context);

        Assert.Equal(StepStatus.Completed, step1.Status);
        Assert.Equal(StepStatus.Completed, step2.Status);
        Assert.NotNull(step1.CompletedAt);
        Assert.NotNull(step2.CompletedAt);
    }

    // ─── Step Verification Tests ───

    [Fact]
    public async Task ExecutePlanAsync_VerifierSucceeds_CompletesStep()
    {
        var runtime = new ActionRuntime(_executor, _permissionGate);
        var plan = new ExecutionPlan();

        bool verifierCalled = false;
        var step = new ExecutionStep
        {
            Id = "S1",
            Action = new AutomationAction { Permission = ActionPermission.AutoApproved, Type = ActionType.Wait, Value = "10" },
            Verifier = new DelegatingVerifier((ctx, ct) =>
            {
                verifierCalled = true;
                return Task.FromResult(true);
            })
        };

        plan.Steps["S1"] = step;

        await runtime.ExecutePlanAsync(plan, new ExecutionContext());

        Assert.True(verifierCalled);
        Assert.Equal(StepStatus.Completed, step.Status);
    }

    [Fact]
    public async Task ExecutePlanAsync_VerifierFails_TriggersRollbackAndThrows()
    {
        var runtime = new ActionRuntime(_executor, _permissionGate);
        var plan = new ExecutionPlan();

        var step1 = new ExecutionStep
        {
            Id = "S1",
            Action = new AutomationAction { Permission = ActionPermission.AutoApproved, Type = ActionType.Wait, Value = "10" }
        };

        var step2 = new ExecutionStep
        {
            Id = "S2",
            Action = new AutomationAction { Permission = ActionPermission.AutoApproved, Type = ActionType.Wait, Value = "10" },
            DependsOn = new() { "S1" },
            Verifier = new DelegatingVerifier((ctx, ct) => Task.FromResult(false)) // Fails
        };

        plan.Steps["S1"] = step1;
        plan.Steps["S2"] = step2;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.ExecutePlanAsync(plan, new ExecutionContext()));
        Assert.Contains("Verification failed for step 'S2'", exception.Message);

        Assert.Equal(StepStatus.Failed, step2.Status);
        // S1 was completed but must be rolled back because S2 failed
        Assert.Equal(StepStatus.RolledBack, step1.Status);
    }

    // ─── Recovery Tests ───

    [Fact]
    public async Task ExecutePlanAsync_RecoverySucceeds_CompletesStep()
    {
        var runtime = new ActionRuntime(_executor, _permissionGate);
        var plan = new ExecutionPlan();

        int executionAttempts = 0;
        bool recoveryCalled = false;

        // Custom action execution could fail or we can mock/stub.
        // Wait, ActionExecutor.ExecuteAsync throws on Type without target.
        // We can leverage this to make it fail the first time, and we modify the target in recovery!
        var action = new AutomationAction
        {
            Permission = ActionPermission.AutoApproved,
            Type = ActionType.Type,
            Value = "test"
            // Target is missing initially -> fails
        };

        var step = new ExecutionStep
        {
            Id = "S1",
            Action = action,
            RecoveryPolicy = new DelegatingRecovery((ctx, ex, ct) =>
            {
                recoveryCalled = true;
                // Fix the action Target in recovery so the retry succeeds!
                typeof(AutomationAction).GetProperty(nameof(AutomationAction.Target))?
                    .SetValue(action, new ActionTarget { Selector = "#input" });
                return Task.FromResult(true); // Signify recovered
            })
        };

        plan.Steps["S1"] = step;

        await runtime.ExecutePlanAsync(plan, new ExecutionContext());

        Assert.True(recoveryCalled);
        Assert.Equal(StepStatus.Completed, step.Status);
        Assert.Equal("#input", step.Action.Target?.Selector);
    }

    [Fact]
    public async Task ExecutePlanAsync_RecoveryFails_TriggersRollbackAndThrows()
    {
        var runtime = new ActionRuntime(_executor, _permissionGate);
        var plan = new ExecutionPlan();

        var step1 = new ExecutionStep
        {
            Id = "S1",
            Action = new AutomationAction { Permission = ActionPermission.AutoApproved, Type = ActionType.Wait, Value = "10" }
        };

        var step2 = new ExecutionStep
        {
            Id = "S2",
            DependsOn = new() { "S1" },
            Action = new AutomationAction { Permission = ActionPermission.AutoApproved, Type = ActionType.Type, Value = "test" }, // Fails (no target)
            RecoveryPolicy = new DelegatingRecovery((ctx, ex, ct) => Task.FromResult(false)) // Fails recovery
        };

        plan.Steps["S1"] = step1;
        plan.Steps["S2"] = step2;

        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.ExecutePlanAsync(plan, new ExecutionContext()));

        Assert.Equal(StepStatus.RolledBack, step1.Status);
        Assert.Equal(StepStatus.Failed, step2.Status);
    }

    // ─── Rollback Tests ───

    [Fact]
    public async Task ExecutePlanAsync_LIFO_Rollback_ExecutedCorrectly()
    {
        var runtime = new ActionRuntime(_executor, _permissionGate);
        var plan = new ExecutionPlan();

        var rollbackOrder = new List<string>();

        var step1 = new ExecutionStep
        {
            Id = "S1",
            Action = new AutomationAction { Permission = ActionPermission.AutoApproved, Type = ActionType.Wait, Value = "10" },
            RollbackHandler = new DelegatingRollback((ctx, ct) =>
            {
                rollbackOrder.Add("S1");
                return Task.CompletedTask;
            })
        };

        var step2 = new ExecutionStep
        {
            Id = "S2",
            DependsOn = new() { "S1" },
            Action = new AutomationAction { Permission = ActionPermission.AutoApproved, Type = ActionType.Wait, Value = "10" },
            RollbackHandler = new DelegatingRollback((ctx, ct) =>
            {
                rollbackOrder.Add("S2");
                return Task.CompletedTask;
            })
        };

        var step3 = new ExecutionStep
        {
            Id = "S3",
            DependsOn = new() { "S2" },
            Action = new AutomationAction { Permission = ActionPermission.AutoApproved, Type = ActionType.Type, Value = "test" } // Fails (no target)
        };

        plan.Steps["S1"] = step1;
        plan.Steps["S2"] = step2;
        plan.Steps["S3"] = step3;

        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.ExecutePlanAsync(plan, new ExecutionContext()));

        // S3 failed, so completed S2 and S1 must roll back in reverse order (S2, then S1)
        Assert.Equal(2, rollbackOrder.Count);
        Assert.Equal("S2", rollbackOrder[0]);
        Assert.Equal("S1", rollbackOrder[1]);

        Assert.Equal(StepStatus.RolledBack, step1.Status);
        Assert.Equal(StepStatus.RolledBack, step2.Status);
        Assert.Equal(StepStatus.Failed, step3.Status);
    }

    [Fact]
    public async Task ExecutePlanAsync_RollbackException_DoesNotStopCascade()
    {
        var runtime = new ActionRuntime(_executor, _permissionGate);
        var plan = new ExecutionPlan();

        bool step1RollbackCalled = false;

        var step1 = new ExecutionStep
        {
            Id = "S1",
            Action = new AutomationAction { Permission = ActionPermission.AutoApproved, Type = ActionType.Wait, Value = "10" },
            RollbackHandler = new DelegatingRollback((ctx, ct) =>
            {
                step1RollbackCalled = true;
                return Task.CompletedTask;
            })
        };

        var step2 = new ExecutionStep
        {
            Id = "S2",
            DependsOn = new() { "S1" },
            Action = new AutomationAction { Permission = ActionPermission.AutoApproved, Type = ActionType.Wait, Value = "10" },
            RollbackHandler = new DelegatingRollback((ctx, ct) =>
            {
                throw new InvalidOperationException("Rollback error"); // Throws during rollback
            })
        };

        var step3 = new ExecutionStep
        {
            Id = "S3",
            DependsOn = new() { "S2" },
            Action = new AutomationAction { Permission = ActionPermission.AutoApproved, Type = ActionType.Type, Value = "test" } // Fails
        };

        plan.Steps["S1"] = step1;
        plan.Steps["S2"] = step2;
        plan.Steps["S3"] = step3;

        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.ExecutePlanAsync(plan, new ExecutionContext()));

        // S2 rollback failure must not prevent S1 rollback from being called
        Assert.True(step1RollbackCalled);
        Assert.Equal(StepStatus.RolledBack, step1.Status);
        Assert.Equal(StepStatus.RolledBack, step2.Status);
    }

    // ─── Permission Gate / Skipped Steps Tests ───

    [Fact]
    public async Task ExecutePlanAsync_UnapprovedAction_FailsAndSkipsRemaining()
    {
        var runtime = new ActionRuntime(_executor, _permissionGate);
        var plan = new ExecutionPlan();

        var step1 = new ExecutionStep
        {
            Id = "S1",
            Action = new AutomationAction { Permission = ActionPermission.AutoApproved, Type = ActionType.Wait, Value = "10" }
        };

        var step2 = new ExecutionStep
        {
            Id = "S2",
            DependsOn = new() { "S1" },
            // Requires approval (Click is not auto-approved, default Permission is Pending)
            Action = new AutomationAction { Type = ActionType.Click, Target = new ActionTarget { Selector = "button" } }
        };

        var step3 = new ExecutionStep
        {
            Id = "S3",
            DependsOn = new() { "S2" },
            Action = new AutomationAction { Permission = ActionPermission.AutoApproved, Type = ActionType.Wait, Value = "10" }
        };

        plan.Steps["S1"] = step1;
        plan.Steps["S2"] = step2;
        plan.Steps["S3"] = step3;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.ExecutePlanAsync(plan, new ExecutionContext()));
        Assert.Contains("action is not approved", exception.Message);

        Assert.Equal(StepStatus.RolledBack, step1.Status); // S1 is rolled back because S2 failed permission
        Assert.Equal(StepStatus.Failed, step2.Status);
        Assert.Equal(StepStatus.Skipped, step3.Status); // S3 never ran and is marked Skipped
    }

    // ─── Task Planner Tests ───

    [Fact]
    public void TaskPlanner_HeuristicPlan_GeneratesSequentialSteps()
    {
        var planner = new TaskPlanner();
        var goal = "open google.com, then type 'Engram' into 'input[name=q]', and click 'input[name=btnK]', then wait 5 seconds then take a screenshot";

        var plan = planner.PlanWithHeuristics(goal);

        Assert.Equal(goal, plan.Goal);
        Assert.Equal(5, plan.Steps.Count);

        // Steps should be step_1, step_2, step_3, step_4, step_5
        Assert.True(plan.Steps.ContainsKey("step_1"));
        Assert.True(plan.Steps.ContainsKey("step_2"));
        Assert.True(plan.Steps.ContainsKey("step_3"));
        Assert.True(plan.Steps.ContainsKey("step_4"));
        Assert.True(plan.Steps.ContainsKey("step_5"));

        var s1 = plan.Steps["step_1"];
        var s2 = plan.Steps["step_2"];
        var s3 = plan.Steps["step_3"];
        var s4 = plan.Steps["step_4"];
        var s5 = plan.Steps["step_5"];

        // Action types
        Assert.Equal(ActionType.Navigate, s1.Action.Type);
        Assert.Equal(ActionType.Type, s2.Action.Type);
        Assert.Equal(ActionType.Click, s3.Action.Type);
        Assert.Equal(ActionType.Wait, s4.Action.Type);
        Assert.Equal(ActionType.Screenshot, s5.Action.Type);

        // Sequential dependencies
        Assert.Empty(s1.DependsOn);
        Assert.Equal(new List<string> { "step_1" }, s2.DependsOn);
        Assert.Equal(new List<string> { "step_2" }, s3.DependsOn);
        Assert.Equal(new List<string> { "step_3" }, s4.DependsOn);
        Assert.Equal(new List<string> { "step_4" }, s5.DependsOn);

        // Extracted values
        Assert.Equal("https://google.com", s1.Action.Value);
        Assert.Equal("Engram", s2.Action.Value);
        Assert.Equal("input[name=q]", s2.Action.Target?.Selector);
        Assert.Equal("input[name=btnK]", s3.Action.Target?.Selector);
        Assert.Equal("5000", s4.Action.Value); // 5 seconds = 5000ms

        // Validate plan structure is correct
        var exception = Record.Exception(() => plan.Validate());
        Assert.Null(exception);
    }

    [Fact]
    public async Task TaskPlanner_PlanAsync_NullLlm_FallsBackToHeuristics()
    {
        var planner = new TaskPlanner(null);
        var plan = await planner.PlanAsync("open google.com");

        Assert.Single(plan.Steps);
        Assert.Equal(ActionType.Navigate, plan.Steps["step_1"].Action.Type);
        Assert.Equal("https://google.com", plan.Steps["step_1"].Action.Value);
    }
}

