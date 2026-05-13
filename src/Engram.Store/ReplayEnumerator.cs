using System.Text.Json;

namespace Engram.Store;

/// <summary>
/// Enumerates persisted raw events from .engram/raw/ in deterministic order.
/// Order: sorted by date folder (YYYY-MM-DD), then by event file name within each folder.
/// Supports filtering via ReplayQuery and integrity verification.
/// </summary>
public class ReplayEnumerator
{
    private readonly WorkspacePaths _paths;
    private readonly ProcessingSidecar _sidecar;
    private readonly ContentHasher _hasher;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    public ReplayEnumerator(WorkspacePaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _sidecar = new ProcessingSidecar(paths);
        _hasher = new ContentHasher();
    }

    /// <summary>
    /// Enumerates all raw events (no filtering). Preserves Phase 1 behavior.
    /// </summary>
    public IReadOnlyList<RawEvent> EnumerateAll()
    {
        return Enumerate(new ReplayQuery());
    }

    /// <summary>
    /// Enumerates raw events with optional filtering by date, source, and processing status.
    /// Deterministic ordering preserved.
    /// </summary>
    public IReadOnlyList<RawEvent> Enumerate(ReplayQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var result = new List<RawEvent>();

        if (!Directory.Exists(_paths.Raw))
            return result;

        var dateDirs = Directory.EnumerateDirectories(_paths.Raw)
            .OrderBy(d => Path.GetFileName(d));

        foreach (var dateDir in dateDirs)
        {
            var dirName = Path.GetFileName(dateDir);

            // Date filter: check if folder date is in range
            if (DateOnly.TryParse(dirName, out var folderDate))
            {
                if (query.FromDate.HasValue && folderDate < query.FromDate.Value)
                    continue;
                if (query.ToDate.HasValue && folderDate > query.ToDate.Value)
                    continue;
            }

            var files = Directory.EnumerateFiles(dateDir, "*.json")
                .Where(f => !f.EndsWith(".meta.json") && !f.EndsWith(".tmp"))
                .OrderBy(f => Path.GetFileName(f));

            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var rawEvent = JsonSerializer.Deserialize<RawEvent>(json, JsonOptions);
                    if (rawEvent == null) continue;

                    // Source filter
                    if (query.Source != null &&
                        !string.Equals(rawEvent.Source, query.Source, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Processing status filter (from sidecar)
                    if (query.ProcessingStatus != null)
                    {
                        var state = _sidecar.Read(file);
                        if (state == null || state.Status != query.ProcessingStatus)
                            continue;
                    }

                    result.Add(rawEvent);
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

    /// <summary>
    /// Enumerates with integrity verification. Corrupted files are reported separately.
    /// </summary>
    public IntegrityResult EnumerateWithIntegrityCheck()
    {
        var valid = new List<RawEvent>();
        var corrupted = new List<CorruptedEvent>();

        if (!Directory.Exists(_paths.Raw))
            return new IntegrityResult { ValidEvents = valid, CorruptedEvents = corrupted };

        var dateDirs = Directory.EnumerateDirectories(_paths.Raw)
            .OrderBy(d => Path.GetFileName(d));

        foreach (var dateDir in dateDirs)
        {
            var files = Directory.EnumerateFiles(dateDir, "*.json")
                .Where(f => !f.EndsWith(".meta.json") && !f.EndsWith(".tmp"))
                .OrderBy(f => Path.GetFileName(f));

            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var rawEvent = JsonSerializer.Deserialize<RawEvent>(json, JsonOptions);

                    if (rawEvent == null)
                    {
                        corrupted.Add(new CorruptedEvent { FilePath = file, Reason = "Deserialized to null" });
                        continue;
                    }

                    // Verify integrity: recompute hash and compare
                    var recomputed = _hasher.ComputeHash(rawEvent);
                    if (recomputed != rawEvent.Hash)
                    {
                        corrupted.Add(new CorruptedEvent
                        {
                            FilePath = file,
                            Reason = $"Hash mismatch: stored={rawEvent.Hash[..16]}... computed={recomputed[..16]}..."
                        });
                        continue;
                    }

                    valid.Add(rawEvent);
                }
                catch (Exception ex)
                {
                    corrupted.Add(new CorruptedEvent { FilePath = file, Reason = ex.Message });
                }
            }
        }

        return new IntegrityResult { ValidEvents = valid, CorruptedEvents = corrupted };
    }
}
