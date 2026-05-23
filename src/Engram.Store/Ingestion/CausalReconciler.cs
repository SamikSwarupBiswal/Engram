using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Ingestion;

/// <summary>
/// Replays the Write-Ahead Log (WAL) on startup to repair causal sequence fractures
/// and self-heals corrupted write sequences.
/// </summary>
public class CausalReconciler
{
    private readonly WorkspacePaths _paths;
    private readonly ContentHasher _hasher;
    private readonly ILogger<CausalReconciler>? _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public CausalReconciler(WorkspacePaths paths, ContentHasher hasher, ILogger<CausalReconciler>? logger = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
        _logger = logger;
    }

    /// <summary>
    /// Scan the WAL, identify uncommitted event writes, verify their contents,
    /// complete/delete files, update index, and clear the WAL.
    /// </summary>
    public int Reconcile()
    {
        using var wal = new WriteAheadLog(_paths.Raw);
        var uncommitted = wal.GetUncommittedWrites();

        if (uncommitted.Count == 0)
        {
            _logger?.LogInformation("Causal Reconciler: No uncommitted event writes found. System consistent.");
            return 0;
        }

        _logger?.LogWarning("Causal Reconciler: Found {Count} uncommitted writes. Starting recovery...", uncommitted.Count);
        int healedCount = 0;

        using var hashIndex = new HashIndex(_paths.Raw);

        foreach (var entry in uncommitted)
        {
            if (string.IsNullOrEmpty(entry.FilePath) || string.IsNullOrEmpty(entry.Hash))
            {
                continue;
            }

            var targetFile = entry.FilePath;
            var tmpFile = targetFile + ".tmp";

            // Case 1: Target file already exists (rename succeeded, commit failed/crashed)
            if (File.Exists(targetFile))
            {
                if (VerifyFileHash(targetFile, entry.Hash))
                {
                    _logger?.LogInformation("Causal Reconciler: Target file exists and hash matches. Indexing and committing {EventId}", entry.EventId);
                    hashIndex.Add(entry.Hash, targetFile);
                    healedCount++;
                }
                else
                {
                    _logger?.LogWarning("Causal Reconciler: Target file exists but hash mismatch. Deleting corrupted file: {Path}", targetFile);
                    try { File.Delete(targetFile); } catch { }
                }
            }
            // Case 2: Target file doesn't exist, but tmp file exists
            else if (File.Exists(tmpFile))
            {
                if (VerifyFileHash(tmpFile, entry.Hash))
                {
                    _logger?.LogInformation("Causal Reconciler: Tmp file is valid. Renaming to target file and committing {EventId}", entry.EventId);
                    try
                    {
                        File.Move(tmpFile, targetFile, overwrite: true);
                        hashIndex.Add(entry.Hash, targetFile);
                        healedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Causal Reconciler: Failed to rename tmp file: {Path}", tmpFile);
                    }
                }
                else
                {
                    _logger?.LogWarning("Causal Reconciler: Tmp file hash mismatch or corrupted. Cleaning up tmp file: {Path}", tmpFile);
                    try { File.Delete(tmpFile); } catch { }
                }
            }
            else
            {
                _logger?.LogWarning("Causal Reconciler: Neither target nor tmp file exists for {EventId}. Clean recovery.", entry.EventId);
            }
        }

        // Clear the WAL after recovery
        wal.Clear();
        _logger?.LogInformation("Causal Reconciler recovery completed. Healed {HealedCount} writes.", healedCount);
        return healedCount;
    }

    private bool VerifyFileHash(string path, string expectedHash)
    {
        try
        {
            var content = File.ReadAllText(path);
            var rawEvent = JsonSerializer.Deserialize<RawEvent>(content, JsonOptions);
            if (rawEvent == null) return false;

            var computedHash = _hasher.ComputeHash(rawEvent);
            return string.Equals(computedHash, expectedHash, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
