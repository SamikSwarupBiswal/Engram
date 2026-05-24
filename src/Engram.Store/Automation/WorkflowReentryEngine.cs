using System;
using System.Threading;
using System.Threading.Tasks;

namespace Engram.Store.Automation;

public class WorkflowReentryEngine
{
    private readonly WorkflowRuntime _workflowRuntime;
    private readonly WorkflowPersistenceStore _persistenceStore;

    public WorkflowReentryEngine(WorkflowRuntime workflowRuntime, WorkflowPersistenceStore persistenceStore)
    {
        _workflowRuntime = workflowRuntime ?? throw new ArgumentNullException(nameof(workflowRuntime));
        _persistenceStore = persistenceStore ?? throw new ArgumentNullException(nameof(persistenceStore));
    }

    public async Task<bool> RehydrateAndResumeAsync(string workflowId, ExecutionContext context, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(workflowId)) return false;

        var checkpoint = await _persistenceStore.LoadCheckpointAsync(workflowId);
        if (checkpoint == null)
        {
            return false;
        }

        try
        {
            await _workflowRuntime.RestoreWorkflowAsync(workflowId, context, ct);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
