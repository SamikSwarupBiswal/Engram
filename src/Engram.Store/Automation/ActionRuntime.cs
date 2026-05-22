using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Automation;

public enum RuntimeState
{
    Idle,
    Running,
    Paused,
    Aborted
}

public class ActionRuntime : IDisposable
{
    private readonly ActionExecutor _executor;
    private readonly PermissionGate _permissionGate;
    private readonly ExecutionSafetyManager _safetyManager;
    private readonly ILogger<ActionRuntime>? _logger;

    private RuntimeState _state = RuntimeState.Idle;
    private readonly ManualResetEventSlim _pauseEvent = new(true);
    private CancellationTokenSource? _runCts;
    private ExecutionPlan? _activePlan;
    private ExecutionContext? _activeContext;

    public RuntimeState State => _state;
    public ExecutionPlan? ActivePlan => _activePlan;
    public ExecutionContext? ActiveContext => _activeContext;

    public ActionRuntime(
        ActionExecutor executor, 
        PermissionGate permissionGate, 
        ExecutionSafetyManager? safetyManager = null,
        ILogger<ActionRuntime>? logger = null)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _permissionGate = permissionGate ?? throw new ArgumentNullException(nameof(permissionGate));
        _safetyManager = safetyManager ?? new ExecutionSafetyManager();
        _logger = logger;
    }

    public void Pause()
    {
        if (_state == RuntimeState.Running)
        {
            _state = RuntimeState.Paused;
            _pauseEvent.Reset();
            _runCts?.Cancel();
            _logger?.LogInformation("Execution plan paused.");
        }
    }

    public void Resume()
    {
        if (_state == RuntimeState.Paused)
        {
            _state = RuntimeState.Running;
            _pauseEvent.Set();
            _logger?.LogInformation("Execution plan resumed.");
        }
    }

    public void Abort()
    {
        _state = RuntimeState.Aborted;
        _pauseEvent.Set(); // Wake up if paused
        _runCts?.Cancel();
        _logger?.LogWarning("Execution plan aborted.");
    }

    public async Task ExecutePlanAsync(ExecutionPlan plan, ExecutionContext context, CancellationToken ct = default)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));
        if (context == null) throw new ArgumentNullException(nameof(context));

        _activePlan = plan;
        _activeContext = context;
        _runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var linkedToken = _runCts.Token;

        _state = RuntimeState.Running;
        _pauseEvent.Set();

        _logger?.LogInformation("Validating and executing execution plan {PlanId}: {Goal}", plan.PlanId, plan.Goal);
        plan.Validate();

        var order = GetTopologicalOrder(plan);
        var completedSteps = new List<ExecutionStep>();

        // Initialize safety failsafes
        _safetyManager.InitializeMouseFailsafe();

        try
        {
            for (int i = 0; i < order.Count; i++)
            {
                var step = order[i];

                if (step.Status == StepStatus.Completed)
                {
                    _logger?.LogInformation("Step '{StepId}' is already completed. Skipping.", step.Id);
                    completedSteps.Add(step);
                    continue;
                }

                // Check Pause state
                if (_state == RuntimeState.Paused)
                {
                    _logger?.LogInformation("Execution is paused. Exiting execution loop.");
                    throw new OperationCanceledException("Execution paused.");
                }

                if (linkedToken.IsCancellationRequested || _state == RuntimeState.Aborted)
                {
                    for (int j = i; j < order.Count; j++)
                    {
                        order[j].Status = StepStatus.Skipped;
                    }
                    linkedToken.ThrowIfCancellationRequested();
                }

                // ── SAFETY CHECKS ──
                _safetyManager.VerifyRateLimit();
                _safetyManager.VerifyMouseFailsafe();

                var desktopOp = context.GetVariable<IDesktopOperator>("DesktopOperator");
                if (desktopOp != null)
                {
                    var (proc, title) = await desktopOp.GetActiveWindowAsync(linkedToken);
                    _safetyManager.VerifyProcessSafety(proc, title);
                }

                if (step.Action.Type == ActionType.Navigate && !string.IsNullOrEmpty(step.Action.Value))
                {
                    _safetyManager.VerifyUrlSafety(step.Action.Value);
                }

                var browserAgent = context.GetVariable<BrowserAgentRuntime>("BrowserAgent");
                if (browserAgent != null)
                {
                    var url = await browserAgent.GetUrlAsync(linkedToken);
                    _safetyManager.VerifyUrlSafety(url);
                }

                // Variable substitution for action values and selectors
                var resolvedValue = SubstituteVariables(step.Action.Value, context);
                var resolvedSelector = step.Action.Target != null ? SubstituteVariables(step.Action.Target.Selector, context) : null;
                var resolvedText = step.Action.Target != null ? SubstituteVariables(step.Action.Target.Text, context) : null;

                var resolvedAction = new AutomationAction
                {
                    ActionId = step.Action.ActionId,
                    Type = step.Action.Type,
                    Description = step.Action.Description,
                    Permission = step.Action.Permission,
                    Status = step.Action.Status,
                    Value = resolvedValue,
                    Target = step.Action.Target != null ? new ActionTarget
                    {
                        Selector = resolvedSelector,
                        Text = resolvedText,
                        X = step.Action.Target.X,
                        Y = step.Action.Target.Y
                    } : null
                };

                // Track expected mouse coordinates if performing coordinate-based click
                if (resolvedAction.Type == ActionType.Click && resolvedAction.Target != null && resolvedAction.Target.X.HasValue && resolvedAction.Target.Y.HasValue)
                {
                    _safetyManager.UpdateExpectedMousePosition(resolvedAction.Target.X.Value, resolvedAction.Target.Y.Value);
                }

                // 1. Permission check
                if (resolvedAction.Permission != ActionPermission.Approved && resolvedAction.Permission != ActionPermission.AutoApproved)
                {
                    var gatePermission = _permissionGate.CheckPermission(resolvedAction);
                    if (gatePermission == ActionPermission.AutoApproved)
                    {
                        resolvedAction.Permission = ActionPermission.AutoApproved;
                        step.Action.Permission = ActionPermission.AutoApproved;
                    }
                    else
                    {
                        step.Status = StepStatus.Failed;
                        step.Error = $"Step action is not approved (status: {gatePermission})";
                        
                        // Mark remaining steps as Skipped
                        for (int j = i + 1; j < order.Count; j++)
                        {
                            order[j].Status = StepStatus.Skipped;
                        }

                        await RollbackCompletedStepsAsync(completedSteps, context, linkedToken);
                        throw new InvalidOperationException($"Step '{step.Id}' action is not approved (status: {gatePermission})");
                    }
                }

                step.Status = StepStatus.Executing;
                step.StartedAt = DateTimeOffset.UtcNow;

                try
                {
                    _logger?.LogInformation("Executing step '{StepId}': {Description}", step.Id, step.Action.Description);
                    
                    string result;
                    if (browserAgent != null && IsBrowserAction(resolvedAction.Type))
                    {
                        result = await ExecuteBrowserActionAsync(browserAgent, resolvedAction, linkedToken);
                    }
                    else if (desktopOp != null && IsDesktopAction(resolvedAction.Type))
                    {
                        result = await ExecuteDesktopActionAsync(desktopOp, resolvedAction, linkedToken);
                    }
                    else
                    {
                        result = await _executor.ExecuteAsync(resolvedAction, linkedToken);
                    }

                    step.Action.Status = resolvedAction.Status;
                    step.Action.Result = result;

                    context.SetVariable("last_result", step.Action.Result ?? string.Empty);
                    if (step.Action.Type == ActionType.Navigate && !string.IsNullOrEmpty(resolvedAction.Value))
                    {
                        context.SetVariable("current_url", resolvedAction.Value);
                    }

                    context.SetVariable($"step_{step.Id}_result", step.Action.Result ?? string.Empty);

                    if (step.Verifier != null)
                    {
                        _logger?.LogDebug("Verifying step '{StepId}'", step.Id);
                        bool verified = await step.Verifier.VerifyAsync(context, linkedToken);
                        if (!verified)
                        {
                            throw new InvalidOperationException($"Verification failed for step '{step.Id}'");
                        }
                    }

                    step.Status = StepStatus.Completed;
                    step.CompletedAt = DateTimeOffset.UtcNow;
                    completedSteps.Add(step);
                }
                catch (Exception ex)
                {
                    if (ex is OperationCanceledException && _state == RuntimeState.Paused)
                    {
                        step.Status = StepStatus.Pending;
                        throw;
                    }

                    bool recovered = false;
                    Exception? lastError = ex;

                    if (step.RecoveryPolicy != null)
                    {
                        try
                        {
                            _logger?.LogWarning(ex, "Step '{StepId}' failed. Executing recovery policy.", step.Id);
                            recovered = await step.RecoveryPolicy.RecoverAsync(context, ex, linkedToken);
                            if (recovered)
                            {
                                _logger?.LogInformation("Recovery policy succeeded. Retrying step '{StepId}' execution.", step.Id);
                                
                                // Re-resolve the action properties in case recovery policy modified step.Action (e.g. added target)
                                resolvedValue = SubstituteVariables(step.Action.Value, context);
                                resolvedSelector = step.Action.Target != null ? SubstituteVariables(step.Action.Target.Selector, context) : null;
                                resolvedText = step.Action.Target != null ? SubstituteVariables(step.Action.Target.Text, context) : null;

                                resolvedAction = new AutomationAction
                                {
                                    ActionId = step.Action.ActionId,
                                    Type = step.Action.Type,
                                    Description = step.Action.Description,
                                    Permission = step.Action.Permission,
                                    Status = step.Action.Status,
                                    Value = resolvedValue,
                                    Target = step.Action.Target != null ? new ActionTarget
                                    {
                                        Selector = resolvedSelector,
                                        Text = resolvedText,
                                        X = step.Action.Target.X,
                                        Y = step.Action.Target.Y
                                    } : null
                                };

                                string retryResult;
                                if (browserAgent != null && IsBrowserAction(resolvedAction.Type))
                                {
                                    retryResult = await ExecuteBrowserActionAsync(browserAgent, resolvedAction, linkedToken);
                                }
                                else if (desktopOp != null && IsDesktopAction(resolvedAction.Type))
                                {
                                    retryResult = await ExecuteDesktopActionAsync(desktopOp, resolvedAction, linkedToken);
                                }
                                else
                                {
                                    retryResult = await _executor.ExecuteAsync(resolvedAction, linkedToken);
                                }

                                step.Action.Status = resolvedAction.Status;
                                step.Action.Result = retryResult;

                                context.SetVariable("last_result", step.Action.Result ?? string.Empty);
                                if (step.Action.Type == ActionType.Navigate && !string.IsNullOrEmpty(resolvedAction.Value))
                                {
                                    context.SetVariable("current_url", resolvedAction.Value);
                                }

                                if (step.Verifier != null)
                                {
                                    _logger?.LogDebug("Verifying step '{StepId}' after recovery", step.Id);
                                    bool verified = await step.Verifier.VerifyAsync(context, linkedToken);
                                    if (!verified)
                                    {
                                        throw new InvalidOperationException($"Verification failed for step '{step.Id}' after recovery.");
                                    }
                                }

                                step.Status = StepStatus.Completed;
                                step.CompletedAt = DateTimeOffset.UtcNow;
                                completedSteps.Add(step);
                                recovered = true;
                                lastError = null;
                            }
                        }
                        catch (Exception recoveryEx)
                        {
                            _logger?.LogError(recoveryEx, "Recovery failed or threw an exception for step '{StepId}'", step.Id);
                            lastError = recoveryEx;
                            recovered = false;
                        }
                    }

                    if (!recovered)
                    {
                        step.Status = StepStatus.Failed;
                        step.Error = lastError?.Message;

                        for (int j = i + 1; j < order.Count; j++)
                        {
                            order[j].Status = StepStatus.Skipped;
                        }

                        _logger?.LogError(lastError, "Step '{StepId}' execution or recovery failed. Initiating rollback.", step.Id);
                        await RollbackCompletedStepsAsync(completedSteps, context, linkedToken);
                        throw new InvalidOperationException($"Step '{step.Id}' failed: {lastError?.Message}", lastError);
                    }
                }
            }

            _state = RuntimeState.Idle;
        }
        catch (Exception)
        {
            if (_state != RuntimeState.Aborted)
            {
                _state = RuntimeState.Idle;
            }
            throw;
        }
        finally
        {
            _runCts?.Dispose();
            _runCts = null;
        }
    }

    public List<ExecutionStep> GetTopologicalOrder(ExecutionPlan plan)
    {
        var order = new List<ExecutionStep>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Visit(string id)
        {
            if (visited.Contains(id)) return;

            var step = plan.Steps[id];
            foreach (var depId in step.DependsOn)
            {
                Visit(depId);
            }

            visited.Add(id);
            order.Add(step);
        }

        foreach (var key in plan.Steps.Keys)
        {
            Visit(key);
        }

        return order;
    }

    private async Task RollbackCompletedStepsAsync(List<ExecutionStep> completedSteps, ExecutionContext context, CancellationToken ct)
    {
        _logger?.LogWarning("Initiating reverse-order rollback for {Count} completed steps.", completedSteps.Count);
        for (int i = completedSteps.Count - 1; i >= 0; i--)
        {
            var step = completedSteps[i];
            step.Status = StepStatus.RolledBack;
            if (step.RollbackHandler != null)
            {
                try
                {
                    _logger?.LogInformation("Rolling back step '{StepId}'", step.Id);
                    await step.RollbackHandler.RollbackAsync(context, ct);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to roll back step '{StepId}' during graph rollback.", step.Id);
                }
            }
        }
    }

    private static bool IsBrowserAction(ActionType type)
    {
        return type == ActionType.Navigate || type == ActionType.Click || type == ActionType.Type || type == ActionType.Screenshot || type == ActionType.Scroll;
    }

    private static bool IsDesktopAction(ActionType type)
    {
        return type == ActionType.Click || type == ActionType.Type || type == ActionType.KeyPress;
    }

    private static async Task<string> ExecuteBrowserActionAsync(BrowserAgentRuntime browser, AutomationAction action, CancellationToken ct)
    {
        action.Status = ActionStatus.Running;
        switch (action.Type)
        {
            case ActionType.Navigate:
                var url = action.Value ?? throw new ArgumentException("Navigate requires URL");
                await browser.NavigateAsync(url, ct);
                action.Status = ActionStatus.Completed;
                return $"Navigated to {url}";
            case ActionType.Click:
                var selector = action.Target?.Selector ?? throw new ArgumentException("Click selector is required");
                await browser.ClickAsync(selector, ct);
                action.Status = ActionStatus.Completed;
                return $"Clicked selector {selector}";
            case ActionType.Type:
                var typeSelector = action.Target?.Selector ?? throw new ArgumentException("Type selector is required");
                var text = action.Value ?? throw new ArgumentException("Type value is required");
                await browser.TypeAsync(typeSelector, text, ct);
                action.Status = ActionStatus.Completed;
                return $"Typed '{text}' into {typeSelector}";
            case ActionType.Screenshot:
                var bytes = await browser.TakeScreenshotAsync(ct);
                action.Status = ActionStatus.Completed;
                return $"Screenshot captured ({bytes.Length} bytes)";
            default:
                throw new NotSupportedException($"Browser action {action.Type} is not supported in this runtime.");
        }
    }

    private static async Task<string> ExecuteDesktopActionAsync(IDesktopOperator desktop, AutomationAction action, CancellationToken ct)
    {
        action.Status = ActionStatus.Running;
        switch (action.Type)
        {
            case ActionType.Click:
                if (!action.Target?.X.HasValue ?? true || !action.Target.Y.HasValue)
                    throw new ArgumentException("Desktop Click requires X and Y coordinates");
                await desktop.ClickAsync(action.Target.X.Value, action.Target.Y.Value, ct);
                action.Status = ActionStatus.Completed;
                return $"Clicked desktop at ({action.Target.X.Value}, {action.Target.Y.Value})";
            case ActionType.Type:
                var text = action.Value ?? throw new ArgumentException("Desktop Type requires value");
                await desktop.TypeAsync(text, ct);
                action.Status = ActionStatus.Completed;
                return $"Typed desktop text '{text}'";
            case ActionType.KeyPress:
                var key = action.Value ?? throw new ArgumentException("Desktop KeyPress requires key name");
                await desktop.KeyPressAsync(key, ct);
                action.Status = ActionStatus.Completed;
                return $"Pressed desktop key '{key}'";
            default:
                throw new NotSupportedException($"Desktop action {action.Type} is not supported.");
        }
    }

    private static string SubstituteVariables(string? template, ExecutionContext context)
    {
        if (string.IsNullOrEmpty(template)) return string.Empty;
        var result = template;
        foreach (var kvp in context.Variables)
        {
            var placeholder = "{{" + kvp.Key + "}}";
            if (result.Contains(placeholder))
            {
                result = result.Replace(placeholder, kvp.Value?.ToString() ?? string.Empty);
            }
        }
        return result;
    }

    public void Dispose()
    {
        _pauseEvent.Dispose();
        _runCts?.Dispose();
        GC.SuppressFinalize(this);
    }
}

