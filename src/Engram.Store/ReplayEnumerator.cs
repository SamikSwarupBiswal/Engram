using System.Text.Json;

namespace Engram.Store;

/// <summary>
/// Enumerates persisted raw events from .engram/raw/ in deterministic order.
/// Order: sorted by date folder (YYYY-MM-DD), then by event file name within each folder.
/// </summary>
public class ReplayEnumerator
{
    private readonly WorkspacePaths _paths;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    public ReplayEnumerator(WorkspacePaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    /// <summary>
    /// Enumerates all raw events from .engram/raw/ in deterministic order.
    /// Returns empty list if the raw directory doesn't exist or is empty.
    /// Never modifies raw event files.
    /// </summary>
    public IReadOnlyList<RawEvent> EnumerateAll()
    {
        var result = new List<RawEvent>();

        if (!Directory.Exists(_paths.Raw))
            return result;

        // Sort by date folder name, then by file name within each folder
        var dateDirs = Directory.EnumerateDirectories(_paths.Raw)
            .OrderBy(d => Path.GetFileName(d));

        foreach (var dateDir in dateDirs)
        {
            var files = Directory.EnumerateFiles(dateDir, "*.json")
                .OrderBy(f => Path.GetFileName(f));

            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var rawEvent = JsonSerializer.Deserialize<RawEvent>(json, JsonOptions);
                    if (rawEvent != null)
                    {
                        result.Add(rawEvent);
                    }
                }
                catch
                {
                    // Skip malformed JSON files silently
                    continue;
                }
            }
        }

        return result;
    }
}
