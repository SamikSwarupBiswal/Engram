using System;
using System.Text.Json;

namespace Engram.Store.Automation;

public class WorkflowContinuitySnapshots
{
    private readonly WorkflowPersistenceStore _persistenceStore;

    public WorkflowContinuitySnapshots(WorkflowPersistenceStore persistenceStore)
    {
        _persistenceStore = persistenceStore ?? throw new ArgumentNullException(nameof(persistenceStore));
    }

    public async System.Threading.Tasks.Task SaveSnapshotAsync(WorkflowRuntime runtime, string reason)
    {
        if (runtime == null) throw new ArgumentNullException(nameof(runtime));
        await runtime.CreateCheckpointAsync(reason);
    }

    public async System.Threading.Tasks.Task<WorkflowCheckpoint?> LoadSnapshotAsync(string workflowId)
    {
        if (string.IsNullOrEmpty(workflowId)) return null;
        return await _persistenceStore.LoadCheckpointAsync(workflowId);
    }

    public bool ValidateSnapshotResumability(WorkflowCheckpoint checkpoint, out string validationError)
    {
        validationError = string.Empty;
        if (checkpoint == null)
        {
            validationError = "Checkpoint is null";
            return false;
        }

        if (string.IsNullOrEmpty(checkpoint.WorkflowId))
        {
            validationError = "WorkflowId is empty";
            return false;
        }

        if (string.IsNullOrEmpty(checkpoint.PlanJson))
        {
            validationError = "PlanJson is empty";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(checkpoint.PlanJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("PlanId", out _))
            {
                validationError = "PlanJson missing PlanId";
                return false;
            }
            if (!root.TryGetProperty("Steps", out var steps) || steps.ValueKind != JsonValueKind.Array)
            {
                validationError = "PlanJson missing or invalid Steps array";
                return false;
            }
        }
        catch (JsonException ex)
        {
            validationError = $"Invalid PlanJson serialization: {ex.Message}";
            return false;
        }

        return true;
    }
}
