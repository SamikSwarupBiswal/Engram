using System;
using Engram.Store.Automation;
using Engram.Store.Events;
using Xunit;

using ExecutionContext = Engram.Store.Automation.ExecutionContext;

namespace Engram.Store.Tests;

public class EscalationPolicyEngineTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();
    private readonly InMemoryEventBus _eventBus = new();
    private readonly CollaborationEngine _collaborationEngine;
    private readonly WorkflowConfidenceEngine _confidenceEngine;

    public EscalationPolicyEngineTests()
    {
        _collaborationEngine = new CollaborationEngine(_eventBus);
        var telemetry = new ExecutionTelemetryEngine(_workspace.Root);
        var proceduralMemory = new ProceduralMemoryEngine(_workspace.Root);
        _confidenceEngine = new WorkflowConfidenceEngine(telemetry, proceduralMemory, _eventBus);
    }

    public void Dispose()
    {
        _workspace.Dispose();
    }

    [Fact]
    public void Evaluate_SafeAction_ContinuesAutonomously()
    {
        var engine = new EscalationPolicyEngine(_collaborationEngine, _confidenceEngine, _eventBus);
        var context = new EscalationContext
        {
            ActionType = "Browse",
            Description = "Summarize file contents",
            Reversibility = 1.0,
            ExternalImpact = 0.0,
            FinancialLegalImpact = 0.0,
            UserPreferenceHistoryScore = 1.0
        };

        var decision = engine.Evaluate("wf1", context);

        Assert.Equal(EscalationAction.ContinueAutonomously, decision.Action);
        Assert.False(decision.RequiresHumanResponse);
    }

    [Fact]
    public void Evaluate_UnsafeAction_RequestsApproval()
    {
        var engine = new EscalationPolicyEngine(_collaborationEngine, _confidenceEngine, _eventBus);
        
        // Deleting file
        var context = new EscalationContext
        {
            ActionType = "Delete",
            Description = "Delete build folder",
            Reversibility = 0.2, // low reversibility
            ExternalImpact = 0.0,
            FinancialLegalImpact = 0.0,
            UserPreferenceHistoryScore = 1.0
        };

        bool eventFired = false;
        _eventBus.Subscribe("automation.escalation.triggered", env =>
        {
            eventFired = true;
            Assert.Contains("RequestApproval", env.Payload.ToString());
        });

        var decision = engine.Evaluate("wf1", context);

        Assert.Equal(EscalationAction.RequestApproval, decision.Action);
        Assert.True(decision.RequiresHumanResponse);
        Assert.True(eventFired);
    }

    [Fact]
    public void Evaluate_WithFinancialImpact_RequestsApproval()
    {
        var engine = new EscalationPolicyEngine(_collaborationEngine, _confidenceEngine, _eventBus);
        var context = new EscalationContext
        {
            ActionType = "Purchase",
            Description = "Buy API subscription",
            Reversibility = 1.0,
            ExternalImpact = 0.0,
            FinancialLegalImpact = 0.5, // high financial impact
            UserPreferenceHistoryScore = 1.0
        };

        var decision = engine.Evaluate("wf1", context);

        Assert.Equal(EscalationAction.RequestApproval, decision.Action);
        Assert.True(decision.RequiresHumanResponse);
        Assert.Equal("AbortWorkflow", decision.TimeoutAction);
    }

    [Fact]
    public void Evaluate_WithHighExternalImpact_RequestsApproval()
    {
        var engine = new EscalationPolicyEngine(_collaborationEngine, _confidenceEngine, _eventBus);
        var context = new EscalationContext
        {
            ActionType = "Email",
            Description = "Send status update email to stakeholders",
            Reversibility = 1.0,
            ExternalImpact = 0.8, // high external impact
            FinancialLegalImpact = 0.0,
            UserPreferenceHistoryScore = 1.0
        };

        var decision = engine.Evaluate("wf1", context);

        Assert.Equal(EscalationAction.RequestApproval, decision.Action);
        Assert.True(decision.RequiresHumanResponse);
        Assert.Equal("PauseAndWait", decision.TimeoutAction);
    }

    [Fact]
    public void Evaluate_WithLowConfidence_AsksClarification()
    {
        var engine = new EscalationPolicyEngine(_collaborationEngine, _confidenceEngine, _eventBus);
        var context = new EscalationContext
        {
            ActionType = "Browse",
            Description = "Search details",
            Reversibility = 1.0,
            ExternalImpact = 0.0,
            FinancialLegalImpact = 0.0,
            UserPreferenceHistoryScore = 1.0
        };

        // Create a failed plan step to lower confidence
        var plan = new ExecutionPlan();
        plan.Steps["s1"] = new ExecutionStep { Id = "s1", Status = StepStatus.Failed };
        plan.Steps["s2"] = new ExecutionStep { Id = "s2", Status = StepStatus.Failed };

        var execCtx = new ExecutionContext();

        var decision = engine.Evaluate("wf1", context, plan, execCtx);

        Assert.Equal(EscalationAction.AskClarification, decision.Action);
        Assert.True(decision.RequiresHumanResponse);
    }

    [Fact]
    public void Evaluate_WithLowUserPreferenceHistory_RequestsApproval()
    {
        var engine = new EscalationPolicyEngine(_collaborationEngine, _confidenceEngine, _eventBus);
        var context = new EscalationContext
        {
            ActionType = "Browse",
            Description = "Search details",
            Reversibility = 1.0,
            ExternalImpact = 0.0,
            FinancialLegalImpact = 0.0,
            UserPreferenceHistoryScore = 0.2 // User dislikes this automated action
        };

        var decision = engine.Evaluate("wf1", context);

        Assert.Equal(EscalationAction.RequestApproval, decision.Action);
        Assert.True(decision.RequiresHumanResponse);
    }
}
