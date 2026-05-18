using Engram.Store.Automation;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Industrial-level tests for the Automation system.
/// Tests permission gate, action executor, and plan lifecycle.
/// </summary>
public class AutomationTests : IDisposable
{
    public AutomationTests() { }
    public void Dispose() { }

    // ─── Action Model ───

    [Fact]
    public void Action_DefaultValues()
    {
        var action = new AutomationAction();
        Assert.NotEmpty(action.ActionId);
        Assert.Equal(ActionPermission.Pending, action.Permission);
        Assert.Equal(ActionStatus.Pending, action.Status);
        Assert.Null(action.Result);
        Assert.Null(action.Error);
    }

    [Fact]
    public void Action_WithType_PreservesValue()
    {
        var action = new AutomationAction { Type = ActionType.Click };
        Assert.Equal(ActionType.Click, action.Type);
    }

    [Fact]
    public void ActionTarget_WithSelector_PreservesValue()
    {
        var target = new ActionTarget { Selector = "#submit-btn" };
        Assert.Equal("#submit-btn", target.Selector);
    }

    [Fact]
    public void ActionTarget_WithCoordinates_PreservesValues()
    {
        var target = new ActionTarget { X = 100, Y = 200 };
        Assert.Equal(100, target.X);
        Assert.Equal(200, target.Y);
    }

    // ─── Action Plan ───

    [Fact]
    public void Plan_DefaultValues()
    {
        var plan = new ActionPlan();
        Assert.NotEmpty(plan.PlanId);
        Assert.Equal(ActionPlanStatus.Draft, plan.Status);
        Assert.Empty(plan.Actions);
        Assert.Equal(0, plan.Progress);
    }

    [Fact]
    public void Plan_Progress_CalculatesCorrectly()
    {
        var plan = new ActionPlan
        {
            Actions = new List<AutomationAction>
            {
                new() { Type = ActionType.Navigate },
                new() { Type = ActionType.Click },
                new() { Type = ActionType.Type },
                new() { Type = ActionType.Screenshot }
            },
            CurrentActionIndex = 2
        };
        Assert.Equal(50, plan.Progress);
    }

    [Fact]
    public void Plan_Progress_EmptyActions_ReturnsZero()
    {
        var plan = new ActionPlan { CurrentActionIndex = 5 };
        Assert.Equal(0, plan.Progress);
    }

    // ─── Permission Gate ───

    [Fact]
    public void Gate_Screenshot_AutoApproved()
    {
        var gate = new PermissionGate();
        var action = new AutomationAction { Type = ActionType.Screenshot };
        Assert.Equal(ActionPermission.AutoApproved, gate.CheckPermission(action));
    }

    [Fact]
    public void Gate_Wait_AutoApproved()
    {
        var gate = new PermissionGate();
        var action = new AutomationAction { Type = ActionType.Wait };
        Assert.Equal(ActionPermission.AutoApproved, gate.CheckPermission(action));
    }

    [Fact]
    public void Gate_Click_RequiresApproval()
    {
        var gate = new PermissionGate();
        var action = new AutomationAction { Type = ActionType.Click };
        Assert.Equal(ActionPermission.Pending, gate.CheckPermission(action));
    }

    [Fact]
    public void Gate_Navigate_RequiresApproval()
    {
        var gate = new PermissionGate();
        var action = new AutomationAction { Type = ActionType.Navigate };
        Assert.Equal(ActionPermission.Pending, gate.CheckPermission(action));
    }

    [Fact]
    public void Gate_Type_RequiresApproval()
    {
        var gate = new PermissionGate();
        var action = new AutomationAction { Type = ActionType.Type };
        Assert.Equal(ActionPermission.Pending, gate.CheckPermission(action));
    }

    [Fact]
    public void Gate_Approve_PendingAction_Succeeds()
    {
        var gate = new PermissionGate();
        var action = new AutomationAction { Type = ActionType.Click };
        Assert.True(gate.Approve(action));
        Assert.Equal(ActionPermission.Approved, action.Permission);
    }

    [Fact]
    public void Gate_Approve_AlreadyApproved_ReturnsFalse()
    {
        var gate = new PermissionGate();
        var action = new AutomationAction { Type = ActionType.Click, Permission = ActionPermission.Approved };
        Assert.False(gate.Approve(action));
    }

    [Fact]
    public void Gate_Deny_PendingAction_Succeeds()
    {
        var gate = new PermissionGate();
        var action = new AutomationAction { Type = ActionType.Click };
        Assert.True(gate.Deny(action));
        Assert.Equal(ActionPermission.Denied, action.Permission);
        Assert.Equal(ActionStatus.Denied, action.Status);
    }

