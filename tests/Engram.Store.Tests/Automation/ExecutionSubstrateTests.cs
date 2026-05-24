using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Engram.Store.Automation;

namespace Engram.Store.Tests.Automation;

public class ExecutionSubstrateTests
{
    [Fact]
    public void StateMachine_Transitions_ShouldBeStrict()
    {
        var machine = new ExecutionStateMachine();
        Assert.Equal(WorkflowState.Pending, machine.CurrentState);

        // Valid transition
        machine.TransitionTo(WorkflowState.AcquiringTarget);
        Assert.Equal(WorkflowState.AcquiringTarget, machine.CurrentState);

        // Invalid transition
        Assert.Throws<InvalidOperationException>(() => machine.TransitionTo(WorkflowState.Completed));
    }

    [Fact]
    public void StateMachine_RealityUncertain_Transitions_ShouldBeControlled()
    {
        var machine = new ExecutionStateMachine();
        machine.TransitionTo(WorkflowState.AcquiringTarget);
        
        // Target acquisition fails to verify -> RealityUncertain
        machine.TransitionTo(WorkflowState.RealityUncertain);
        Assert.Equal(WorkflowState.RealityUncertain, machine.CurrentState);

        // Can only transition to Suspended or FailedSafe
        Assert.True(machine.CanTransitionTo(WorkflowState.FailedSafe));
        Assert.True(machine.CanTransitionTo(WorkflowState.Suspended));
        Assert.False(machine.CanTransitionTo(WorkflowState.Completed));

        machine.TransitionTo(WorkflowState.FailedSafe);
    }

    [Fact]
    public async Task FocusOwnershipManager_ShouldIdentifyFocusAndOverlays()
    {
        var uiMock = new MockUiProvider
        {
            MockProcessName = "chrome.exe",
            MockWindowTitle = "Google Chrome"
        };
        var manager = new FocusOwnershipManager(uiMock);

        Assert.True(await manager.VerifyFocusAsync("chrome"));
        Assert.False(await manager.VerifyFocusAsync("notepad"));

        uiMock.MockProcessName = "ShellExperienceHost";
        uiMock.MockWindowTitle = "Notification Center";
        Assert.True(await manager.DetectOverlayOrOcclusionAsync());
    }

    [Fact]
    public async Task InputStabilityGuard_ShouldBlockOnLayoutChangeAndUserActivity()
    {
        var monitor = new SovereigntyMonitor(2000, () => 100); // 100ms idle -> user active
        var guard = new InputStabilityGuard(monitor);

        // User active -> Unstable
        Assert.False(await guard.IsInputStableAsync());

        // Idle user
        var quietMonitor = new SovereigntyMonitor(2000, () => 5000); // 5s idle -> user inactive
        var guard2 = new InputStabilityGuard(quietMonitor);
        Assert.True(await guard2.IsInputStableAsync());

        // Layout change cooldown
        guard2.RegisterLayoutChange();
        Assert.False(await guard2.IsInputStableAsync());
    }

    [Fact]
    public void InteractionDebounceEngine_ShouldFilterDuplicateActions()
    {
        var engine = new InteractionDebounceEngine(TimeSpan.FromMilliseconds(200));
        var action = new AutomationAction
        {
            Type = ActionType.Click,
            Target = new ActionTarget { Selector = "#submit-button" }
        };

        // First execution: not debounced
        Assert.False(engine.RecordActionAndCheckDebounce(action));

        // Rapid second execution: debounced
        Assert.True(engine.RecordActionAndCheckDebounce(action));
    }
}
