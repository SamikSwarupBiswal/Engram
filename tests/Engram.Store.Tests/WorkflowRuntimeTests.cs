using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Engram.Store.Automation;
using Engram.Store.Events;
using Xunit;
using ExecutionContext = Engram.Store.Automation.ExecutionContext;

namespace Engram.Store.Tests;

public class WorkflowRuntimeTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ActionExecutor _executor = new();
    private readonly PermissionGate _permissionGate = new();

    public WorkflowRuntimeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"engram_wf_tests_{Guid.NewGuid():n}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch { }
        }
    }

    [Fact]
    public async Task WorkflowPersistenceStore_SaveAndLoadCheckpoint_SavesCorrectly()
    {
        // Arrange
        var store = new WorkflowPersistenceStore(_tempDir);
        var checkpoint = new WorkflowCheckpoint
        {
            WorkflowId = "wf-test-1",
            Goal = "Test Goal",
            CurrentPhase = "ExecutingSteps",
            CurrentStepIndex = 1,
            ActiveStepId = "step_2",
            Variables = new Dictionary<string, string> { ["v1"] = "val1" },
            ExecutedStepIds = new List<string> { "step_1" },
            PlanJson = "{}"
        };

        // Act
        await store.SaveCheckpointAsync(checkpoint);
        var loaded = await store.LoadCheckpointAsync("wf-test-1");

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal(checkpoint.WorkflowId, loaded!.WorkflowId);
        Assert.Equal(checkpoint.Goal, loaded.Goal);
        Assert.Equal(checkpoint.CurrentPhase, loaded.CurrentPhase);
        Assert.Equal(checkpoint.CurrentStepIndex, loaded.CurrentStepIndex);
        Assert.Equal(checkpoint.ActiveStepId, loaded.ActiveStepId);
        Assert.Equal("val1", loaded.Variables["v1"]);
        Assert.Equal("step_1", loaded.ExecutedStepIds[0]);

        var all = await store.ListCheckpointsAsync();
        Assert.Single(all);
    }

    [Fact]
    public async Task WorkflowRuntime_StartWorkflow_ExecutesHappyPathAndCleansCheckpoint()
    {
        // Arrange
        var store = new WorkflowPersistenceStore(_tempDir);
        var actionRuntime = new ActionRuntime(_executor, _permissionGate);
        var eventBus = new InMemoryEventBus();
        var worldModel = new OperationalWorldModel(eventBus);
        var runtime = new WorkflowRuntime(store, actionRuntime, worldModel);

        var plan = new ExecutionPlan { Goal = "Happy Goal" };
        var step1 = new ExecutionStep
        {
            Id = "step_1",
            Action = new AutomationAction
            {
                Type = ActionType.Wait,
                Value = "5",
                Permission = ActionPermission.AutoApproved
            }
        };
        plan.Steps[step1.Id] = step1;
        var context = new ExecutionContext();

        // Act
        await runtime.StartWorkflowAsync("workflow-happy", plan, context, CancellationToken.None);

        // Assert
        Assert.Equal(StepStatus.Completed, step1.Status);
        Assert.Equal("Completed", worldModel.CurrentPhase);
        Assert.Equal(1.0, worldModel.ExecutionConfidence);
        
        // Checkpoint should be deleted on success
        var cp = await store.LoadCheckpointAsync("workflow-happy");
        Assert.Null(cp);
    }

    [Fact]
    public async Task WorkflowRuntime_StartWorkflow_OnFailure_SavesCheckpoint()
    {
        // Arrange
        var store = new WorkflowPersistenceStore(_tempDir);
        var actionRuntime = new ActionRuntime(_executor, _permissionGate);
        var eventBus = new InMemoryEventBus();
        var worldModel = new OperationalWorldModel(eventBus);
        var runtime = new WorkflowRuntime(store, actionRuntime, worldModel);

        var plan = new ExecutionPlan { Goal = "Failing Goal" };
        var step1 = new ExecutionStep
        {
            Id = "step_1",
            // Requires approval but we run auto-approved execution without approving => fails
            Action = new AutomationAction
            {
                Type = ActionType.Click,
                Target = new ActionTarget { Selector = "button" },
                Permission = ActionPermission.Pending
            }
        };
        plan.Steps[step1.Id] = step1;
        var context = new ExecutionContext();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.StartWorkflowAsync("workflow-fail", plan, context, CancellationToken.None));

        Assert.Equal("Failed", worldModel.CurrentPhase);
        Assert.Equal(0.0, worldModel.ExecutionConfidence);

        // Checkpoint should exist and capture failure phase
        var cp = await store.LoadCheckpointAsync("workflow-fail");
        Assert.NotNull(cp);
        Assert.Equal("Failed", cp!.CurrentPhase);
        Assert.Contains("action is not approved", cp.PlanJson);
    }

    [Fact]
    public async Task WorkflowRuntime_PauseAndRestore_ResumesSuccessfully()
    {
        // Arrange
        var store = new WorkflowPersistenceStore(_tempDir);
        var actionRuntime = new ActionRuntime(_executor, _permissionGate);
        var eventBus = new InMemoryEventBus();
        var worldModel = new OperationalWorldModel(eventBus);
        var runtime = new WorkflowRuntime(store, actionRuntime, worldModel);

        var plan = new ExecutionPlan { Goal = "Pause Goal" };
        var step1 = new ExecutionStep
        {
            Id = "step_1",
            Action = new AutomationAction { Type = ActionType.Wait, Value = "1000", Permission = ActionPermission.AutoApproved }
        };
        var step2 = new ExecutionStep
        {
            Id = "step_2",
            Action = new AutomationAction { Type = ActionType.Wait, Value = "5", Permission = ActionPermission.AutoApproved },
            DependsOn = new List<string> { "step_1" }
        };
        plan.Steps[step1.Id] = step1;
        plan.Steps[step2.Id] = step2;
        var context = new ExecutionContext();
        context.SetVariable("testKey", "testVal");

        // Run workflow in background, pause it, then restore it in a new runtime/context
        var startTask = runtime.StartWorkflowAsync("workflow-pause", plan, context, CancellationToken.None);
        await Task.Delay(100); // Let step1 start executing
        await runtime.PauseWorkflowAsync();

        try
        {
            await startTask;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
        catch (Exception)
        {
            // We might throw if paused
        }

        // Verify paused checkpoint was saved
        var cp = await store.LoadCheckpointAsync("workflow-pause");
        Assert.NotNull(cp);
        Assert.Equal("Paused", cp!.CurrentPhase);

        // Restore in a fresh runtime/context
        var freshStore = new WorkflowPersistenceStore(_tempDir);
        var freshActionRuntime = new ActionRuntime(_executor, _permissionGate);
        var freshWorldModel = new OperationalWorldModel(eventBus);
        var freshRuntime = new WorkflowRuntime(freshStore, freshActionRuntime, freshWorldModel);
        var freshContext = new ExecutionContext();

        // Act
        await freshRuntime.RestoreWorkflowAsync("workflow-pause", freshContext, CancellationToken.None);

        // Assert
        Assert.Equal("Completed", freshWorldModel.CurrentPhase);
        Assert.Equal("testVal", freshContext.GetVariable<string>("testKey"));
        var cleanedCp = await freshStore.LoadCheckpointAsync("workflow-pause");
        Assert.Null(cleanedCp); // Cleaned up after completion
    }
}
