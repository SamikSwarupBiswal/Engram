using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Engram.Store;

/// <summary>
/// Production-grade replay enumerator.
/// Features: streaming (yield return), pagination, filtering, integrity verification, structured logging.
/// </summary>
public class ReplayEnumerator : IDisposable
{
    private readonly WorkspacePaths _paths;
    private readonly ProcessingSidecar _sidecar;
    private readonly ContentHasher _hasher;
    private readonly ILogger<ReplayEnumerator>? _logger;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    public ReplayEnumerator(WorkspacePaths paths, ILogger<ReplayEnumerator>? logger = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _logger = logger;
        _sidecar = new ProcessingSidecar(paths, logger as ILogger<ProcessingSidecar>);
        _hasher = new ContentHasher();
    }

    /// <summary>
    /// Enumerates all raw events (backward compatible, returns list).
    /// </summary>
    public IReadOnlyList<RawEvent> EnumerateAll()
    {
        return Enumerate(new ReplayQuery());
    }

    /// <summary>
    /// Enumerates with filtering, pagination. Collects into list for backward compatibility.
    /// </summary>
    public IReadOnlyList<RawEvent> Enumerate(ReplayQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        _logger?.LogDebug("Enumerating events with query: From={From}, To={To}, Source={Source}, Status={Status}",
            query.FromDate, query.ToDate, query.Source, query.ProcessingStatus);

        var result = EnumerateStreaming(query).ToList();

        _logger?.LogInformation("Enumerated {Count} events", result.Count);
        return result;
    }

    /// <summary>
    /// Streaming enumeration with yield return. Memory-efficient for large datasets.
    /// Supports filtering by date range, source, processing status, and pagination.
    /// </summary>
    public IEnumerable<RawEvent> EnumerateStreaming(ReplayQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!Directory.Exists(_paths.Raw))
            yield break;

        var skip = query.Offset ?? 0;
        var take = query.Limit ?? int.MaxValue;
        var yielded = 0;
        var skipped = 0;

        var dateDirs = Directory.EnumerateDirectories(_paths.Raw)
            .OrderBy(d => Path.GetFileName(d));

        foreach (var dateDir in dateDirs)
        {
            var dirName = Path.GetFileName(dateDir);

            // Date filter
            if (DateOnly.TryParse(dirName, out var folderDate))
            {
                if (query.FromDate.HasValue && folderDate < query.FromDate.Value)
                    continue;
                if (query.ToDate.HasValue && folderDate > query.ToDate.Value)
                    continue;
            }

            var files = Directory.EnumerateFiles(dateDir, "*.json")
                .Where(f => !f.EndsWith(".meta.json") && !f.EndsWith(".tmp") && !f.EndsWith(".lock"))
                .OrderBy(f => Path.GetFileName(f));

            foreach (var file in files)
            {
                RawEvent? rawEvent = null;
                try
                {
                    var json = File.ReadAllText(file);
                    rawEvent = JsonSerializer.Deserialize<RawEvent>(json, JsonOptions);
                }
                catch (JsonException ex)
                {
                    _logger?.LogWarning(ex, "Skipping malformed JSON file: {Path}", file);
                    continue;
                }
                catch (IOException ex)
                {
                    _logger?.LogWarning(ex, "IO error reading file: {Path}", file);
                    continue;
                }

                if (rawEvent == null) continue;

                // Source filter
                if (query.Source != null &&
                    !string.Equals(rawEvent.Source, query.Source, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Processing status filter (from sidecar)
                if (query.ProcessingStatus != null)
                {
                    ProcessingState? state = null;
                    try
                    {
                        state = _sidecar.Read(file);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to read sidecar for {Path}", file);
                    }

                    if (state == null || state.Status != query.ProcessingStatus)
                        continue;
                }

                // Pagination: skip
                if (skipped < skip)
                {
                    skipped++;
                    continue;
                }

                // Pagination: take
                if (yielded >= take)
                    yield break;

                yielded++;
                yield return rawEvent;
            }
        }
    }

    /// <summary>
    /// Enumerates with integrity verification. Corrupted files reported separately.
    /// </summary>
    public IntegrityResult EnumerateWithIntegrityCheck()
    {
        _logger?.LogInformation("Starting integrity verification scan");

        var valid = new List<RawEvent>();
        var corrupted = new List<CorruptedEvent>();

        if (!Directory.Exists(_paths.Raw))
            return new IntegrityResult { ValidEvents = valid, CorruptedEvents = corrupted };

        var dateDirs = Directory.EnumerateDirectories(_paths.Raw)
            .OrderBy(d => Path.GetFileName(d));

        foreach (var dateDir in dateDirs)
        {
            var files = Directory.EnumerateFiles(dateDir, "*.json")
                .Where(f => !f.EndsWith(".meta.json") && !f.EndsWith(".tmp") && !f.EndsWith(".lock"))
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
                        _logger?.LogWarning("Corrupted event (null): {Path}", file);
                        continue;
                    }

                    var recomputed = _hasher.ComputeHash(rawEvent);
                    if (recomputed != rawEvent.Hash)
                    {
                        corrupted.Add(new CorruptedEvent
                        {
                            FilePath = file,
                            Reason = $"Hash mismatch: stored={rawEvent.Hash[..16]}... computed={recomputed[..16]}..."
                        });
                        _logger?.LogWarning("Corrupted event (hash mismatch): {Path}", file);
                        continue;
                    }

                    valid.Add(rawEvent);
                }
                catch (JsonException ex)
                {
                    corrupted.Add(new CorruptedEvent { FilePath = file, Reason = $"JSON parse error: {ex.Message}" });
                    _logger?.LogWarning(ex, "Corrupted event (JSON error): {Path}", file);
                }
                catch (Exception ex)
                {
                    corrupted.Add(new CorruptedEvent { FilePath = file, Reason = ex.Message });
                    _logger?.LogWarning(ex, "Corrupted event: {Path}", file);
                }
            }
        }

        _logger?.LogInformation("Integrity check complete: {Valid} valid, {Corrupted} corrupted", valid.Count, corrupted.Count);

        return new IntegrityResult { ValidEvents = valid, CorruptedEvents = corrupted };
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _sidecar.Dispose();
            _disposed = true;
        }
    }
}
