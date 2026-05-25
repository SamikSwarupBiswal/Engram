using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Engram.Store.Automation;
using Engram.Store.Events;
using ExecutionContext = Engram.Store.Automation.ExecutionContext;

namespace Engram.Store.Tests;

public class MockCompensationAction : ICompensationAction
{
    public int ExecuteCount { get; private set; }
    public bool ShouldThrow { get; set; }

    public Task ExecuteCompensationAsync(ExecutionContext context, CancellationToken ct)
    {
        ExecuteCount++;
        if (ShouldThrow)
        {
            throw new InvalidOperationException("Mock compensation failed!");
        }
        return Task.CompletedTask;
    }
}

public class MockRollbackHandler : IStepRollback
{
    public int ExecuteCount { get; private set; }

    public Task RollbackAsync(ExecutionContext context, CancellationToken ct)
    {
        ExecuteCount++;
        return Task.CompletedTask;
    }
}

public class MockVerifier : IStepVerifier
{
    public bool Result { get; set; } = true;

    public Task<bool> VerifyAsync(ExecutionContext context, CancellationToken ct)
    {
        return Task.FromResult(Result);
    }
}

public class PhaseD7ValidationSuite : IDisposable
{
    private readonly string _tempDir;
    private readonly ActionExecutor _executor;
    private readonly PermissionGate _permissionGate;

    public PhaseD7ValidationSuite()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram_epistemic_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _executor = new ActionExecutor();
        _permissionGate = new PermissionGate();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    [Fact]
    public async Task IdentityEnvelopeContinuity_ShouldPersistAndRestoreLineage()
    {
        // Arrange
        var persistenceStore = new WorkflowPersistenceStore(_tempDir);
        using var runtime = new ActionRuntime(_executor, _permissionGate);
        var worldModel = new OperationalWorldModel(new InMemoryEventBus());
        var wr = new WorkflowRuntime(persistenceStore, runtime, worldModel);

        var plan = new ExecutionPlan
        {
            PlanId = "test_workflow_123",
            Goal = "Test Identity Continuity"
        };
        var step1 = new ExecutionStep
        {
            Id = "step1",
            Action = new AutomationAction
            {
                ActionId = "act1",
                Type = ActionType.Wait,
                Value = "10",
                Description = "Wait for 10ms",
                Permission = ActionPermission.AutoApproved
            }
        };
        plan.Steps[step1.Id] = step1;

        var context = new ExecutionContext();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act - Run with cancelled token to stop after initial checkpoint
        await Assert.ThrowsAnyAsync<Exception>(() => wr.StartWorkflowAsync("test_workflow_123", plan, context, cts.Token));

        var filePath = Path.Combine(persistenceStore.StoreDirectory, "test_workflow_123.json");
        if (File.Exists(filePath))
        {
            Console.WriteLine("CHECKPOINT JSON CONTENT: " + File.ReadAllText(filePath));
        }
        else
        {
            Console.WriteLine("CHECKPOINT JSON FILE NOT FOUND AT: " + filePath);
        }

        // Verify it ran and saved checkpoint
        var checkpoint = await persistenceStore.LoadCheckpointAsync("test_workflow_123");
        Assert.NotNull(checkpoint);
        Assert.NotNull(checkpoint.IdentityEnvelope);
        Assert.Equal("test_workflow_123", checkpoint.IdentityEnvelope.WorkflowId);
        Assert.Contains("Test Identity Continuity", checkpoint.IdentityEnvelope.IntentHistory);

        // Reset runtime envelope to simulate clean restore
        runtime.IdentityEnvelope = null;

        // Restore - also using cancelled token to avoid running/deleting checkpoint on success
        var newContext = new ExecutionContext();
        await Assert.ThrowsAnyAsync<Exception>(() => wr.RestoreWorkflowAsync("test_workflow_123", newContext, cts.Token));

        // Assert restored identity envelope is intact
        Assert.NotNull(runtime.IdentityEnvelope);
        Assert.Equal("test_workflow_123", runtime.IdentityEnvelope.WorkflowId);
        Assert.Contains("Test Identity Continuity", runtime.IdentityEnvelope.IntentHistory);
    }

