using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Automation;

/// <summary>
/// Manages long-running workflows, checkpointing, and state restoration.
/// </summary>
public class WorkflowRuntime
{
    private readonly WorkflowPersistenceStore _persistenceStore;
    private readonly ActionRuntime _actionRuntime;
    private readonly OperationalWorldModel _worldModel;
    private readonly ILogger<WorkflowRuntime>? _logger;

    private string _activeWorkflowId = string.Empty;
    private ExecutionPlan? _activePlan;
    private ExecutionContext? _activeContext;

    public string ActiveWorkflowId => _activeWorkflowId;
    public ExecutionPlan? ActivePlan => _activePlan;
    public ExecutionContext? ActiveContext => _activeContext;

    public WorkflowRuntime(
        WorkflowPersistenceStore persistenceStore,
        ActionRuntime actionRuntime,
        OperationalWorldModel worldModel,
        ILogger<WorkflowRuntime>? logger = null)
    {
        _persistenceStore = persistenceStore ?? throw new ArgumentNullException(nameof(persistenceStore));
        _actionRuntime = actionRuntime ?? throw new ArgumentNullException(nameof(actionRuntime));
        _worldModel = worldModel ?? throw new ArgumentNullException(nameof(worldModel));
        _logger = logger;
    }

    public async Task StartWorkflowAsync(string workflowId, ExecutionPlan plan, ExecutionContext context, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(workflowId)) throw new ArgumentException("Workflow ID cannot be empty", nameof(workflowId));
        if (plan == null) throw new ArgumentNullException(nameof(plan));
        if (context == null) throw new ArgumentNullException(nameof(context));

        _activeWorkflowId = workflowId;
        _activePlan = plan;
        _activeContext = context;

        _actionRuntime.IdentityEnvelope = new WorkflowIdentityEnvelope
        {
            WorkflowId = workflowId,
            IntentHistory = new List<string> { plan.Goal }
        };

        _worldModel.ActiveWorkflow = workflowId;
        _worldModel.CurrentPhase = "ExecutingSteps";
        _worldModel.ExecutionConfidence = 0.95;

        _logger?.LogInformation("Starting workflow '{WorkflowId}' for goal: {Goal}", workflowId, plan.Goal);

        // Save initial checkpoint
        await CreateCheckpointAsync("Initial start");

