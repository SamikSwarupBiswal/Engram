using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Engram.Store.Automation;
using Xunit;
using ExecutionContext = Engram.Store.Automation.ExecutionContext;

namespace Engram.Store.Tests;

public class MockEmbodimentTests
{
    [Fact]
    public async Task ActionRuntime_UsesProvidedEmbodimentProvider()
    {
        var context = new ExecutionContext();
        var mockProvider = new MockUiProvider();
        context.SetVariable("UiEmbodimentProvider", mockProvider);

        var executor = new ActionExecutor();
        var gate = new PermissionGate();
        var safety = new ExecutionSafetyManager();
        var trustManager = new TrustTierManager(TrustTier.Privileged);

        using var runtime = new ActionRuntime(executor, gate, safety, trustManager);

        var plan = new ExecutionPlan
        {
            Goal = "Test mock provider",
            PlanId = "test-mock-plan"
        };
        var action = new AutomationAction
        {
            Type = ActionType.Navigate,
            Value = "https://example.com",
            Description = "Test navigation via mock",
            Permission = ActionPermission.Approved
        };
        plan.Steps["step1"] = new ExecutionStep
        {
            Id = "step1",
            Action = action,
            Status = StepStatus.Pending
        };

        await runtime.ExecutePlanAsync(plan, context, CancellationToken.None);

        Assert.Single(mockProvider.ExecutedActions);
        Assert.Equal("https://example.com", mockProvider.ExecutedActions[0].Value);
        Assert.Equal(StepStatus.Completed, plan.Steps["step1"].Status);
    }

    [Fact]
    public async Task ActionRuntime_BlocksAction_IfTrustTierInsufficient()
    {
        var context = new ExecutionContext();
        var mockProvider = new MockUiProvider();
        context.SetVariable("UiEmbodimentProvider", mockProvider);

        var executor = new ActionExecutor();
        var gate = new PermissionGate();
        var safety = new ExecutionSafetyManager();
        
        // Start in Observe tier, which blocks Click action
        var trustManager = new TrustTierManager(TrustTier.Observe);

        using var runtime = new ActionRuntime(executor, gate, safety, trustManager);

        var plan = new ExecutionPlan
        {
            Goal = "Test blocked action",
            PlanId = "test-blocked-plan"
        };
        var action = new AutomationAction
        {
            Type = ActionType.Click,
            Description = "Dangerous Click",
            Permission = ActionPermission.Approved
        };
        plan.Steps["step1"] = new ExecutionStep
        {
            Id = "step1",
            Action = action,
            Status = StepStatus.Pending
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => 
            runtime.ExecutePlanAsync(plan, context, CancellationToken.None));

        Assert.Contains("Action blocked", exception.Message);
        Assert.Equal(StepStatus.Failed, plan.Steps["step1"].Status);
        Assert.Empty(mockProvider.ExecutedActions); // Action never reached the provider
    }
}
