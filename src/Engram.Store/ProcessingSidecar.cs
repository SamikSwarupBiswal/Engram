using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Engram.Store;

/// <summary>
/// Production-grade processing sidecar.
/// Features: atomic writes, versioning, structured logging, IDisposable.
/// </summary>
public class ProcessingSidecar : IDisposable
{
    private readonly WorkspacePaths _paths;
    private readonly ILogger<ProcessingSidecar>? _logger;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public ProcessingSidecar(WorkspacePaths paths, ILogger<ProcessingSidecar>? logger = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _logger = logger;
    }

    /// <summary>
    /// Writes processing state to .meta.json sidecar using atomic write (tmp + rename).
    /// </summary>
    public string Write(string eventFilePath, ProcessingState state)
    {
        var sidecarPath = GetSidecarPath(eventFilePath);
        state.Version = ProcessingState.CurrentVersion;

        var tmpPath = sidecarPath + ".tmp";
        var json = JsonSerializer.Serialize(state, JsonOptions);

        File.WriteAllText(tmpPath, json);
        File.Move(tmpPath, sidecarPath, overwrite: true);

        _logger?.LogDebug("Sidecar written for {EventPath}: status={Status}", eventFilePath, state.Status);
        return sidecarPath;
    }

    /// <summary>
    /// Reads processing state from sidecar. Returns null if no sidecar exists.
    /// Handles version mismatch gracefully.
    /// </summary>
    public ProcessingState? Read(string eventFilePath)
    {
        var sidecarPath = GetSidecarPath(eventFilePath);

        if (!File.Exists(sidecarPath))
            return null;

        try
        {
            var json = File.ReadAllText(sidecarPath);
            var state = JsonSerializer.Deserialize<ProcessingState>(json, JsonOptions);

            if (state == null) return null;

            // Handle version mismatch
            if (state.Version != ProcessingState.CurrentVersion)
            {
                _logger?.LogWarning("Sidecar version mismatch for {Path}: found v{Found}, expected v{Expected}",
                    eventFilePath, state.Version, ProcessingState.CurrentVersion);
                // Future: migrate old versions here
            }

            return state;
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "Corrupted sidecar file: {Path}", sidecarPath);
            return null;
        }
        catch (IOException ex)
        {
            _logger?.LogWarning(ex, "IO error reading sidecar: {Path}", sidecarPath);
            return null;
        }
    }

    private static string GetSidecarPath(string eventFilePath)
    {
        return eventFilePath + ".meta.json";
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
