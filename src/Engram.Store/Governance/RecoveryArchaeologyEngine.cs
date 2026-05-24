using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Engram.Store.Governance;

public class PathologyRecord
{
    public string RecordId { get; set; } = Guid.NewGuid().ToString("n");
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string ComponentName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ExceptionDetails { get; set; } = string.Empty;
    public bool IsResolved { get; set; }
}

public class RecoveryArchaeologyEngine
{
    private readonly string _filePath;
    private readonly List<PathologyRecord> _records = new();
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public RecoveryArchaeologyEngine(WorkspacePaths paths)
    {
        var dir = Path.Combine(paths.Config, "governance");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "archaeology.json");
        LoadRecords();
    }

    public void RecordPathology(string component, string desc, Exception? ex = null)
    {
        var record = new PathologyRecord
        {
            ComponentName = component,
            Description = desc,
            ExceptionDetails = ex?.ToString() ?? string.Empty
        };

        lock (_lock)
        {
            _records.Add(record);
            if (_records.Count > 200)
            {
                _records.RemoveAt(0);
            }
            SaveRecords();
        }
    }

    public void ResolvePathology(string recordId)
    {
        lock (_lock)
        {
            var record = _records.Find(r => string.Equals(r.RecordId, recordId, StringComparison.OrdinalIgnoreCase));
            if (record != null)
            {
                record.IsResolved = true;
                SaveRecords();
            }
        }
    }

    public IReadOnlyList<PathologyRecord> GetActivePathologies()
    {
        lock (_lock)
        {
            return _records.FindAll(r => !r.IsResolved);
        }
    }

    public IReadOnlyList<PathologyRecord> GetAllRecords()
    {
        lock (_lock)
        {
            return _records.ToArray();
        }
    }

    private void LoadRecords()
    {
        lock (_lock)
        {
            if (!File.Exists(_filePath)) return;
            try
            {
                var json = File.ReadAllText(_filePath);
                var loaded = JsonSerializer.Deserialize<List<PathologyRecord>>(json, JsonOptions);
                if (loaded != null)
                {
                    _records.Clear();
                    _records.AddRange(loaded);
                }
            }
            catch
            {
                // Graceful fallback
            }
        }
    }

    private void SaveRecords()
    {
        lock (_lock)
        {
            try
            {
                var tmpPath = _filePath + ".tmp";
                var json = JsonSerializer.Serialize(_records, JsonOptions);
                File.WriteAllText(tmpPath, json);
                File.Move(tmpPath, _filePath, overwrite: true);
            }
            catch
            {
                // Graceful fallback
            }
        }
    }
}
