using System;
using System.Collections.Generic;
using Engram.Store.Wiki;

namespace Engram.Store.Governance;

public class GraphMutationRecord
{
    public string MutationId { get; set; } = Guid.NewGuid().ToString("n");
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string NodeId { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty; // "create", "update", "delete"
    public string? BackupNodeJson { get; set; } // Serialization of node before modification
}

public class SemanticRollbackEngine
{
    private readonly WikiNodeStore _nodeStore;
    private readonly List<GraphMutationRecord> _mutationsLog = new();
    private readonly object _lock = new();

    public SemanticRollbackEngine(WikiNodeStore nodeStore)
    {
        _nodeStore = nodeStore;
    }

    public void TrackMutation(string nodeId, string operationType, WikiNode? preMutationNode)
    {
        var record = new GraphMutationRecord
        {
            NodeId = nodeId,
            OperationType = operationType
        };

        if (preMutationNode != null)
        {
            // Serialize node to JSON for restoration
            record.BackupNodeJson = System.Text.Json.JsonSerializer.Serialize(preMutationNode);
        }

        lock (_lock)
        {
            _mutationsLog.Add(record);
            // Cap history
            if (_mutationsLog.Count > 500)
            {
                _mutationsLog.RemoveAt(0);
            }
        }
    }

    public void RollbackLastMutations(int count = 1)
    {
        lock (_lock)
        {
            if (_mutationsLog.Count == 0) return;

            var targetCount = Math.Min(count, _mutationsLog.Count);
            for (int i = 0; i < targetCount; i++)
            {
                var record = _mutationsLog[^1];
                _mutationsLog.RemoveAt(_mutationsLog.Count - 1);

                try
                {
                    if (record.OperationType == "create")
                    {
                        // Rollback create by deleting the node
                        _nodeStore.Delete(record.NodeId);
                    }
                    else if (record.OperationType == "update" && record.BackupNodeJson != null)
                    {
                        // Rollback update by restoring the backup JSON
                        var node = System.Text.Json.JsonSerializer.Deserialize<WikiNode>(record.BackupNodeJson);
                        if (node != null)
                        {
                            _nodeStore.Save(node);
                        }
                    }
                    else if (record.OperationType == "delete" && record.BackupNodeJson != null)
                    {
                        // Rollback delete by recreating the node
                        var node = System.Text.Json.JsonSerializer.Deserialize<WikiNode>(record.BackupNodeJson);
                        if (node != null)
                        {
                            _nodeStore.Save(node);
                        }
                    }
                }
                catch
                {
                    // Continue rolling back other records even if one fails
                }
            }
        }
    }

    public void ClearHistory()
    {
        lock (_lock)
        {
            _mutationsLog.Clear();
        }
    }
}
