using System;
using System.Threading.Tasks;
using Xunit;
using Engram.Store.Automation;

namespace Engram.Store.Tests.Automation;

public class TransactionalMutationTests
{
    [Fact]
    public void TransactionalWorkflowEnvelope_ShouldTrackBoundaries()
    {
        var envelope = new TransactionalWorkflowEnvelope();
        var wfId = "workflow_abc";

        envelope.BeginTransaction(wfId, "step_anchor_0");
        envelope.RecordStep(wfId, "step_1");
        envelope.RecordStep(wfId, "step_2");

        Assert.Equal("step_anchor_0", envelope.GetRollbackAnchor(wfId));
        Assert.Equal(2, envelope.GetTransactionSteps(wfId).Count);
        Assert.False(envelope.IsCommitted(wfId));

        envelope.CommitTransaction(wfId);
        Assert.True(envelope.IsCommitted(wfId));
    }

    [Fact]
    public void ExternalPropagationTracker_ShouldIndexOutwardEffects()
    {
        var tracker = new ExternalPropagationTracker();
        var wfId = "workflow_xyz";

        tracker.TrackPropagation(wfId, "step_3", "Email", "test@example.com");
        tracker.TrackPropagation(wfId, "step_4", "API", "https://api.github.com");

        var props = tracker.GetPropagations(wfId);
        Assert.Equal(2, props.Count);
        Assert.Equal("Email", props[0].DestinationType);
        Assert.Equal("API", props[1].DestinationType);
    }

    [Fact]
    public async Task ExternalImpactGate_ShouldBlockOutboundActions()
    {
        var gate = new ExternalImpactGate();

        // Safe action: scrolling or wait
        var safeAction = new AutomationAction
        {
            Type = ActionType.Scroll,
            Description = "Scroll down the readme"
        };
        Assert.True(await gate.ValidateActionSafetyAsync(safeAction));

        // Unsafe actions: financial or deletes
        var deleteAction = new AutomationAction
        {
            Type = ActionType.Click,
            Description = "Delete temporary file folder"
        };
        Assert.False(await gate.ValidateActionSafetyAsync(deleteAction));

        var sendAction = new AutomationAction
        {
            Type = ActionType.Click,
            Description = "Submit draft invoice payment"
        };
        Assert.False(await gate.ValidateActionSafetyAsync(sendAction));
    }

    [Fact]
    public void TemporalExecutionDegradationModel_ShouldApplyTimeDecay()
    {
        var model = new TemporalExecutionDegradationModel();

        // Initial/fast execution
        double factor1 = model.ComputeTemporalDecayFactor("wf", TimeSpan.FromSeconds(30), 1);
        Assert.True(factor1 > 0.95);

        // Extended execution: 30 minutes, 15 steps
        double factor2 = model.ComputeTemporalDecayFactor("wf", TimeSpan.FromMinutes(30), 15);
        Assert.True(factor2 < 0.95);
        Assert.True(factor2 > 0.80);
    }
}