    [Fact]
    public async Task CompensationVsRollback_ShouldExecuteCompensation_WhenCompensatable()
    {
        // Arrange
        using var runtime = new ActionRuntime(_executor, _permissionGate);
        var mockCompensation = new MockCompensationAction();
        var mockRollback = new MockRollbackHandler();

        var plan = new ExecutionPlan { PlanId = "comp_test", Goal = "Test Compensation" };
        
        var step1 = new ExecutionStep
        {
            Id = "step1",
            Action = new AutomationAction { ActionId = "act1", Type = ActionType.Wait, Value = "10", Permission = ActionPermission.AutoApproved },
            RollbackHandler = mockRollback,
            Status = StepStatus.Completed,
            Semantics = new MutationBoundarySemantics
            {
                IsCompensatable = true,
                CompensationAction = mockCompensation
            }
        };

        var step2 = new ExecutionStep
        {
            Id = "step2",
            Action = new AutomationAction { ActionId = "act2", Type = ActionType.Wait, Value = "10", Permission = ActionPermission.AutoApproved },
            Verifier = new MockVerifier { Result = false } // verification failure triggers rollback
        };

        plan.Steps[step1.Id] = step1;
        plan.Steps[step2.Id] = step2;
        step2.DependsOn.Add(step1.Id);

        var context = new ExecutionContext();

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(() => runtime.ExecutePlanAsync(plan, context));

        // step1's compensation action should run, NOT its rollback handler
        Assert.Equal(1, mockCompensation.ExecuteCount);
        Assert.Equal(0, mockRollback.ExecuteCount);
    }

    [Fact]
    public async Task CompensationVsRollback_ShouldSuspend_WhenIrrecoverable()
    {
        // Arrange
        using var runtime = new ActionRuntime(_executor, _permissionGate);
        var mockRollback = new MockRollbackHandler();

        var plan = new ExecutionPlan { PlanId = "irrecoverable_test", Goal = "Test Rollback Bypass" };
        
        var step1 = new ExecutionStep
        {
            Id = "step1",
            Action = new AutomationAction { ActionId = "act1", Type = ActionType.Wait, Value = "10", Permission = ActionPermission.AutoApproved },
            RollbackHandler = mockRollback,
            Status = StepStatus.Completed,
            Semantics = new MutationBoundarySemantics
            {
                IsIrrecoverable = true
            }
        };

        var step2 = new ExecutionStep
        {
            Id = "step2",
            Action = new AutomationAction { ActionId = "act2", Type = ActionType.Wait, Value = "10", Permission = ActionPermission.AutoApproved },
            Verifier = new MockVerifier { Result = false }
        };

        plan.Steps[step1.Id] = step1;
        plan.Steps[step2.Id] = step2;
        step2.DependsOn.Add(step1.Id);

        var context = new ExecutionContext();

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(() => runtime.ExecutePlanAsync(plan, context));

        // Rollback should be bypassed entirely, leaving step1 untouched
        Assert.Equal(0, mockRollback.ExecuteCount);
        Assert.Equal(WorkflowState.Suspended, runtime.StateMachine.CurrentState);
    }

    [Fact]
    public async Task CompensationVsRollback_ShouldSuspend_WhenCompensationThrows()
    {
        // Arrange
        using var runtime = new ActionRuntime(_executor, _permissionGate);
        var mockCompensation = new MockCompensationAction { ShouldThrow = true };

        var plan = new ExecutionPlan { PlanId = "failed_comp_test", Goal = "Test Compensation Failure" };
        
        var step1 = new ExecutionStep
        {
            Id = "step1",
            Action = new AutomationAction { ActionId = "act1", Type = ActionType.Wait, Value = "10", Permission = ActionPermission.AutoApproved },
            Status = StepStatus.Completed,
            Semantics = new MutationBoundarySemantics
            {
                IsCompensatable = true,
                CompensationAction = mockCompensation
            }
        };

        var step2 = new ExecutionStep
        {
            Id = "step2",
            Action = new AutomationAction { ActionId = "act2", Type = ActionType.Wait, Value = "10", Permission = ActionPermission.AutoApproved },
            Verifier = new MockVerifier { Result = false }
        };

        plan.Steps[step1.Id] = step1;
        plan.Steps[step2.Id] = step2;
        step2.DependsOn.Add(step1.Id);

        var context = new ExecutionContext();

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(() => runtime.ExecutePlanAsync(plan, context));

        Assert.Equal(1, mockCompensation.ExecuteCount);
        Assert.Equal(WorkflowState.Suspended, runtime.StateMachine.CurrentState);
    }