        try
        {
            await _actionRuntime.ExecutePlanAsync(plan, context, ct);
            
            _worldModel.CurrentPhase = "Completed";
            _worldModel.ExecutionConfidence = 1.0;
            _logger?.LogInformation("Workflow '{WorkflowId}' completed successfully.", workflowId);
            
            // Delete checkpoint on successful completion to clean up
            _persistenceStore.DeleteCheckpoint(workflowId);
        }
        catch (Exception ex)
        {
            if (_worldModel.CurrentPhase == "Paused" || (ex is OperationCanceledException && _worldModel.CurrentPhase == "Paused"))
            {
                _logger?.LogInformation("Workflow '{WorkflowId}' was paused. Propagating cancellation.", workflowId);
                throw;
            }

            _worldModel.CurrentPhase = "Failed";
            _worldModel.ExecutionConfidence = 0.0;
            _logger?.LogError(ex, "Workflow '{WorkflowId}' encountered an error. Saving failure checkpoint.", workflowId);
            
            await CreateCheckpointAsync($"Failed: {ex.Message}");
            throw;
        }
    }

    public async Task PauseWorkflowAsync()
    {
        if (string.IsNullOrEmpty(_activeWorkflowId))
        {
            _logger?.LogWarning("No active workflow to pause.");
            return;
        }

        _actionRuntime.Pause();
        _worldModel.CurrentPhase = "Paused";
        _logger?.LogInformation("Workflow '{WorkflowId}' paused.", _activeWorkflowId);

        await CreateCheckpointAsync("Paused by user");
    }

    public async Task ResumeWorkflowAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_activeWorkflowId) || _activePlan == null || _activeContext == null)
        {
            throw new InvalidOperationException("No active workflow to resume.");
        }

        _actionRuntime.Resume();
        _worldModel.CurrentPhase = "Resumed";
        _logger?.LogInformation("Workflow '{WorkflowId}' resumed.", _activeWorkflowId);

        try
        {
            await _actionRuntime.ExecutePlanAsync(_activePlan, _activeContext, ct);
            _worldModel.CurrentPhase = "Completed";
            _persistenceStore.DeleteCheckpoint(_activeWorkflowId);
        }
        catch (Exception ex)
        {
            if (_worldModel.CurrentPhase == "Paused" || (ex is OperationCanceledException && _worldModel.CurrentPhase == "Paused"))
            {
                _logger?.LogInformation("Workflow '{WorkflowId}' was paused. Propagating cancellation.", _activeWorkflowId);
                throw;
            }

            _worldModel.CurrentPhase = "Failed";
            await CreateCheckpointAsync($"Failed after resume: {ex.Message}");
            throw;
        }
    }

    public async Task RestoreWorkflowAsync(string workflowId, ExecutionContext context, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(workflowId)) throw new ArgumentException("Workflow ID cannot be empty", nameof(workflowId));
        if (context == null) throw new ArgumentNullException(nameof(context));

        _logger?.LogInformation("Restoring workflow '{WorkflowId}' from checkpoint...", workflowId);

        var checkpoint = await _persistenceStore.LoadCheckpointAsync(workflowId);
        if (checkpoint == null)
        {
            throw new KeyNotFoundException($"No checkpoint found for workflow '{workflowId}'");
        }

        // Restore context variables
        foreach (var kvp in checkpoint.Variables)
        {
            context.SetVariable(kvp.Key, kvp.Value);
        }

        // Reconstruct plan
        if (string.IsNullOrEmpty(checkpoint.PlanJson))
        {
            throw new InvalidOperationException("Checkpoint contains no execution plan data");
        }

        var planDto = JsonSerializer.Deserialize<ExecutionPlanDto>(checkpoint.PlanJson);
        if (planDto == null)
        {
            throw new InvalidOperationException("Failed to deserialize execution plan from checkpoint");
        }

        var plan = new ExecutionPlan
        {
            PlanId = planDto.PlanId,
            Goal = planDto.Goal
        };

        foreach (var stepDto in planDto.Steps)
        {
            var actionType = Enum.TryParse<ActionType>(stepDto.ActionType, out var type) ? type : ActionType.Wait;
            var step = new ExecutionStep
            {
                Id = stepDto.Id,
                DependsOn = stepDto.DependsOn ?? new List<string>(),
                Status = Enum.TryParse<StepStatus>(stepDto.Status, out var stat) ? stat : StepStatus.Pending,
                Error = stepDto.Error,
                Action = new AutomationAction
                {
                    ActionId = stepDto.ActionId,
                    Type = actionType,
                    Description = stepDto.Description,
                    Value = stepDto.Value,
                    Result = stepDto.Result,
                    Permission = Enum.TryParse<ActionPermission>(stepDto.Permission, out var perm) ? perm : ActionPermission.Pending,
                    Target = stepDto.TargetSelector != null ? new ActionTarget
                    {
                        Selector = stepDto.TargetSelector,
                        X = stepDto.TargetX,
                        Y = stepDto.TargetY
                    } : null
                }
            };

            plan.Steps[step.Id] = step;
        }

        _activeWorkflowId = workflowId;
        _activePlan = plan;
        _activeContext = context;

        _actionRuntime.IdentityEnvelope = checkpoint.IdentityEnvelope ?? new WorkflowIdentityEnvelope { WorkflowId = workflowId };

        // ── Recovery Reconciliation Protocol ──
        _logger?.LogInformation("Executing Recovery Reconciliation Protocol for workflow '{WorkflowId}'...", workflowId);
        await ReconcileSuspendedStateAsync(context, ct);

        _worldModel.ActiveWorkflow = workflowId;
        _worldModel.CurrentPhase = "Restored";
        _worldModel.ExecutionConfidence = 0.85;

        _logger?.LogInformation("Workflow '{WorkflowId}' restored. Resuming execution...", workflowId);
        await ResumeWorkflowAsync(ct);
    }

    private async Task ReconcileSuspendedStateAsync(ExecutionContext context, CancellationToken ct)
    {
        // 1. Re-verify environment preconditions
        if (_actionRuntime.SynchronizationEngine != null)
        {
            var syncReport = _actionRuntime.SynchronizationEngine.CheckSynchronization(context);
            if (!syncReport.IsSynchronized)
            {
                var interpreter = new EnvironmentalDivergenceInterpreter();
                foreach (var div in syncReport.Divergences)
                {
                    var interpretation = interpreter.Interpret(div, context);
                    if (interpretation == DivergenceInterpretation.Hostile || interpretation == DivergenceInterpretation.Propagation)
                    {
                        throw new InvalidOperationException($"Reconciliation failed: Unresolved {interpretation} divergence. Expected '{div.Expected}' (Actual: '{div.Actual}').");
                    }
                }
            }
        }

        // 2. Reassess uncertainty lineage
        var envelope = _actionRuntime.IdentityEnvelope;
        if (envelope != null)
        {
            _worldModel.ExecutionConfidence = 0.90; // Start with decent confidence after clean reconciliation
        }

        // 3. Reconcile propagation state
        if (_actionRuntime.PropagationLedger != null)
        {
            var unresolved = _actionRuntime.PropagationLedger.GetRecords();
            foreach (var record in unresolved)
            {
                if (record.Status == "Uncertain" || record.Status == "Failed")
                {
                    string filePath = record.DestinationValue;
                    if (!string.IsNullOrEmpty(filePath) && System.IO.File.Exists(filePath))
                    {
                        _actionRuntime.PropagationLedger.UpdateStatus(record.PropagationId, "Propagated", "Reconciled via filesystem check.");
                    }
                }
            }
        }

        _logger?.LogInformation("Recovery Reconciliation Protocol successfully completed.");
        await Task.CompletedTask;
    }

    public async Task CreateCheckpointAsync(string reason)
    {
        if (string.IsNullOrEmpty(_activeWorkflowId) || _activePlan == null || _activeContext == null)
        {
            return;
        }

        var executedSteps = new List<string>();
        int currentIdx = 0;
        string activeStepId = string.Empty;

        var planDto = new ExecutionPlanDto
        {
            PlanId = _activePlan.PlanId,
            Goal = _activePlan.Goal,
            Steps = new List<ExecutionStepDto>()
        };

        var topologicalSteps = _actionRuntime.GetTopologicalOrder(_activePlan);
        for (int i = 0; i < topologicalSteps.Count; i++)
        {
            var step = topologicalSteps[i];
            
            if (step.Status == StepStatus.Completed)
            {
                executedSteps.Add(step.Id);
            }
            else if (step.Status == StepStatus.Executing)
            {
                activeStepId = step.Id;
                currentIdx = i;
            }

            planDto.Steps.Add(new ExecutionStepDto
            {
                Id = step.Id,
                ActionId = step.Action.ActionId,
                ActionType = step.Action.Type.ToString(),
                Description = step.Action.Description,
                Value = step.Action.Value,
                Result = step.Action.Result,
                Status = step.Status.ToString(),
                Permission = step.Action.Permission.ToString(),
                DependsOn = step.DependsOn,
                TargetSelector = step.Action.Target?.Selector,
                TargetX = step.Action.Target?.X,
                TargetY = step.Action.Target?.Y,
                Error = step.Error
            });
        }

        var variablesMap = new Dictionary<string, string>();
        foreach (var kvp in _activeContext.Variables)
        {
            if (kvp.Value is string or int or double or float or decimal or bool)
            {
                variablesMap[kvp.Key] = kvp.Value.ToString() ?? string.Empty;
            }
        }

        var checkpoint = new WorkflowCheckpoint
        {
            WorkflowId = _activeWorkflowId,
            Goal = _activePlan.Goal,
            CurrentPhase = _worldModel.CurrentPhase,
            CurrentStepIndex = currentIdx,
            ActiveStepId = activeStepId,
            Variables = variablesMap,
            ExecutedStepIds = executedSteps,
            PlanJson = JsonSerializer.Serialize(planDto),
            IdentityEnvelope = _actionRuntime.IdentityEnvelope,
            CheckpointTime = DateTimeOffset.UtcNow
        };

        await _persistenceStore.SaveCheckpointAsync(checkpoint);
        _logger?.LogDebug("Checkpoint created for workflow '{WorkflowId}'. Reason: {Reason}", _activeWorkflowId, reason);
    }

    private class ExecutionPlanDto
    {
        public string PlanId { get; set; } = string.Empty;
        public string Goal { get; set; } = string.Empty;
        public List<ExecutionStepDto> Steps { get; set; } = new();
    }

    private class ExecutionStepDto
    {
        public string Id { get; set; } = string.Empty;
        public string ActionId { get; set; } = string.Empty;
        public string ActionType { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? Value { get; set; }
        public string? Result { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Permission { get; set; } = string.Empty;
        public List<string>? DependsOn { get; set; }
        public string? TargetSelector { get; set; }
        public int? TargetX { get; set; }
        public int? TargetY { get; set; }
        public string? Error { get; set; }
    }
}
