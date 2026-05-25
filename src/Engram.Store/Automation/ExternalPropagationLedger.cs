using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Engram.Store.Automation;

public class PropagationRecord
{
    public string PropagationId { get; set; } = string.Empty;
    public string StepId { get; set; } = string.Empty;
    public string DestinationType { get; set; } = string.Empty;
    public string DestinationValue { get; set; } = string.Empty;
    public string Status { get; set; } = "Uncertain"; // Uncertain, Propagated, Compensated, Failed
    public string CompensationDetails { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

public class ExternalPropagationLedger
{
    private readonly string _filePath;
    private readonly ConcurrentDictionary<string, PropagationRecord> _records = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public ExternalPropagationLedger(string? baseDir = null)
    {
        var baseDirectory = baseDir ?? Directory.GetCurrentDirectory();
        _filePath = Path.Combine(baseDirectory, ".engram", "automation", "propagation_ledger.json");
        Load();
    }

    private void Load()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    var list = JsonSerializer.Deserialize<List<PropagationRecord>>(json);
                    if (list != null)
                    {
                        foreach (var record in list)
                        {
                            _records[record.PropagationId] = record;
                        }
                    }
                }
            }
            catch
            {
                // Ignore loading error
            }
        }
    }

    private void Save()
    {
        lock (_lock)
        {
            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (dir != null && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                var list = new List<PropagationRecord>(_records.Values);
                var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch
            {
                // Ignore save errors
            }
        }
    }

    public void RecordPropagation(string stepId, string destinationType, string destinationValue, string status = "Uncertain")
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        var record = new PropagationRecord
        {
            PropagationId = id,
            StepId = stepId,
            DestinationType = destinationType,
            DestinationValue = destinationValue,
            Status = status,
            Timestamp = DateTimeOffset.UtcNow
        };
        _records[id] = record;
        Save();
    }

    public void UpdateStatus(string propagationId, string status, string compensationDetails = "")
    {
        if (_records.TryGetValue(propagationId, out var record))
        {
            record.Status = status;
            if (!string.IsNullOrEmpty(compensationDetails))
            {
                record.CompensationDetails = compensationDetails;
            }
            Save();
        }
    }

    public List<PropagationRecord> GetRecords()
    {
        return new List<PropagationRecord>(_records.Values);
    }
}