    [Fact]
    public void Gate_Deny_AlreadyDenied_ReturnsFalse()
    {
        var gate = new PermissionGate();
        var action = new AutomationAction { Type = ActionType.Click, Permission = ActionPermission.Denied };
        Assert.False(gate.Deny(action));
    }

    [Fact]
    public void Gate_ApproveAll_ApprovesAllPending()
    {
        var gate = new PermissionGate();
        var plan = new ActionPlan
        {
            Actions = new List<AutomationAction>
            {
                new() { Type = ActionType.Click },
                new() { Type = ActionType.Type },
                new() { Type = ActionType.Screenshot, Permission = ActionPermission.AutoApproved }
            }
        };
        var count = gate.ApproveAll(plan);
        Assert.Equal(2, count); // Only pending ones
        Assert.Equal(ActionPermission.Approved, plan.Actions[0].Permission);
        Assert.Equal(ActionPermission.Approved, plan.Actions[1].Permission);
        Assert.Equal(ActionPermission.AutoApproved, plan.Actions[2].Permission); // Unchanged
    }

    [Fact]
    public void Gate_DenyAll_DeniesAllPending()
    {
        var gate = new PermissionGate();
        var plan = new ActionPlan
        {
            Actions = new List<AutomationAction>
            {
                new() { Type = ActionType.Click },
                new() { Type = ActionType.Type },
                new() { Type = ActionType.Screenshot, Permission = ActionPermission.AutoApproved }
            }
        };
        var count = gate.DenyAll(plan);
        Assert.Equal(2, count);
        Assert.Equal(ActionStatus.Denied, plan.Actions[0].Status);
    }

    // ─── Action Executor ───

    [Fact]
    public async Task Executor_ExecuteApprovedAction_Succeeds()
    {
        var executor = new ActionExecutor();
        var action = new AutomationAction
        {
            Type = ActionType.Wait,
            Permission = ActionPermission.AutoApproved,
            Value = "100"
        };
        var result = await executor.ExecuteAsync(action);
        Assert.Contains("100", result);
        Assert.Equal(ActionStatus.Completed, action.Status);
    }

