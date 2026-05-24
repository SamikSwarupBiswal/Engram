using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Engram.Store.Automation;

public class CompensationPlanner
{
    private readonly ConcurrentDictionary<string, Func<ExecutionContext, CancellationToken, Task<bool>>> _compensations = new();

    public void RegisterCompensation(string stepId, Func<ExecutionContext, CancellationToken, Task<bool>> compensationAction)
    {
        _compensations[stepId] = compensationAction;
    }

    public async Task<bool> ExecuteCompensationAsync(string workflowId, string stepId, ExecutionContext context, CancellationToken ct = default)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));

        if (_compensations.TryGetValue(stepId, out var action))
        {
            try
            {
                return await action(context, ct);
            }
            catch
            {
                return false;
            }
        }

        // Default compensation behavior if none registered: log warning and mark as completed
        context.SetVariable($"compensation_{stepId}_status", "CompensationExecutedHeuristically");
        return true;
    }
}
