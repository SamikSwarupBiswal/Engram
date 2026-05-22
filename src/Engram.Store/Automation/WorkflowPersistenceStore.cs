using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Engram.Store.Automation;

/// <summary>
/// Saves and loads workflow checkpoints under .engram/automation/workflows/
/// </summary>
public class WorkflowPersistenceStore
{
    private readonly string _storeDir;

    public WorkflowPersistenceStore(string? baseDir = null)
    {
        var baseDirectory = baseDir ?? Directory.GetCurrentDirectory();
        _storeDir = Path.Combine(baseDirectory, ".engram", "automation", "workflows");
    }

    public string StoreDirectory => _storeDir;

    public async Task SaveCheckpointAsync(WorkflowCheckpoint checkpoint)
    {
        if (checkpoint == null) throw new ArgumentNullException(nameof(checkpoint));
        if (string.IsNullOrEmpty(checkpoint.WorkflowId)) throw new ArgumentException("WorkflowId cannot be empty");

        Directory.CreateDirectory(_storeDir);

        var filePath = Path.Combine(_storeDir, $"{checkpoint.WorkflowId}.json");
        var json = JsonSerializer.Serialize(checkpoint, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(filePath, json);
    }

    public async Task<WorkflowCheckpoint?> LoadCheckpointAsync(string workflowId)
    {
        if (string.IsNullOrEmpty(workflowId)) throw new ArgumentException("WorkflowId cannot be empty");

        var filePath = Path.Combine(_storeDir, $"{workflowId}.json");
        if (!File.Exists(filePath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(filePath);
        return JsonSerializer.Deserialize<WorkflowCheckpoint>(json);
    }

    public async Task<List<WorkflowCheckpoint>> ListCheckpointsAsync()
    {
        var list = new List<WorkflowCheckpoint>();
        if (!Directory.Exists(_storeDir))
        {
            return list;
        }

        var files = Directory.GetFiles(_storeDir, "*.json");
        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var cp = JsonSerializer.Deserialize<WorkflowCheckpoint>(json);
                if (cp != null)
                {
                    list.Add(cp);
                }
            }
            catch
            {
                // Ignore corrupt or invalid json files
            }
        }

        return list;
    }

    public void DeleteCheckpoint(string workflowId)
    {
        var filePath = Path.Combine(_storeDir, $"{workflowId}.json");
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }
}
