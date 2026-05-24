using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Engram.Store.Automation;

public class TransactionalWorkflowEnvelope
{
    private readonly ConcurrentDictionary<string, List<string>> _transactionSteps = new();
    private readonly ConcurrentDictionary<string, string> _rollbackAnchors = new();
    private readonly ConcurrentDictionary<string, bool> _committedTransactions = new();

    public void BeginTransaction(string workflowId, string initialRollbackAnchor)
    {
        _transactionSteps[workflowId] = new List<string>();
        _rollbackAnchors[workflowId] = initialRollbackAnchor;
        _committedTransactions[workflowId] = false;
    }

    public void RecordStep(string workflowId, string stepId)
    {
        if (_transactionSteps.TryGetValue(workflowId, out var steps))
        {
            steps.Add(stepId);
        }
    }

    public void CommitTransaction(string workflowId)
    {
        _committedTransactions[workflowId] = true;
    }

    public bool IsCommitted(string workflowId)
    {
        return _committedTransactions.TryGetValue(workflowId, out var val) && val;
    }

    public string? GetRollbackAnchor(string workflowId)
    {
        _rollbackAnchors.TryGetValue(workflowId, out var anchor);
        return anchor;
    }

    public List<string> GetTransactionSteps(string workflowId)
    {
        if (_transactionSteps.TryGetValue(workflowId, out var steps))
        {
            return new List<string>(steps);
        }
        return new List<string>();
    }

    public void ClearTransaction(string workflowId)
    {
        _transactionSteps.TryRemove(workflowId, out _);
        _rollbackAnchors.TryRemove(workflowId, out _);
        _committedTransactions.TryRemove(workflowId, out _);
    }
}
