using System.Text.Json;

namespace Engram.Store;

/// <summary>
/// Manages per-event processing sidecar files (.meta.json).
/// The sidecar tracks mutable processing state without modifying the immutable raw event payload.
/// </summary>
public class ProcessingSidecar
{
    private readonly WorkspacePaths _paths;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public ProcessingSidecar(WorkspacePaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    /// <summary>
    /// Writes processing state to a .meta.json sidecar file adjacent to the event file.
    /// </summary>
    public string Write(string eventFilePath, ProcessingState state)
    {
        var sidecarPath = GetSidecarPath(eventFilePath);
        var json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(sidecarPath, json);
        return sidecarPath;
    }

    /// <summary>
    /// Reads processing state from the sidecar file. Returns null if no sidecar exists.
    /// </summary>
    public ProcessingState? Read(string eventFilePath)
    {
        var sidecarPath = GetSidecarPath(eventFilePath);

        if (!File.Exists(sidecarPath))
            return null;

        var json = File.ReadAllText(sidecarPath);
        return JsonSerializer.Deserialize<ProcessingState>(json, JsonOptions);
    }

    private static string GetSidecarPath(string eventFilePath)
    {
        return eventFilePath + ".meta.json";
    }
}
