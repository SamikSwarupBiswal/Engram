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
    private readonly TrustTierManager _trustTierManager;
    private readonly ReversibilityEvaluator _reversibilityEvaluator;
    private readonly SemanticSummarizer _semanticSummarizer;
    private readonly FailureNarrativeRecorder? _failureNarrativeRecorder;
    private readonly RecoveryLegibilityEngine? _recoveryLegibilityEngine;
    private readonly ILogger<ActionRuntime>? _logger;

    private RuntimeState _state = RuntimeState.Idle;
    private readonly ManualResetEventSlim _pauseEvent = new(true);
    private CancellationTokenSource? _runCts;
    private ExecutionPlan? _activePlan;
    private ExecutionContext? _activeContext;

    private readonly ExecutionStateMachine _stateMachine = new();
    private FocusOwnershipManager? _focusOwnershipManager;
    private InputStabilityGuard? _inputStabilityGuard;
    private InteractionDebounceEngine? _debounceEngine;
    private VerificationConsensusEngine? _verificationConsensusEngine;
    private VerificationStrengthPolicy? _verificationStrengthPolicy;
    private FalseCompletionDetector? _falseCompletionDetector;
    private ChaosInjectionHarness? _chaosHarness;
    private ExternalImpactGate? _externalImpactGate;
    private TemporalExecutionDegradationModel? _temporalDegradationModel;

    private RealityConvergenceTracker? _realityConvergenceTracker;
    private VerificationTemporalStabilizer? _temporalStabilizer;
    private EnvironmentalInterruptGraph? _environmentalInterruptGraph;
    private FailureTopologyGraph? _failureTopologyGraph;
    private ProceduralExperienceStore? _experienceStore;
    private ProceduralDriftDetector? _driftDetector;

    public RuntimeState State => _state;
    public ExecutionPlan? ActivePlan => _activePlan;
    public ExecutionContext? ActiveContext => _activeContext;
    public TrustTierManager TrustTierManager => _trustTierManager;
    public ReversibilityEvaluator ReversibilityEvaluator => _reversibilityEvaluator;
    public SemanticSummarizer SemanticSummarizer => _semanticSummarizer;

    public ExecutionStateMachine StateMachine => _stateMachine;
    public FocusOwnershipManager? FocusOwnershipManager { get => _focusOwnershipManager; set => _focusOwnershipManager = value; }
    public InputStabilityGuard? InputStabilityGuard { get => _inputStabilityGuard; set => _inputStabilityGuard = value; }
    public InteractionDebounceEngine? DebounceEngine { get => _debounceEngine; set => _debounceEngine = value; }
    public VerificationConsensusEngine? VerificationConsensusEngine { get => _verificationConsensusEngine; set => _verificationConsensusEngine = value; }
    public VerificationStrengthPolicy? VerificationStrengthPolicy { get => _verificationStrengthPolicy; set => _verificationStrengthPolicy = value; }
    public FalseCompletionDetector? FalseCompletionDetector { get => _falseCompletionDetector; set => _falseCompletionDetector = value; }
    public ChaosInjectionHarness? ChaosHarness { get => _chaosHarness; set => _chaosHarness = value; }
    public ExternalImpactGate? ExternalImpactGate { get => _externalImpactGate; set => _externalImpactGate = value; }
    public TemporalExecutionDegradationModel? TemporalDegradationModel { get => _temporalDegradationModel; set => _temporalDegradationModel = value; }

    public RealityConvergenceTracker? RealityConvergenceTracker { get => _realityConvergenceTracker; set => _realityConvergenceTracker = value; }
    public VerificationTemporalStabilizer? TemporalStabilizer { get => _temporalStabilizer; set => _temporalStabilizer = value; }
    public EnvironmentalInterruptGraph? EnvironmentalInterruptGraph { get => _environmentalInterruptGraph; set => _environmentalInterruptGraph = value; }
    public FailureTopologyGraph? FailureTopologyGraph { get => _failureTopologyGraph; set => _failureTopologyGraph = value; }
    public ProceduralExperienceStore? ExperienceStore { get => _experienceStore; set => _experienceStore = value; }
    public ProceduralDriftDetector? DriftDetector { get => _driftDetector; set => _driftDetector = value; }

    public ActionRuntime(
        ActionExecutor executor, 
        PermissionGate permissionGate, 
        ExecutionSafetyManager? safetyManager = null,
        TrustTierManager? trustTierManager = null,
        ReversibilityEvaluator? reversibilityEvaluator = null,
        SemanticSummarizer? semanticSummarizer = null,
        FailureNarrativeRecorder? failureNarrativeRecorder = null,
        RecoveryLegibilityEngine? recoveryLegibilityEngine = null,
        ILogger<ActionRuntime>? logger = null)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _permissionGate = permissionGate ?? throw new ArgumentNullException(nameof(permissionGate));
        _safetyManager = safetyManager ?? new ExecutionSafetyManager();
        _trustTierManager = trustTierManager ?? new TrustTierManager(TrustTier.Privileged);
        _reversibilityEvaluator = reversibilityEvaluator ?? new ReversibilityEvaluator();
        _semanticSummarizer = semanticSummarizer ?? new SemanticSummarizer();
        _failureNarrativeRecorder = failureNarrativeRecorder;
        _recoveryLegibilityEngine = recoveryLegibilityEngine;
        _logger = logger;

        var defaultUi = new MockUiProvider();
        _focusOwnershipManager = new FocusOwnershipManager(defaultUi);
        _inputStabilityGuard = new InputStabilityGuard(new SovereigntyMonitor());
        _debounceEngine = new InteractionDebounceEngine();
        _verificationConsensusEngine = new VerificationConsensusEngine();
        _verificationStrengthPolicy = new VerificationStrengthPolicy();
        _falseCompletionDetector = new FalseCompletionDetector(defaultUi);
        _chaosHarness = new ChaosInjectionHarness();
        _externalImpactGate = new ExternalImpactGate();
        _temporalDegradationModel = new TemporalExecutionDegradationModel();

        _realityConvergenceTracker = new RealityConvergenceTracker();
        _temporalStabilizer = new VerificationTemporalStabilizer(defaultUi, null, _realityConvergenceTracker);
        _environmentalInterruptGraph = new EnvironmentalInterruptGraph();
        _failureTopologyGraph = new FailureTopologyGraph();
        _experienceStore = new ProceduralExperienceStore();
        _driftDetector = new ProceduralDriftDetector(_experienceStore);
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

        if (Array.Exists(Environment.GetCommandLineArgs(), arg => arg == "--safe-mode") ||
            Environment.GetEnvironmentVariable("ENGRAM_SAFE_MODE") == "true")
        {
            throw new InvalidOperationException("System is running in read-only Safe Mode. Automation actions are blocked.");
        }

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

        _debounceEngine?.ClearHistory();

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

                // Generate and log semantic summary
                var semanticSummary = _semanticSummarizer.Summarize(resolvedAction);
                _logger?.LogInformation("Semantic Intent: {Summary}", semanticSummary);

                // ── D7 ROBOTICS SUBSTRATE ──
                _stateMachine.ForceState(WorkflowState.AcquiringTarget);

                // Get embodiment provider
                var embodimentProvider = context.GetVariable<IUiEmbodimentProvider>("UiEmbodimentProvider")
                                         ?? new DefaultUiEmbodimentProvider(context, _executor);
                embodimentProvider.IsSimulationMode = _safetyManager.IsSimulationMode;

                // Re-initialize managers with active provider
                _focusOwnershipManager = new FocusOwnershipManager(embodimentProvider);
                _falseCompletionDetector = new FalseCompletionDetector(embodimentProvider);
                _temporalStabilizer = new VerificationTemporalStabilizer(
                    embodimentProvider,
                    null,
                    _realityConvergenceTracker);

                // Debounce check
                if (_debounceEngine != null && _debounceEngine.RecordActionAndCheckDebounce(resolvedAction))
                {
                    _logger?.LogWarning("InteractionDebounceEngine: Action debounced to prevent duplicate click/execution storm.");
                    step.Status = StepStatus.Completed;
                    step.CompletedAt = DateTimeOffset.UtcNow;
                    completedSteps.Add(step);
                    continue;
                }

                // Input stability check
                if (!_safetyManager.IsSimulationMode && _inputStabilityGuard != null && !await _inputStabilityGuard.IsInputStableAsync(linkedToken))
                {
                    _stateMachine.TransitionTo(WorkflowState.YieldedToHuman);
                    _logger?.LogWarning("InputStabilityGuard: Desktop input is unstable. Yielding control to human.");
                    step.Status = StepStatus.Failed;
                    step.Error = "Input stability guard blocked execution.";
                    throw new InvalidOperationException("Execution halted: Input stability guard blocked execution due to cursor spikes or layout shifts.");
                }

                // Focus ownership and occlusion check
                if (_focusOwnershipManager != null)
                {
                    var (activeProc, activeTitle) = await embodimentProvider.GetActiveWindowAsync(linkedToken);
                    if (resolvedAction.Target != null)
                    {
                        _stateMachine.TransitionTo(WorkflowState.VerifyingEnvironment);
                        
                        // Focus ownership check: verify active process matches expected target if expected process is set in context
                        var expectedProc = context.GetVariable<string>("ExpectedProcessName");
                        if (!string.IsNullOrEmpty(expectedProc))
                        {
                            bool hasFocus = await _focusOwnershipManager.VerifyFocusAsync(expectedProc, null, linkedToken);
                            if (!hasFocus)
                            {
                                _stateMachine.TransitionTo(WorkflowState.RealityUncertain);
                                throw new InvalidOperationException($"Focus ownership conflict: Expected focus on '{expectedProc}' but active is '{activeProc}'.");
                            }
                        }

                        // Occlusion check
                        bool isOccluded = await _focusOwnershipManager.DetectOverlayOrOcclusionAsync(linkedToken);
                        if (isOccluded)
                        {
                            _stateMachine.TransitionTo(WorkflowState.RealityUncertain);
                            throw new InvalidOperationException("Focus ownership conflict: Notification overlay or system dialog is occluding target window.");
                        }
                    }
                }

                // External Impact Gate
                if (_externalImpactGate != null && !await _externalImpactGate.ValidateActionSafetyAsync(resolvedAction, linkedToken))
                {
                    _logger?.LogInformation("ExternalImpactGate: Action flagged as irreversible or external propagation. Forcing human confirmation.");
                    if (resolvedAction.Permission == ActionPermission.AutoApproved)
                    {
                        resolvedAction.Permission = ActionPermission.Pending;
                        step.Action.Permission = ActionPermission.Pending;
                    }
                }

                if (_stateMachine.CurrentState == WorkflowState.AcquiringTarget)
                {
                    _stateMachine.TransitionTo(WorkflowState.VerifyingEnvironment);
                }

                // Register StateVerificationEngine in context so verifiers can use it
                var verificationEngine = new StateVerificationEngine(embodimentProvider);
                context.SetVariable("StateVerificationEngine", verificationEngine);

                try
                {
                    // Validate the action against the active trust tier
                    _trustTierManager.ValidateAction(resolvedAction);

                    // ── SAFETY CHECKS ──
                    _safetyManager.VerifyRateLimit();
                    _safetyManager.VerifyMouseFailsafe();

                    var (proc, title) = await embodimentProvider.GetActiveWindowAsync(linkedToken);
                    _safetyManager.VerifyProcessSafety(proc, title);

                    if (resolvedAction.Type == ActionType.Navigate && !string.IsNullOrEmpty(resolvedAction.Value))
                    {
                        _safetyManager.VerifyUrlSafety(resolvedAction.Value);
                    }

                    var url = await embodimentProvider.GetUrlAsync(linkedToken);
                    if (!string.IsNullOrEmpty(url))
                    {
                        _safetyManager.VerifyUrlSafety(url);
                    }
                }
                catch (Exception ex)
                {
                    step.Status = StepStatus.Failed;
                    step.Error = ex.Message;
                    _permissionGate.RecordFailure(resolvedAction, wasCancelled: false);
                    
                    if (_failureNarrativeRecorder != null && _recoveryLegibilityEngine != null)
                    {
                        var activeAutonomy = context.GetVariable<string>("ActiveAutonomy") ?? "Medium";
                        var legibleExplanation = _recoveryLegibilityEngine.TranslateFailure(ex.Message, ex.ToString());
                        var narrative = new FailureNarrative
                        {
                            WorkflowId = plan.PlanId,
                            Goal = plan.Goal,
                            FailedStepId = step.Id,
                            StepDescription = step.Action.Description,
                            TechnicalDetails = ex.ToString(),
                            LegibleExplanation = legibleExplanation,
                            AutonomyLevel = activeAutonomy,
                            RecoveryAttempted = false,
                            RecoverySucceeded = false,
                            RecoveryExplanation = "No recovery policy specified."
                        };
                        _ = _failureNarrativeRecorder.RecordFailureNarrativeAsync(narrative);
                    }

                    for (int j = i + 1; j < order.Count; j++)
                    {
                        order[j].Status = StepStatus.Skipped;
                    }
                    _stateMachine.TransitionTo(WorkflowState.FailedSafe);
                    await RollbackCompletedStepsAsync(completedSteps, context, linkedToken);
                    throw;
                }

                // Track expected mouse coordinates if performing coordinate-based click
                if (resolvedAction.Type == ActionType.Click && resolvedAction.Target != null && resolvedAction.Target.X.HasValue && resolvedAction.Target.Y.HasValue)
                {
                    _safetyManager.UpdateExpectedMousePosition(resolvedAction.Target.X.Value, resolvedAction.Target.Y.Value);
                }

                // 1. Permission check
                if (resolvedAction.Permission != ActionPermission.Approved && resolvedAction.Permission != ActionPermission.AutoApproved)
                {
                    var gatePermission = _permissionGate.CheckPermission(resolvedAction);
                    var isIrreversible = _reversibilityEvaluator.IsIrreversible(resolvedAction);

                    if (gatePermission == ActionPermission.AutoApproved && !isIrreversible)
                    {
                        resolvedAction.Permission = ActionPermission.AutoApproved;
                        step.Action.Permission = ActionPermission.AutoApproved;
                    }
                    else
                    {
                        step.Status = StepStatus.Failed;
                        var errorMsg = isIrreversible 
                            ? "Action blocked: action is irreversible and requires explicit human approval." 
                            : $"Step action is not approved (status: {gatePermission})";
                        step.Error = errorMsg;
                        
                        // Mark remaining steps as Skipped
                        for (int j = i + 1; j < order.Count; j++)
                        {
                            order[j].Status = StepStatus.Skipped;
                        }

                        _stateMachine.TransitionTo(WorkflowState.FailedSafe);
                        await RollbackCompletedStepsAsync(completedSteps, context, linkedToken);
                        throw new InvalidOperationException(errorMsg);
                    }
                }
                else if (resolvedAction.Permission == ActionPermission.AutoApproved && _reversibilityEvaluator.IsIrreversible(resolvedAction))
                {
                    step.Status = StepStatus.Failed;
                    var errorMsg = "Action blocked: action is irreversible and requires explicit human approval.";
                    step.Error = errorMsg;
                    
                    for (int j = i + 1; j < order.Count; j++)
                    {
                        order[j].Status = StepStatus.Skipped;
                    }

                    _stateMachine.TransitionTo(WorkflowState.FailedSafe);
                    await RollbackCompletedStepsAsync(completedSteps, context, linkedToken);
                    throw new InvalidOperationException(errorMsg);
                }

                _stateMachine.TransitionTo(WorkflowState.Executing);
                step.Status = StepStatus.Executing;
                step.StartedAt = DateTimeOffset.UtcNow;

                try
                {
                    _logger?.LogInformation("Executing step '{StepId}': {Description}", step.Id, step.Action.Description);

                    // Register step semantics and properties in FailureTopologyGraph
                    if (_failureTopologyGraph != null)
                    {
                        var isIrreversible = _reversibilityEvaluator.IsIrreversible(resolvedAction);
                        var semantics = new MutationBoundarySemantics
                        {
                            IsReversible = !isIrreversible,
                            IsIrreversible = isIrreversible,
                            IsExternallyPropagated = resolvedAction.Type == ActionType.Download || resolvedAction.Type == ActionType.Upload
                        };
                        _failureTopologyGraph.RegisterStepSemantics(step.Id, semantics);

                        if (semantics.IsExternallyPropagated)
                        {
                            var prop = new TrackedPropagation
                            {
                                WorkflowId = plan.PlanId,
                                StepId = step.Id,
                                DestinationType = resolvedAction.Type.ToString(),
                                DestinationValue = resolvedAction.Value ?? resolvedAction.Target?.Selector ?? string.Empty,
                                Timestamp = DateTimeOffset.UtcNow
                            };
                            _failureTopologyGraph.RegisterPropagation(step.Id, prop);
                        }
                    }

                    // Enforce modal safety laws and active window check before dispatching mouse/keyboard events
                    if (_environmentalInterruptGraph != null)
                    {
                        var (activeProc, activeTitle) = await embodimentProvider.GetActiveWindowAsync(linkedToken);
                        var expectedProc = context.GetVariable<string>("ExpectedProcessName");
                        bool isUnexpectedProc = !string.IsNullOrEmpty(expectedProc) && !activeProc.Equals(expectedProc, StringComparison.OrdinalIgnoreCase);

                        bool isOverlayOrModal = isUnexpectedProc || 
                                                activeProc.Contains("consent", StringComparison.OrdinalIgnoreCase) ||
                                                activeProc.Contains("CredentialUIBroker", StringComparison.OrdinalIgnoreCase) ||
                                                activeProc.Contains("pinflow", StringComparison.OrdinalIgnoreCase) ||
                                                EnvironmentalInterruptGraph.ForbiddenKeywords.Any(k => activeTitle.Contains(k, StringComparison.OrdinalIgnoreCase));

                        if (isOverlayOrModal)
                        {
                            bool handled = await _environmentalInterruptGraph.AssessAndHandleInterruptAsync(activeProc, activeTitle, context, linkedToken);
                            if (!handled)
                            {
                                _stateMachine.TransitionTo(WorkflowState.RealityUncertain);
                                _stateMachine.TransitionTo(WorkflowState.Suspended);
                                throw new InvalidOperationException($"Safety Violation: Forbidden or unknown modal '{activeTitle}' (Process: '{activeProc}') detected. Sovereignty yielded to human.");
                            }
                        }
                    }
                    
                    string result = await embodimentProvider.ExecuteActionAsync(resolvedAction, linkedToken);

                    step.Action.Status = resolvedAction.Status;
                    step.Action.Result = result;

                    context.SetVariable("last_result", step.Action.Result ?? string.Empty);
                    if (step.Action.Type == ActionType.Navigate && !string.IsNullOrEmpty(resolvedAction.Value))
                    {
                        context.SetVariable("current_url", resolvedAction.Value);
                    }

                    context.SetVariable($"step_{step.Id}_result", step.Action.Result ?? string.Empty);

                    _stateMachine.TransitionTo(WorkflowState.VerifyingMutation);

                    // False completion check
                    if (_falseCompletionDetector != null && await _falseCompletionDetector.DetectFalseCompletionAsync(result, linkedToken))
                    {
                        _stateMachine.TransitionTo(WorkflowState.RealityUncertain);
                        throw new InvalidOperationException($"False completion detected: Screen indicates error or occluding prompt.");
                    }

                    var signals = new VerificationSignals { StructuredApiVerified = true };

                    if (step.Verifier != null)
                    {
                        _logger?.LogDebug("Verifying step '{StepId}' with reality convergence", step.Id);
                        bool verified = await VerifyStepWithConvergenceAsync(step, context, linkedToken);
                        signals.StructuredApiVerified = verified;
                        if (!verified)
                        {
                            var uncertainty = UncertaintyLevel.U1_Observational;
                            if (_reversibilityEvaluator != null && _reversibilityEvaluator.IsIrreversible(resolvedAction))
                            {
                                uncertainty = UncertaintyLevel.U3_Irreversible;
                            }

                            if (uncertainty == UncertaintyLevel.U1_Observational)
                            {
                                throw new InvalidOperationException($"Verification failed for step '{step.Id}'");
                            }
                            else if (uncertainty == UncertaintyLevel.U2_StateAmbiguity)
                            {
                                _stateMachine.TransitionTo(WorkflowState.RealityUncertain);
                                _stateMachine.TransitionTo(WorkflowState.Suspended);
                                throw new InvalidOperationException($"Verification failed: Reality is uncertain (Uncertainty: U2_StateAmbiguity). Halt to preserve trust.");
                            }
                            else
                            {
                                _stateMachine.TransitionTo(WorkflowState.RealityUncertain);
                                _stateMachine.TransitionTo(WorkflowState.FailedSafe);
                                throw new InvalidOperationException($"Critical verification safety violation (Uncertainty: {uncertainty}). Full immediate halt.");
                            }
                        }
                    }

                    if (_verificationConsensusEngine != null && _verificationStrengthPolicy != null)
                    {
                        double confidence = _verificationConsensusEngine.CalculateRealityConfidence(signals);
                        bool meetsReqs = _verificationStrengthPolicy.MeetsVerificationRequirements(RiskLevel.MediumRisk, confidence, signals);
                        
                        double requiredCertainty = _verificationStrengthPolicy.GetRequiredCertainty(RiskLevel.MediumRisk);
                        if (_temporalDegradationModel != null)
                        {
                            var elapsedPlanTime = DateTimeOffset.UtcNow - plan.Steps.Values.Min(s => s.StartedAt ?? DateTimeOffset.UtcNow);
                            double decayFactor = _temporalDegradationModel.ComputeTemporalDecayFactor(plan.PlanId, elapsedPlanTime, completedSteps.Count);
                            // Raise threshold as elapsed plan time compounds
                            requiredCertainty += (1.0 - decayFactor) * (1.0 - requiredCertainty);
                        }

                        if (!meetsReqs || confidence < requiredCertainty)
                        {
                            throw new InvalidOperationException($"Epistemic verification failed: Consensus confidence {confidence:F2} is insufficient for step '{step.Id}' (Required: {requiredCertainty:F2}).");
                        }
                    }

                    if (_temporalDegradationModel != null)
                    {
                        var elapsed = DateTimeOffset.UtcNow - (step.StartedAt ?? DateTimeOffset.UtcNow);
                        _temporalDegradationModel.ComputeTemporalDecayFactor(plan.PlanId, elapsed, completedSteps.Count);
                    }

                    // Record procedural experience on success
                    var elapsedStepTime = DateTimeOffset.UtcNow - (step.StartedAt ?? DateTimeOffset.UtcNow);
                    if (_experienceStore != null)
                    {
                        var appName = context.GetVariable<string>("AppName") ?? "DefaultApp";
                        var appVersion = context.GetVariable<string>("AppVersion") ?? "1.0";
                        var (curProc, curTitle) = await embodimentProvider.GetActiveWindowAsync(linkedToken);

                        _experienceStore.RecordMetric(appName, appVersion, resolvedAction.Type, resolvedAction.Target?.Selector ?? string.Empty, elapsedStepTime, success: true);

                        if (!string.IsNullOrEmpty(curTitle) && curTitle != "Desktop" && curTitle != "Windows")
                        {
                            _experienceStore.AddSeenModal(appName, appVersion, resolvedAction.Type, resolvedAction.Target?.Selector ?? string.Empty, curTitle);
                        }

                        if (_driftDetector != null)
                        {
                            var drift = _driftDetector.DetectDrift(
                                appName,
                                appVersion,
                                resolvedAction.Type,
                                resolvedAction.Target?.Selector ?? string.Empty,
                                elapsedStepTime,
                                success: true,
                                curTitle,
                                out var reason);

                            if (drift != null)
                            {
                                _logger?.LogWarning("Procedural Drift Detected on success: {Reason}. Uncertainty Level: {DriftLevel}", reason, drift);
                                if (drift >= UncertaintyLevel.U2_StateAmbiguity)
                                {
                                    _stateMachine.TransitionTo(WorkflowState.RealityUncertain);
                                    _stateMachine.TransitionTo(WorkflowState.Suspended);
                                    throw new InvalidOperationException($"Halted execution due to procedural drift: {reason}");
                                }
                            }
                        }
                    }

                    step.Status = StepStatus.Completed;
                    step.CompletedAt = DateTimeOffset.UtcNow;
                    completedSteps.Add(step);
                    _stateMachine.TransitionTo(WorkflowState.Completed);
                    _permissionGate.RecordSuccess(resolvedAction);
                }
                catch (Exception ex)
                {
                    if (ex is OperationCanceledException && _state == RuntimeState.Paused)
                    {
                        step.Status = StepStatus.Pending;
                        throw;
                    }

                    // Record procedural experience on failure
                    if (_experienceStore != null)
                    {
                        var appName = context.GetVariable<string>("AppName") ?? "DefaultApp";
                        var appVersion = context.GetVariable<string>("AppVersion") ?? "1.0";
                        var (curProc, curTitle) = await embodimentProvider.GetActiveWindowAsync(linkedToken);
                        var elapsedFailureTime = DateTimeOffset.UtcNow - (step.StartedAt ?? DateTimeOffset.UtcNow);

                        _experienceStore.RecordMetric(appName, appVersion, resolvedAction.Type, resolvedAction.Target?.Selector ?? string.Empty, elapsedFailureTime, success: false);

                        if (_driftDetector != null)
                        {
                            var drift = _driftDetector.DetectDrift(
                                appName,
                                appVersion,
                                resolvedAction.Type,
                                resolvedAction.Target?.Selector ?? string.Empty,
                                elapsedFailureTime,
                                success: false,
                                curTitle,
                                out var reason);

                            if (drift != null)
                            {
                                _logger?.LogWarning("Procedural Drift Detected on failure: {Reason}. Uncertainty Level: {DriftLevel}", reason, drift);
                                if (drift >= UncertaintyLevel.U2_StateAmbiguity)
                                {
                                    _stateMachine.TransitionTo(WorkflowState.RealityUncertain);
                                    _stateMachine.TransitionTo(WorkflowState.Suspended);
                                    throw new InvalidOperationException($"Halted execution due to procedural drift on failure: {reason}", ex);
                                }
                            }
                        }
                    }

                    // Assess failure impact using FailureTopologyGraph
                    if (_failureTopologyGraph != null)
                    {
                        var impact = _failureTopologyGraph.AssessFailureImpact(step.Id, completedSteps);
                        _logger?.LogWarning("Failure impact assessment: CanRollbackCleanly={CanRollback}, BlockedSteps={BlockedCount}, UncertainSteps={UncertainCount}",
                            impact.CanRollbackCleanly, impact.BlockedSteps.Count, impact.UncertainSteps.Count);

                        foreach (var blockedStepId in impact.BlockedSteps)
                        {
                            var blockedStep = order.Find(s => s.Id == blockedStepId);
                            if (blockedStep != null)
                            {
                                blockedStep.Status = StepStatus.Skipped;
                            }
                        }
                    }

                    if (_stateMachine.CurrentState != WorkflowState.RealityUncertain && 
                        _stateMachine.CurrentState != WorkflowState.Suspended && 
                        _stateMachine.CurrentState != WorkflowState.FailedSafe)
                    {
                        _stateMachine.TransitionTo(WorkflowState.Recovering);
                    }
                    bool recovered = false;
                    Exception? lastError = ex;

                    if (step.RecoveryPolicy != null && 
                        _stateMachine.CurrentState != WorkflowState.Suspended && 
                        _stateMachine.CurrentState != WorkflowState.FailedSafe)
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

                                if (_stateMachine.CurrentState == WorkflowState.Recovering)
                                {
                                    _stateMachine.TransitionTo(WorkflowState.Executing);
                                }

                                string retryResult = await embodimentProvider.ExecuteActionAsync(resolvedAction, linkedToken);

                                step.Action.Status = resolvedAction.Status;
                                step.Action.Result = retryResult;

                                context.SetVariable("last_result", step.Action.Result ?? string.Empty);
                                if (step.Action.Type == ActionType.Navigate && !string.IsNullOrEmpty(resolvedAction.Value))
                                {
                                    context.SetVariable("current_url", resolvedAction.Value);
                                }

                                _stateMachine.TransitionTo(WorkflowState.VerifyingMutation);

                                if (step.Verifier != null)
                                {
                                    _logger?.LogDebug("Verifying step '{StepId}' after recovery", step.Id);
                                    bool verified = await VerifyStepWithConvergenceAsync(step, context, linkedToken);
                                    if (!verified)
                                    {
                                        throw new InvalidOperationException($"Verification failed for step '{step.Id}' after recovery.");
                                    }
                                }

                                _stateMachine.TransitionTo(WorkflowState.Completed);
                                step.Status = StepStatus.Completed;
                                step.CompletedAt = DateTimeOffset.UtcNow;
                                completedSteps.Add(step);
                                _permissionGate.RecordSuccess(resolvedAction);
                                recovered = true;
                                
                                if (_failureNarrativeRecorder != null && _recoveryLegibilityEngine != null)
                                {
                                    var activeAutonomy = context.GetVariable<string>("ActiveAutonomy") ?? "Medium";
                                    var legibleExplanation = _recoveryLegibilityEngine.TranslateFailure(ex.Message, ex.ToString());
                                    var recoveryExplanation = _recoveryLegibilityEngine.TranslateRecovery(true);
                                    var narrative = new FailureNarrative
                                    {
                                        WorkflowId = plan.PlanId,
                                        Goal = plan.Goal,
                                        FailedStepId = step.Id,
                                        StepDescription = step.Action.Description,
                                        TechnicalDetails = ex.ToString(),
                                        LegibleExplanation = legibleExplanation,
                                        AutonomyLevel = activeAutonomy,
                                        RecoveryAttempted = true,
                                        RecoverySucceeded = true,
                                        RecoveryExplanation = recoveryExplanation
                                    };
                                    _ = _failureNarrativeRecorder.RecordFailureNarrativeAsync(narrative);
                                }

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
                        _permissionGate.RecordFailure(resolvedAction, wasCancelled: lastError is OperationCanceledException || _state == RuntimeState.Aborted);
                         
                         if (_failureNarrativeRecorder != null && _recoveryLegibilityEngine != null)
                         {
                             var activeAutonomy = context.GetVariable<string>("ActiveAutonomy") ?? "Medium";
                             var legibleExplanation = _recoveryLegibilityEngine.TranslateFailure(lastError?.Message ?? string.Empty, lastError?.ToString() ?? string.Empty);
                             var recoveryExplanation = _recoveryLegibilityEngine.TranslateRecovery(false);
                             var narrative = new FailureNarrative
                             {
                                 WorkflowId = plan.PlanId,
                                 Goal = plan.Goal,
                                 FailedStepId = step.Id,
                                 StepDescription = step.Action.Description,
                                 TechnicalDetails = lastError?.ToString() ?? string.Empty,
                                 LegibleExplanation = legibleExplanation,
                                 AutonomyLevel = activeAutonomy,
                                 RecoveryAttempted = step.RecoveryPolicy != null,
                                 RecoverySucceeded = false,
                                 RecoveryExplanation = recoveryExplanation
                             };
                             _ = _failureNarrativeRecorder.RecordFailureNarrativeAsync(narrative);
                         }

                         if (_stateMachine.CurrentState != WorkflowState.Recovering &&
                             _stateMachine.CurrentState != WorkflowState.Suspended &&
                             _stateMachine.CurrentState != WorkflowState.FailedSafe)
                         {
                             _stateMachine.TransitionTo(WorkflowState.Recovering);
                         }

                         if (_stateMachine.CurrentState != WorkflowState.Suspended &&
                             _stateMachine.CurrentState != WorkflowState.FailedSafe)
                         {
                             _stateMachine.TransitionTo(WorkflowState.RolledBack);
                             await RollbackCompletedStepsAsync(completedSteps, context, linkedToken);
                             _stateMachine.TransitionTo(WorkflowState.FailedSafe);
                         }
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

    private async Task<bool> VerifyStepWithConvergenceAsync(ExecutionStep step, ExecutionContext context, CancellationToken ct)
    {
        if (step.Verifier == null) return true;

        if (_realityConvergenceTracker != null)
        {
            var totalTimeout = _temporalStabilizer?.MaxWaitTime ?? TimeSpan.FromSeconds(5);
            var quietWindow = _temporalStabilizer?.QuietPeriod ?? TimeSpan.FromMilliseconds(500);

            return await _realityConvergenceTracker.TrackConvergenceAsync(
                async () => await step.Verifier.VerifyAsync(context, ct),
                totalTimeout,
                quietWindow,
                ct
            );
        }

        return await step.Verifier.VerifyAsync(context, ct);
    }

    public void Dispose()
    {
        _pauseEvent.Dispose();
        _runCts?.Dispose();
        GC.SuppressFinalize(this);
    }
}