    [Fact]
    public async Task ContextualOverlays_ShouldEnforceAppScopedRules()
    {
        // Arrange
        var interruptGraph = new EnvironmentalInterruptGraph();
        var context = new ExecutionContext();

        // Policy 1: Banking scope -> Suspend on "Confirm closure"
        interruptGraph.RegisterPolicy(new ContextualOverlayPolicy
        {
            AppScope = "banking.com",
            Rule = SafetyRule.Suspend,
            Keywords = new List<string> { "Confirm closure" }
        });

        // Policy 2: Developer sandbox scope -> AutoDismiss on "Confirm closure"
        interruptGraph.RegisterPolicy(new ContextualOverlayPolicy
        {
            AppScope = "devenv",
            Rule = SafetyRule.AutoDismiss,
            Keywords = new List<string> { "Confirm closure" }
        });

        // Act & Assert 1: Banking context
        context.SetVariable("current_url", "https://banking.com/dashboard");
        var resultBanking = await interruptGraph.AssessAndHandleInterruptAsync(
            "chrome", "Confirm closure of accounts", context, CancellationToken.None);
        Assert.False(resultBanking); // Should Suspend / Human Required

        // Act & Assert 2: Devenv context
        context.SetVariable("current_url", "");
        context.SetVariable("AppName", "devenv");
        var resultDevenv = await interruptGraph.AssessAndHandleInterruptAsync(
            "devenv", "Confirm closure of file", context, CancellationToken.None);
        Assert.True(resultDevenv); // Should AutoDismiss
    }

