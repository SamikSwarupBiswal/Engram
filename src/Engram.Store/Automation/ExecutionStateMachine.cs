using System;
using System.Collections.Generic;

namespace Engram.Store.Automation;

public enum WorkflowState
{
    Pending,
    AcquiringTarget,
    VerifyingEnvironment,
    Executing,
    VerifyingMutation,
    Recovering,
    Suspended,
    YieldedToHuman,
    RealityUncertain,
    RolledBack,
    FailedSafe,
    Completed
}

public class ExecutionStateMachine
{
    private WorkflowState _currentState = WorkflowState.Pending;
    public WorkflowState CurrentState => _currentState;

    private static readonly Dictionary<WorkflowState, HashSet<WorkflowState>> AllowedTransitions = new()
    {
        {
            WorkflowState.Pending, 
            new HashSet<WorkflowState> { WorkflowState.AcquiringTarget, WorkflowState.Suspended, WorkflowState.FailedSafe, WorkflowState.RealityUncertain }
        },
        {
            WorkflowState.AcquiringTarget, 
            new HashSet<WorkflowState> { WorkflowState.VerifyingEnvironment, WorkflowState.FailedSafe, WorkflowState.YieldedToHuman, WorkflowState.Suspended, WorkflowState.RealityUncertain, WorkflowState.Recovering }
        },
        {
            WorkflowState.VerifyingEnvironment, 
            new HashSet<WorkflowState> { WorkflowState.Executing, WorkflowState.FailedSafe, WorkflowState.YieldedToHuman, WorkflowState.Suspended, WorkflowState.RealityUncertain, WorkflowState.Recovering }
        },
        {
            WorkflowState.Executing, 
            new HashSet<WorkflowState> { WorkflowState.VerifyingMutation, WorkflowState.FailedSafe, WorkflowState.YieldedToHuman, WorkflowState.Suspended, WorkflowState.RealityUncertain, WorkflowState.Recovering }
        },
        {
            WorkflowState.VerifyingMutation, 
            new HashSet<WorkflowState> { WorkflowState.Completed, WorkflowState.Recovering, WorkflowState.FailedSafe, WorkflowState.YieldedToHuman, WorkflowState.Suspended, WorkflowState.RealityUncertain }
        },
        {
            WorkflowState.Recovering, 
            new HashSet<WorkflowState> { WorkflowState.Executing, WorkflowState.RolledBack, WorkflowState.FailedSafe, WorkflowState.Suspended, WorkflowState.RealityUncertain }
        },
        {
            WorkflowState.Suspended, 
            new HashSet<WorkflowState> { WorkflowState.Pending, WorkflowState.Executing, WorkflowState.FailedSafe, WorkflowState.RealityUncertain }
        },
        {
            WorkflowState.YieldedToHuman, 
            new HashSet<WorkflowState> { WorkflowState.Executing, WorkflowState.Suspended, WorkflowState.FailedSafe, WorkflowState.RealityUncertain }
        },
        {
            WorkflowState.RealityUncertain, 
            new HashSet<WorkflowState> { WorkflowState.Suspended, WorkflowState.FailedSafe }
        },
        {
            WorkflowState.RolledBack, 
            new HashSet<WorkflowState> { WorkflowState.FailedSafe }
        },
        {
            WorkflowState.FailedSafe, 
            new HashSet<WorkflowState>() // Terminal state
        },
        {
            WorkflowState.Completed, 
            new HashSet<WorkflowState>() // Terminal state
        }
    };

    public void TransitionTo(WorkflowState newState)
    {
        if (CanTransitionTo(newState))
        {
            _currentState = newState;
        }
        else
        {
            throw new InvalidOperationException($"Invalid state transition: Cannot transition from {_currentState} to {newState}.");
        }
    }

    public bool CanTransitionTo(WorkflowState newState)
    {
        if (_currentState == newState) return true;
        return AllowedTransitions.TryGetValue(_currentState, out var targets) && targets.Contains(newState);
    }

    public void ForceState(WorkflowState state)
    {
        _currentState = state;
    }
}