    [Fact]
    public async Task Executor_ExecuteUnapprovedAction_Throws()
    {
        var executor = new ActionExecutor();
        var action = new AutomationAction { Type = ActionType.Click, Permission = ActionPermission.Pending };
        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(action));
    }

    [Fact]
    public async Task Executor_ExecuteDeniedAction_Throws()
    {
        var executor = new ActionExecutor();
        var action = new AutomationAction { Type = ActionType.Click, Permission = ActionPermission.Denied };
        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(action));
    }

    [Fact]
    public async Task Executor_Navigate_ReturnsResult()
    {
        var executor = new ActionExecutor();
        var action = new AutomationAction
        {
            Type = ActionType.Navigate,
            Permission = ActionPermission.Approved,
            Value = "https://example.com"
        };
        var result = await executor.ExecuteAsync(action);
        Assert.Contains("example.com", result);
    }

    [Fact]
    public async Task Executor_Click_ReturnsResult()
    {
        var executor = new ActionExecutor();
        var action = new AutomationAction
        {
            Type = ActionType.Click,
            Permission = ActionPermission.Approved,
            Target = new ActionTarget { Selector = "#btn" }
        };
        var result = await executor.ExecuteAsync(action);
        Assert.Contains("#btn", result);
    }

    [Fact]
    public async Task Executor_Type_ReturnsResult()
    {
        var executor = new ActionExecutor();
        var action = new AutomationAction
        {
            Type = ActionType.Type,
            Permission = ActionPermission.Approved,
            Target = new ActionTarget { Selector = "#input" },
            Value = "hello"
        };
        var result = await executor.ExecuteAsync(action);
        Assert.Contains("hello", result);
    }

    [Fact]
    public async Task Executor_KeepsLog()
    {
        var executor = new ActionExecutor();
        var action = new AutomationAction
        {
            Type = ActionType.Wait,
            Permission = ActionPermission.AutoApproved,
            Value = "10"
        };
        await executor.ExecuteAsync(action);

        var log = executor.GetLog();
        Assert.Single(log);
        Assert.Equal(ActionType.Wait, log[0].Type);
        Assert.Equal(ActionStatus.Completed, log[0].Status);
    }

    [Fact]
    public async Task Executor_FailedAction_LoggedWithError()
    {
        var executor = new ActionExecutor();
        var action = new AutomationAction
        {
            Type = ActionType.Click,
            Permission = ActionPermission.Approved
            // No target → will throw
        };

        try { await executor.ExecuteAsync(action); } catch { }

        var log = executor.GetLog();
        Assert.Single(log);
        Assert.Equal(ActionStatus.Failed, log[0].Status);
        Assert.NotNull(log[0].Error);
    }

    [Fact]
    public async Task Executor_ExecutePlan_AllApproved()
    {
        var executor = new ActionExecutor();
        var plan = new ActionPlan
        {
            Actions = new List<AutomationAction>
            {
                new() { Type = ActionType.Wait, Permission = ActionPermission.AutoApproved, Value = "10" },
                new() { Type = ActionType.Wait, Permission = ActionPermission.AutoApproved, Value = "10" },
                new() { Type = ActionType.Screenshot, Permission = ActionPermission.AutoApproved }
            }
        };

        await executor.ExecutePlanAsync(plan);
        Assert.Equal(ActionPlanStatus.Completed, plan.Status);
    }

    [Fact]
    public async Task Executor_ExecutePlan_SkipsDenied()
    {
        var executor = new ActionExecutor();
        var plan = new ActionPlan
        {
            Actions = new List<AutomationAction>
            {
                new() { Type = ActionType.Wait, Permission = ActionPermission.AutoApproved, Value = "10" },
                new() { Type = ActionType.Click, Permission = ActionPermission.Denied },
                new() { Type = ActionType.Wait, Permission = ActionPermission.AutoApproved, Value = "10" }
            }
        };

        await executor.ExecutePlanAsync(plan);
        Assert.Equal(ActionPlanStatus.Completed, plan.Status);
        Assert.Equal(ActionStatus.Denied, plan.Actions[1].Status);
    }

    [Fact]
    public void Executor_Rollback_MarksCompletedAsRolledBack()
    {
        var executor = new ActionExecutor();
        var plan = new ActionPlan
        {
            Actions = new List<AutomationAction>
            {
                new() { Type = ActionType.Wait, Status = ActionStatus.Completed },
                new() { Type = ActionType.Click, Status = ActionStatus.Completed },
                new() { Type = ActionType.Type, Status = ActionStatus.Completed }
            }
        };

        var count = executor.Rollback(plan, 2);
        Assert.Equal(2, count);
        Assert.Equal(ActionStatus.Completed, plan.Actions[0].Status);
        Assert.Equal(ActionStatus.RolledBack, plan.Actions[1].Status);
        Assert.Equal(ActionStatus.RolledBack, plan.Actions[2].Status);
    }

    [Fact]
    public void Executor_Rollback_NoCompleted_ReturnsZero()
    {
        var executor = new ActionExecutor();
        var plan = new ActionPlan
        {
            Actions = new List<AutomationAction>
            {
                new() { Type = ActionType.Wait, Status = ActionStatus.Pending }
            }
        };
        Assert.Equal(0, executor.Rollback(plan));
    }

    // ─── Enums ───

    [Fact]
    public void ActionType_HasAllExpectedValues()
    {
        Assert.Equal(10, Enum.GetValues<ActionType>().Length);
    }

    [Fact]
    public void ActionPermission_HasAllExpectedValues()
    {
        Assert.Equal(4, Enum.GetValues<ActionPermission>().Length);
    }

    [Fact]
    public void ActionStatus_HasAllExpectedValues()
    {
        Assert.Equal(6, Enum.GetValues<ActionStatus>().Length);
    }

    [Fact]
    public void ActionPlanStatus_HasAllExpectedValues()
    {
        Assert.Equal(6, Enum.GetValues<ActionPlanStatus>().Length);
    }

    // ─── Edge Cases ───

    [Fact]
    public void Action_UnicodeDescription_PreservesValue()
    {
        var action = new AutomationAction { Description = "日本語テスト Über Café" };
        Assert.Contains("日本語", action.Description);
    }

    [Fact]
    public void Plan_ManyActions_ProgressCalculation()
    {
        var plan = new ActionPlan
        {
            Actions = Enumerable.Range(0, 100).Select(_ => new AutomationAction { Type = ActionType.Wait }).ToList(),
            CurrentActionIndex = 50
        };
        Assert.Equal(50, plan.Progress);
    }

    [Fact]
    public void LogEntry_DefaultValues()
    {
        var entry = new ActionLogEntry();
        Assert.NotEmpty(entry.LogId);
        Assert.Equal(string.Empty, entry.ActionId);
    }
}