    [Fact]
    public async Task DriftCorrelation_ShouldIsolateConfidenceDegradationByScope()
    {
        // Arrange
        using var runtime = new ActionRuntime(_executor, _permissionGate);
        var driftEngine = runtime.DriftCorrelationEngine;

        // Act: Degrade Chrome scope multiple times
        driftEngine.RecordObservation("step1", "Chrome", DriftType.Environmental, 0.9);
        driftEngine.RecordObservation("step2", "Chrome", DriftType.Environmental, 0.8);
        driftEngine.RecordObservation("step3", "Chrome", DriftType.Environmental, 0.9);

        // Assert: Chrome scope confidence is degraded
        double chromeConfidence = driftEngine.GetScopeConfidence("Chrome");
        Assert.True(chromeConfidence < 0.5);
        Assert.True(driftEngine.ShouldRecalibrate("Chrome"));

        // Assert: Word scope confidence remains untouched
        double wordConfidence = driftEngine.GetScopeConfidence("Word");
        Assert.Equal(1.0, wordConfidence);
        Assert.False(driftEngine.ShouldRecalibrate("Word"));

        // Assert: Running a step in degraded "Chrome" scope triggers immediate suspension
        var plan = new ExecutionPlan { PlanId = "drift_halt_test", Goal = "Halt degraded scope" };
        var step1 = new ExecutionStep
        {
            Id = "step1",
            Action = new AutomationAction { ActionId = "act1", Type = ActionType.Wait, Value = "10", Permission = ActionPermission.AutoApproved }
        };
        plan.Steps[step1.Id] = step1;

        var context = new ExecutionContext();
        context.SetVariable("AppName", "Chrome");

        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.ExecutePlanAsync(plan, context));
        Assert.Equal(WorkflowState.Suspended, runtime.StateMachine.CurrentState);
    }

    [Fact]
    public async Task Synchronization_ShouldSuspend_WhenCriticalDesynchronization()
    {
        // Arrange
        using var runtime = new ActionRuntime(_executor, _permissionGate);
        var eventBus = new InMemoryEventBus();
        var worldModel = new OperationalWorldModel(eventBus);
        var syncEngine = new EnvironmentSynchronizationEngine(worldModel, eventBus);
        runtime.SynchronizationEngine = syncEngine;

        var plan = new ExecutionPlan { PlanId = "sync_test", Goal = "Test Synchronization Gating" };
        var step1 = new ExecutionStep
        {
            Id = "step1",
            Action = new AutomationAction { ActionId = "act1", Type = ActionType.Wait, Value = "10", Permission = ActionPermission.AutoApproved }
        };
        plan.Steps[step1.Id] = step1;

        var context = new ExecutionContext();
        // Network offline is a Critical desynchronization precondition
        context.SetVariable("requires_network_online", true);
        worldModel.SetEnvironmentalConstraint("network_offline", "True");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.ExecutePlanAsync(plan, context));
        Assert.Contains("Critical environment desynchronization", ex.Message);
        Assert.Equal(WorkflowState.Suspended, runtime.StateMachine.CurrentState);
    }

    [Fact]
    public async Task FatigueGating_ShouldSuspend_WhenEpistemicFatigueExceeded()
    {
        // Arrange
        using var runtime = new ActionRuntime(_executor, _permissionGate);
        
        var plan = new ExecutionPlan { PlanId = "fatigue_test", Goal = "Test Epistemic Fatigue" };
        var step1 = new ExecutionStep
        {
            Id = "step1",
            Action = new AutomationAction { ActionId = "act1", Type = ActionType.Wait, Value = "10", Permission = ActionPermission.AutoApproved },
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-60)
        };
        plan.Steps[step1.Id] = step1;

        var context = new ExecutionContext();
        context.SetVariable("HumanCollisionCount", 5);

        runtime.PropagationLedger = new ExternalPropagationLedger(_tempDir);
        runtime.PropagationLedger.RecordPropagation("step1", "Download", "file1.txt", "Uncertain");
        runtime.PropagationLedger.RecordPropagation("step1", "Download", "file2.txt", "Uncertain");
        runtime.PropagationLedger.RecordPropagation("step1", "Download", "file3.txt", "Uncertain");
        runtime.PropagationLedger.RecordPropagation("step1", "Download", "file4.txt", "Uncertain");

        // Setup workflow identity envelope with high uncertainty count to trigger fatigue decay index < 0.3
        runtime.IdentityEnvelope = new WorkflowIdentityEnvelope
        {
            WorkflowId = "fatigue_test"
        };
        for (int i = 0; i < 7; i++)
        {
            runtime.IdentityEnvelope.UncertaintyLog.Add(new UncertaintyEvent
            {
                Level = UncertaintyLevel.U1_Observational,
                Reason = "Verification mismatch"
            });
        }
        for (int i = 0; i < 5; i++)
        {
            runtime.IdentityEnvelope.UncertaintyLog.Add(new UncertaintyEvent
            {
                Level = UncertaintyLevel.U1_Observational,
                Reason = "unexpected modal interrupt"
            });
        }

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.ExecutePlanAsync(plan, context));
        Assert.Contains("Extreme epistemic fatigue", ex.Message);
        Assert.Equal(WorkflowState.Suspended, runtime.StateMachine.CurrentState);
    }

    [Fact]
    public void ExplainabilityCompression_ShouldTranslateEpistemicFailureToCalmNeutralStatement()
    {
        // Arrange
        var engine = new RecoveryLegibilityEngine();

        // Act & Assert 1: Desynchronization
        var desc1 = engine.TranslateFailure("Critical environment desynchronization (Sovereignty)", "");
        Assert.Equal("The task paused because the expected application state could no longer be confirmed.", desc1);

        // Act & Assert 2: Epistemic fatigue
        var desc2 = engine.TranslateFailure("Extreme epistemic fatigue (decay factor: 0.25)", "");
        Assert.Equal("Too many verification mismatches accumulated during execution.", desc2);

        // Act & Assert 3: Compensation/Irrecoverable
        var desc3 = engine.TranslateFailure("Compensation failed or is irrecoverable", "");
        Assert.Equal("The environment changed in a way that made the workflow unsafe to continue.", desc3);

        // Act & Assert 4: Drift
        var desc4 = engine.TranslateFailure("Systemic platform drift detected", "");
        Assert.Equal("Systemic platform drift prevented execution continuity.", desc4);
    }

    [Fact]
    public async Task RecoveryReconciliation_ShouldReassessAndResolvePropagation()
    {
        // Arrange
        var persistenceStore = new WorkflowPersistenceStore(_tempDir);
        using var runtime = new ActionRuntime(_executor, _permissionGate);
        var eventBus = new InMemoryEventBus();
        var worldModel = new OperationalWorldModel(eventBus);
        var wr = new WorkflowRuntime(persistenceStore, runtime, worldModel);
        
        var plan = new ExecutionPlan { PlanId = "recon_test", Goal = "Test Reconciliation" };
        var step1 = new ExecutionStep
        {
            Id = "step1",
            Action = new AutomationAction { ActionId = "act1", Type = ActionType.Wait, Value = "10", Permission = ActionPermission.AutoApproved }
        };
        plan.Steps[step1.Id] = step1;

        var context = new ExecutionContext();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Run to save initial checkpoint
        await Assert.ThrowsAnyAsync<Exception>(() => wr.StartWorkflowAsync("recon_test", plan, context, cts.Token));

        // Mark a propagation record as Uncertain
        runtime.PropagationLedger.RecordPropagation("step1", "Download", "temp_file.txt", "Uncertain");

        // Set up synchronization engine
        var syncEngine = new EnvironmentSynchronizationEngine(worldModel, eventBus);
        runtime.SynchronizationEngine = syncEngine;

        // Restore using Recovery Reconciliation Protocol
        var newContext = new ExecutionContext();
        
        // Create a temporary file to reconcile the download propagation
        string filePath = Path.Combine(_tempDir, "temp_file.txt");
        File.WriteAllText(filePath, "test content");
        
        runtime.PropagationLedger.RecordPropagation("step1", "Download", filePath, "Uncertain");

        await Assert.ThrowsAnyAsync<Exception>(() => wr.RestoreWorkflowAsync("recon_test", newContext, cts.Token));

        var record = runtime.PropagationLedger.GetRecords().FirstOrDefault(r => r.DestinationValue == filePath);
        Assert.NotNull(record);
        Assert.Equal("Propagated", record.Status);
        Assert.Contains("Reconciled via filesystem check", record.CompensationDetails);
    }

    [Fact]
    public async Task AdaptiveCognitiveCompression_ShouldBlockUnapprovedExternalAction_WhenFatigued()
    {
        // Arrange
        using var runtime = new ActionRuntime(_executor, _permissionGate);
        runtime.PropagationLedger = new ExternalPropagationLedger(_tempDir);
        
        var plan = new ExecutionPlan { PlanId = "fatigue_test", Goal = "Test Cognitive Compression" };
        var step1 = new ExecutionStep
        {
            Id = "step1",
            Action = new AutomationAction { ActionId = "act1", Type = ActionType.Upload, Value = "file.txt", Permission = ActionPermission.AutoApproved },
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-60)
        };
        plan.Steps[step1.Id] = step1;

        var context = new ExecutionContext();
        context.SetVariable("HumanCollisionCount", 5);

        // Setup workflow identity envelope with uncertainty count to trigger fatigue decay index < 0.6
        runtime.IdentityEnvelope = new WorkflowIdentityEnvelope
        {
            WorkflowId = "fatigue_test"
        };
        for (int i = 0; i < 7; i++)
        {
            runtime.IdentityEnvelope.UncertaintyLog.Add(new UncertaintyEvent
            {
                Level = UncertaintyLevel.U1_Observational,
                Reason = "Verification mismatch"
            });
        }

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.ExecutePlanAsync(plan, context));
        Assert.Contains("Blocked unapproved external action", ex.Message);
        Assert.Equal(WorkflowState.Suspended, runtime.StateMachine.CurrentState);
    }

    [Fact]
    public void PhaseRelativeDivergence_ShouldClassifyBenignInResearchButHostileInPayment()
    {
        // Arrange
        var interpreter = new EnvironmentalDivergenceInterpreter();
        var div = new EnvironmentDivergence
        {
            Source = "workflow",
            Expected = "tab1",
            Actual = "tab2"
        };

        // Act & Assert 1: Research Phase (Downgraded to Instability)
        var contextResearch = new ExecutionContext();
        contextResearch.SetVariable("WorkflowNarrativePhase", "Research");
        var res1 = interpreter.Interpret(div, contextResearch);
        Assert.Equal(DivergenceInterpretation.Instability, res1);

        // Act & Assert 2: Payment Phase (Maintained Sovereignty)
        var contextPayment = new ExecutionContext();
        contextPayment.SetVariable("WorkflowNarrativePhase", "Payment");
        var res2 = interpreter.Interpret(div, contextPayment);
        Assert.Equal(DivergenceInterpretation.Sovereignty, res2);
    }

    [Fact]
    public async Task ProgressiveExplainability_ShouldGenerateFourTierReport()
    {
        // Arrange
        var engine = new RecoveryLegibilityEngine();
        using var runtime = new ActionRuntime(_executor, _permissionGate);
        
        var plan = new ExecutionPlan { PlanId = "exp_test", Goal = "Test Explainability" };
        var step1 = new ExecutionStep
        {
            Id = "step1",
            Action = new AutomationAction { ActionId = "act1", Type = ActionType.Wait, Value = "10", Permission = ActionPermission.AutoApproved }
        };
        plan.Steps[step1.Id] = step1;
        
        var context = new ExecutionContext();
        runtime.IdentityEnvelope = new WorkflowIdentityEnvelope { WorkflowId = "exp_test" };
        
        // Populate plan/context into runtime
        try { await runtime.ExecutePlanAsync(plan, context); } catch { }

        // Act
        var report = engine.GenerateReport("Critical environment desynchronization (Sovereignty)", "Exception details here", runtime);

        // Assert
        Assert.NotNull(report);
        Assert.Equal("The task paused because the expected application state could no longer be confirmed.", report.CalmSummary);
        Assert.Contains("Divergence detected", report.OperationalDetail);
        Assert.Contains("Critical environment desynchronization", report.CausalTrace);
        Assert.Contains("TemporalEntropy", report.FullEpistemicGraph);
    }

    [Fact]
    public async Task IntentValidityReassessment_ShouldFail_WhenFinalStepAlreadyVerified()
    {
        // Arrange
        var persistenceStore = new WorkflowPersistenceStore(_tempDir);
        using var runtime = new ActionRuntime(_executor, _permissionGate);
        var eventBus = new InMemoryEventBus();
        var worldModel = new OperationalWorldModel(eventBus);
        var wr = new WorkflowRuntime(persistenceStore, runtime, worldModel);
        
        var plan = new ExecutionPlan { PlanId = "intent_test1", Goal = "Test Obsolete" };
        var step1 = new ExecutionStep
        {
            Id = "step1",
            Action = new AutomationAction { ActionId = "act1", Type = ActionType.Wait, Value = "10", Permission = ActionPermission.Approved },
            Verifier = new MockVerifier { Result = true } // Report success immediately
        };
        plan.Steps[step1.Id] = step1;

        var context = new ExecutionContext();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Run to save initial checkpoint
        await Assert.ThrowsAnyAsync<Exception>(() => wr.StartWorkflowAsync("intent_test1", plan, context, cts.Token));

        // Restore: Intent Validity Reassessment should check final step, see it's satisfied, and throw
        var newContext = new ExecutionContext();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => wr.RestoreWorkflowAsync("intent_test1", newContext, CancellationToken.None));
        Assert.Contains("Goal already satisfied manually", ex.Message);
        Assert.Equal("Goal already satisfied", newContext.GetVariable<string>("IntentValidityObsolete"));
    }

    [Fact]
    public async Task IntentValidityReassessment_ShouldFail_WhenSuspendedTooLong()
    {
        // Arrange
        var persistenceStore = new WorkflowPersistenceStore(_tempDir);
        using var runtime = new ActionRuntime(_executor, _permissionGate);
        var eventBus = new InMemoryEventBus();
        var worldModel = new OperationalWorldModel(eventBus);
        var wr = new WorkflowRuntime(persistenceStore, runtime, worldModel);
        
        var plan = new ExecutionPlan { PlanId = "intent_test2", Goal = "Test Long Suspension" };
        var step1 = new ExecutionStep
        {
            Id = "step1",
            Action = new AutomationAction { ActionId = "act1", Type = ActionType.Wait, Value = "10", Permission = ActionPermission.Approved }
        };
        plan.Steps[step1.Id] = step1;

        var context = new ExecutionContext();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Run to save initial checkpoint
        await Assert.ThrowsAnyAsync<Exception>(() => wr.StartWorkflowAsync("intent_test2", plan, context, cts.Token));

        // Mark SuspendedAt as 5 hours ago in context
        var newContext = new ExecutionContext();
        newContext.SetVariable("SuspendedAt", DateTimeOffset.UtcNow.AddHours(-5).ToString("o"));

        // Restore: should throw due to suspension timeout
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => wr.RestoreWorkflowAsync("intent_test2", newContext, CancellationToken.None));
        Assert.Contains("suspended for too long", ex.Message);
        Assert.Equal("Suspended for too long", newContext.GetVariable<string>("IntentValidityObsolete"));
    }
}
