using System.Text.Json;

namespace Engram.Store;

/// <summary>
/// Writes raw events to .engram/raw/YYYY-MM-DD/[event_id].json.
/// Uses atomic writes (temp + rename) to prevent corruption from partial failures.
/// Append-only: never modifies existing files. Uses content hashing for deduplication.
/// </summary>
public class RawEventWriter
{
    private readonly WorkspacePaths _paths;
    private readonly ContentHasher _hasher;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public RawEventWriter(WorkspacePaths paths, ContentHasher hasher)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
    }

    /// <summary>
    /// Writes a raw event to the store using atomic write (temp + rename).
    /// Returns Created if the event was written, Duplicate if an equivalent event already exists.
    /// Never modifies existing files.
    /// </summary>
    public WriteResult Write(RawEvent rawEvent)
    {
        ArgumentNullException.ThrowIfNull(rawEvent);

        // Compute deterministic hash
        var hash = _hasher.ComputeHash(rawEvent);
        rawEvent.Hash = hash;

        // Determine file path: .engram/raw/YYYY-MM-DD/[event_id].json
        var dateFolder = rawEvent.CapturedAt.ToString("yyyy-MM-dd");
        var dateDir = Path.Combine(_paths.Raw, dateFolder);
        var filePath = Path.Combine(dateDir, $"{rawEvent.EventId}.json");

        // Check if an equivalent event already exists (by hash)
        if (TryFindDuplicateByHash(dateDir, hash, out var existingFilePath))
        {
            return new WriteResult
            {
                Outcome = WriteOutcome.Duplicate,
                EventId = rawEvent.EventId,
                FilePath = existingFilePath,
                Hash = hash
            };
        }

        // Atomic write: create date directory, write to .tmp, then rename
        Directory.CreateDirectory(dateDir);

        var tmpPath = filePath + ".tmp";
        var json = JsonSerializer.Serialize(rawEvent, JsonOptions);

        File.WriteAllText(tmpPath, json);
        File.Move(tmpPath, filePath, overwrite: false);

        return new WriteResult
        {
            Outcome = WriteOutcome.Created,
            EventId = rawEvent.EventId,
            FilePath = filePath,
            Hash = hash
        };
    }

    /// <summary>
    /// Scans the date directory for an existing event with the same hash.
    /// Returns true if a duplicate is found without modifying any files.
    /// </summary>
    private bool TryFindDuplicateByHash(string dateDir, string hash, out string existingFilePath)
    {
        existingFilePath = string.Empty;

        if (!Directory.Exists(dateDir))
            return false;

        foreach (var file in Directory.EnumerateFiles(dateDir, "*.json"))
        {
            // Skip sidecar files and temp files
            if (file.EndsWith(".meta.json") || file.EndsWith(".tmp"))
                continue;

            try
            {
                var json = File.ReadAllText(file);
                var existing = JsonSerializer.Deserialize<RawEvent>(json, JsonOptions);
                if (existing?.Hash == hash)
                {
                    existingFilePath = file;
                    return true;
                }
            }
            catch
            {
                // Skip malformed JSON files
                continue;
            }
        }

        return false;
    }
}
