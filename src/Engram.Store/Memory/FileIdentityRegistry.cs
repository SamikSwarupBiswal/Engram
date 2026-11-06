using System.Security.Cryptography;
using System.Text.Json;
using Engram.Store.Governance;
using Engram.Store.Wiki;

namespace Engram.Store.Memory;

public sealed record FileObservation(string Path, string ChangeType, DateTimeOffset ObservedAt,
    string? OldPath = null, string? SourceUrl = null);

public sealed record FileMemoryReference(string FileId, string Path, string State, string? Sha256,
    long? SizeBytes, DateTimeOffset ObservedAt, string SourceEventId, string? DownloadSourceUrl = null);

/// <summary>Logical file identities shared by explicit tasks and consented filesystem observations.
/// Raw metadata is authoritative; the wiki is a source-linked projection. No file text is captured.</summary>
public sealed class FileIdentityRegistry
{
    public const long MaxHashBytes = 16 * 1024 * 1024;
    private const string StateProperty = "file_identity_state";
    private readonly WorkspacePaths _paths;
    private readonly RawEventWriter _writer;
    private readonly WikiNodeStore _nodes;
    private readonly MemorySovereigntySystem _sovereignty;
    private readonly Func<string, bool> _isPathAllowed;
    private readonly object _gate = new();
    private readonly Dictionary<string, FileMemoryReference> _files = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _historicalPaths = new(StringComparer.Ordinal);
    private readonly HashSet<string> _suppressedPathHashes = new(StringComparer.Ordinal);
    private bool _suppressionStateInvalid;
    private bool _reconciliationRequired;
    public bool ReconciliationRequired { get { lock (_gate) return _reconciliationRequired; } }

    public FileIdentityRegistry(WorkspacePaths paths, RawEventWriter writer, WikiNodeStore nodes,
        MemorySovereigntySystem sovereignty, Func<string, bool>? isPathAllowed = null)
    {
        _paths = paths;
        _writer = writer;
        _nodes = nodes;
        _sovereignty = sovereignty;
        _isPathAllowed = isPathAllowed ?? (_ => false);
        _reconciliationRequired = File.Exists(ReconciliationMarkerFile);
        LoadSuppressedPaths();
        Restore();
    }

    /// <summary>Serializes storage deletion with registry writes and clears cached identities
    /// only after the deletion callback succeeds. Callers must throw for incomplete deletion.</summary>
    public void ResetForDeletion(Action delete)
    {
        ArgumentNullException.ThrowIfNull(delete);
        lock (_gate)
        {
            try { delete(); }
            catch
            {
                // A partial delete cannot leave the old cache available for fresh capture.
                _reconciliationRequired = true;
                throw;
            }
            _files.Clear();
            _byPath.Clear();
            _historicalPaths.Clear();
            _suppressedPathHashes.Clear();
            _suppressionStateInvalid = false;
            _reconciliationRequired = false;
        }
    }

    public FileMemoryReference Observe(FileObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        lock (_gate)
        {
            var path = ValidatePath(observation.Path);
            var oldPath = observation.OldPath == null ? null : ValidatePath(observation.OldPath);
            var change = observation.ChangeType.ToLowerInvariant();
            if (change is not ("created" or "changed" or "renamed" or "deleted"))
                throw new ArgumentException("Unsupported file observation type.");
            if (change == "renamed" && oldPath == null)
                throw new ArgumentException("A rename must include the previous path.");
            if (observation.SourceUrl != null && (observation.SourceUrl.Length > 4096 || !Uri.TryCreate(observation.SourceUrl, UriKind.Absolute, out var source) ||
                source.Scheme is not ("http" or "https") || source.UserInfo.Length != 0))
                throw new ArgumentException("Download source must be an explicit HTTP or HTTPS URL without credentials.");
            EnsureNotForgotten(path);
            if (oldPath != null) EnsureNotForgotten(oldPath);
            var current = Lookup(oldPath ?? path);
            if (current != null && observation.ObservedAt < current.ObservedAt) return current;
            if (change != "deleted" && current?.State is "Deleted" or "Missing") current = null;
            if (oldPath != null && _byPath.TryGetValue(path, out var destinationId) &&
                destinationId != current?.FileId && _files[destinationId].State is not ("Deleted" or "Missing"))
                throw new InvalidOperationException("Rename destination is already associated with another file.");
            var id = current?.FileId ?? "file_identity_" + Guid.NewGuid().ToString("N");
            var reference = new FileMemoryReference(id, path,
                change == "deleted" ? "Deleted" : change == "changed" ? "Changed" : "Present",
                null, null, observation.ObservedAt, NewEventId(), observation.SourceUrl ?? current?.DownloadSourceUrl);
            return Persist(reference, change, oldPath);
        }
    }

    public FileMemoryReference RegisterVerifiedFile(string path, string sha256, long sizeBytes,
        string sourceEventId, DateTimeOffset verifiedAt)
    {
        lock (_gate)
        {
            path = ValidatePath(path);
            EnsureNotForgotten(path);
            if (string.IsNullOrWhiteSpace(sourceEventId) || sourceEventId.Length > 256)
                throw new ArgumentException("Verified file evidence requires a bounded source event identifier.");
            var (hash, size, state) = ReadIdentity(path);
            if (state != "Verified" || size != sizeBytes || !string.Equals(hash, sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The file no longer matches its verification evidence.");
            var current = Lookup(path);
            if (current?.State is "Deleted" or "Missing") current = null;
            if (current != null && verifiedAt < current.ObservedAt) return current;
            return Persist(new(current?.FileId ?? "file_identity_" + Guid.NewGuid().ToString("N"), path,
                state, hash, size, verifiedAt, NewEventId(), current?.DownloadSourceUrl), "verified", evidenceId: sourceEventId);
        }
    }

    public Task<FileMemoryReference?> RevalidateAsync(string fileId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_files.TryGetValue(fileId, out var current) || _sovereignty.IsDeleted(fileId))
                return Task.FromResult<FileMemoryReference?>(null);
            ValidatePath(current.Path);
            EnsureNotForgotten(current.Path);
            // A historical deleted identity cannot adopt a different file recreated at its path.
            if (current.State == "Deleted" || _byPath.GetValueOrDefault(current.Path) != fileId)
                return Task.FromResult<FileMemoryReference?>(current);
            var (hash, size, state) = ReadIdentity(current.Path, ct);
            var observedAt = DateTimeOffset.UtcNow;
            if (observedAt <= current.ObservedAt) observedAt = current.ObservedAt.AddTicks(1);
            var updated = current with { State = state, Sha256 = hash, SizeBytes = size,
                ObservedAt = observedAt, SourceEventId = NewEventId() };
            return Task.FromResult<FileMemoryReference?>(Persist(updated, "revalidated"));
        }
    }

    public IReadOnlyList<FileMemoryReference> FindCandidates(string query, int limit = 5)
    {
        if (limit is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(limit));
        if (string.IsNullOrWhiteSpace(query)) return [];
        lock (_gate)
        {
            return _files.Values.Where(reference => reference.State is not ("Deleted" or "Missing") &&
                    _byPath.GetValueOrDefault(reference.Path) == reference.FileId &&
                    !_sovereignty.IsDeleted(reference.FileId) && CanObservePath(reference.Path) &&
                    Path.GetFileName(reference.Path).Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(reference => reference.ObservedAt).Take(limit).ToArray();
        }
    }

    /// <summary>Pre-publication capture filter. Directories may be watcher roots, but
    /// forgotten paths must never be emitted onto the shared event bus again.</summary>
    public bool CanObservePath(string path)
    {
        lock (_gate)
        {
            try
            {
                var normalized = NormalizePath(path);
                if (!IsAllowed(normalized)) return false;
                EnsureNotForgotten(normalized);
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                return false;
            }
        }
    }

    // Outcome replay must distinguish an absent watching grant from a durable
    // forgetting decision, including snapshots created before a shared file ID existed.
    internal bool IsKnownForgottenPath(string path)
    {
        lock (_gate)
        {
            try { return IsSuppressed(NormalizePath(path)); }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                return true;
            }
        }
    }

    /// <summary>Transfers forgetting across renames before either path reaches the event bus.</summary>
    public bool CanObserveRename(string oldPath, string newPath)
    {
        lock (_gate)
        {
            if (_reconciliationRequired || _suppressionStateInvalid) return false;
            try
            {
                var oldName = NormalizePath(oldPath);
                var newName = NormalizePath(newPath);
                if (IsSuppressed(oldName))
                {
                    SuppressPath(newName);
                    return false;
                }
                return CanObservePath(oldName) && CanObservePath(newName);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                return false;
            }
        }
    }

    /// <summary>Persistently stops capture when events can no longer establish file identity.
    /// Clearing this requires a real reconciliation; restarting must not clear uncertainty.</summary>
    public void SuspendCaptureForReconciliation()
    {
        lock (_gate)
        {
            _reconciliationRequired = true;
            EnsureMemoryStorageSafe(ReconciliationMarkerFile);
            Directory.CreateDirectory(_paths.Config);
            EnsureMemoryStorageSafe(ReconciliationMarkerFile);
            var temporary = ReconciliationMarkerFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, "{\"reconciliationRequired\":true}");
                File.Move(temporary, ReconciliationMarkerFile, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    private FileMemoryReference Persist(FileMemoryReference reference, string change,
        string? oldPath = null, string? evidenceId = null)
    {
        if (_sovereignty.IsDeleted(reference.FileId)) throw new InvalidOperationException("This file memory was forgotten.");
        var json = JsonSerializer.Serialize(reference);
        _writer.Write(new RawEvent
        {
            EventId = reference.SourceEventId, EventType = "file_identity_observed", Source = "file_identity_registry",
            CapturedAt = reference.ObservedAt, SourceUri = reference.Path, PrivacyClass = "private",
            Text = "File metadata observed: " + change,
            Metadata = new() { [StateProperty] = json, ["change_type"] = change,
                ["old_path"] = oldPath ?? string.Empty, ["verification_source"] = evidenceId ?? string.Empty }
        });
        Index(reference, oldPath);
        if (_sovereignty.IsDeleted(reference.FileId)) throw new InvalidOperationException("This file memory was forgotten.");
        var node = _nodes.Load(reference.FileId) ?? new WikiNode
        {
            NodeId = reference.FileId, NodeType = WikiNodeType.File, CreatedAt = reference.ObservedAt
        };
        node.Title = Path.GetFileName(reference.Path);
        node.Summary = $"File {reference.State}: {reference.Path}";
        node.LastTouchedAt = reference.ObservedAt;
        node.Claims.RemoveAll(claim => claim.Property == StateProperty);
        node.Claims.Add(new() { ClaimId = reference.SourceEventId, Property = StateProperty, Value = json,
            Source = "file_identity_registry", Timestamp = reference.ObservedAt });
        node.Facts.RemoveAll(fact => fact.Text.StartsWith("path:", StringComparison.OrdinalIgnoreCase));
        node.Facts.Add(new() { Text = "path: " + reference.Path, LastConfirmedAt = reference.ObservedAt,
            Sources = [new() { EventId = reference.SourceEventId, CapturedAt = reference.ObservedAt, Source = "file_identity_registry" }] });
        _nodes.Save(node);
        // A forget racing the projection write must still win.
        if (_sovereignty.IsDeleted(reference.FileId))
        {
            _nodes.Delete(reference.FileId);
            throw new InvalidOperationException("This file memory was forgotten.");
        }
        return reference;
    }

    private void Index(FileMemoryReference reference, string? oldPath = null)
    {
        if (_files.TryGetValue(reference.FileId, out var previous))
        {
            if (previous.ObservedAt > reference.ObservedAt) return;
            if (_byPath.GetValueOrDefault(previous.Path) == reference.FileId) _byPath.Remove(previous.Path);
        }
        _files[reference.FileId] = reference;
        if (!_historicalPaths.TryGetValue(reference.FileId, out var history))
            _historicalPaths[reference.FileId] = history = new(StringComparer.OrdinalIgnoreCase);
        history.Add(reference.Path);
        if (!string.IsNullOrEmpty(oldPath)) history.Add(oldPath);
        if (!_byPath.TryGetValue(reference.Path, out var occupant) || _files[occupant].ObservedAt <= reference.ObservedAt)
            _byPath[reference.Path] = reference.FileId;
    }

    private void Restore()
    {
        if (!Directory.Exists(_paths.Raw)) return;
        var events = new List<(FileMemoryReference Reference, string? OldPath)>();
        foreach (var file in Directory.EnumerateFiles(_paths.Raw, "file_identity_*.json",
                     new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
        {
            if (new FileInfo(file).Length > 64 * 1024) continue;
            try
            {
                var raw = JsonSerializer.Deserialize<RawEvent>(File.ReadAllText(file));
                if (raw?.Source != "file_identity_registry" || raw.Metadata?.TryGetValue(StateProperty, out var value) != true ||
                    !string.Equals(raw.Hash, new ContentHasher().ComputeHash(raw), StringComparison.OrdinalIgnoreCase)) continue;
                var reference = JsonSerializer.Deserialize<FileMemoryReference>(value);
                if (reference == null || reference.SourceEventId != raw.EventId ||
                    !reference.FileId.StartsWith("file_identity_", StringComparison.Ordinal) || reference.FileId.Length != 46 ||
                    !reference.FileId[14..].All(char.IsAsciiHexDigit) || !Path.IsPathFullyQualified(reference.Path)) continue;
                events.Add((reference, raw.Metadata.GetValueOrDefault("old_path")));
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        }
        foreach (var item in events.OrderBy(item => item.Reference.ObservedAt)) Index(item.Reference, item.OldPath);
    }

    private FileMemoryReference? Lookup(string path) =>
        _byPath.TryGetValue(path, out var id) ? _files.GetValueOrDefault(id) : null;

    private void EnsureNotForgotten(string path)
    {
        if (_reconciliationRequired)
            throw new InvalidOperationException("File-memory capture is suspended until file identities are reconciled.");
        if (IsSuppressed(path))
            throw new InvalidOperationException("This file memory was forgotten. Re-enable remembering it explicitly before capture.");
    }

    private bool IsSuppressed(string path) => _reconciliationRequired || _suppressionStateInvalid || _suppressedPathHashes.Contains(PathHash(path)) ||
        _historicalPaths.Any(pair => pair.Value.Contains(path) && _sovereignty.IsDeleted(pair.Key));

    private string SuppressedPathsFile => Path.Combine(_paths.Config, "file-memory-suppressed-paths.json");
    private string ReconciliationMarkerFile => Path.Combine(_paths.Config, "file-memory-reconciliation-required.json");

    private static string PathHash(string path) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path.ToUpperInvariant())));

    private void LoadSuppressedPaths()
    {
        try
        {
            EnsureMemoryStorageSafe(SuppressedPathsFile);
            if (!File.Exists(SuppressedPathsFile)) return;
            if (new FileInfo(SuppressedPathsFile).Length > 512 * 1024)
                throw new InvalidDataException("File-memory suppression data exceeds its bound.");
            var hashes = JsonSerializer.Deserialize<string[]>(File.ReadAllText(SuppressedPathsFile));
            if (hashes == null || hashes.Length > 4096 || hashes.Any(hash => hash == null || hash.Length != 64 ||
                !hash.All(char.IsAsciiHexDigit))) throw new InvalidDataException("Invalid file-memory suppression data.");
            _suppressedPathHashes.UnionWith(hashes.Select(hash => hash.ToUpperInvariant()));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Damaged forgetting state must never turn capture back on.
            _suppressionStateInvalid = true;
        }
    }

    private void SuppressPath(string path)
    {
        if (_suppressionStateInvalid) throw new InvalidOperationException("File-memory forgetting state needs repair.");
        if (!_suppressedPathHashes.Add(PathHash(path))) return;
        var temporary = SuppressedPathsFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (_suppressedPathHashes.Count > 4096) throw new InvalidDataException("File-memory suppression data exceeds its bound.");
            EnsureMemoryStorageSafe(SuppressedPathsFile);
            Directory.CreateDirectory(_paths.Config);
            EnsureMemoryStorageSafe(SuppressedPathsFile);
            File.WriteAllText(temporary, JsonSerializer.Serialize(_suppressedPathHashes.Order(StringComparer.Ordinal)));
            File.Move(temporary, SuppressedPathsFile, overwrite: true);
        }
        catch
        {
            _suppressionStateInvalid = true;
            throw;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void EnsureMemoryStorageSafe(string path)
    {
        for (var parent = path; parent != null; parent = Path.GetDirectoryName(parent))
            if ((File.Exists(parent) || Directory.Exists(parent)) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("File-memory control storage must not contain symbolic links.");
    }

    private string ValidatePath(string path)
    {
        var normalized = NormalizePath(path);
        if (!IsAllowed(normalized)) throw new UnauthorizedAccessException("This path is outside the allowed file-memory scope.");
        if (Directory.Exists(normalized)) throw new InvalidDataException("File memory cannot identify a directory as a file.");
        return normalized;
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 4096 || !Path.IsPathFullyQualified(path) ||
            path.IndexOfAny(['*', '?', '\0']) >= 0 || path[Path.GetPathRoot(path)!.Length..].Contains(':'))
            throw new ArgumentException("File memory requires an absolute path.");
        // Win32 normalization removes trailing dots/spaces. Reject aliases before
        // GetFullPath so a second spelling cannot silently identify another path.
        if (path[Path.GetPathRoot(path)!.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar])
            .Any(component => component is not ("." or "..") &&
                (component.EndsWith('.') || component.EndsWith(' ') || OperatingSystem.IsWindows() && IsShortAlias(component))))
            throw new ArgumentException("File memory requires an unambiguous normal file path.");
        var normalized = Path.GetFullPath(path);
        if (normalized.StartsWith(@"\\?\", StringComparison.Ordinal) || normalized.StartsWith(@"\\.\", StringComparison.Ordinal) ||
            normalized[Path.GetPathRoot(normalized)!.Length..].Split(Path.DirectorySeparatorChar)
                .Any(component => component.EndsWith('.') || component.EndsWith(' ')))
            throw new ArgumentException("File memory requires an unambiguous normal file path.");
        if (OperatingSystem.IsWindows())
        {
            // Short-name aliases can otherwise bypass path consent or forgotten-path
            // suppression. Require the normal long spelling of existing ancestors.
            var existing = normalized;
            while (!File.Exists(existing) && !Directory.Exists(existing))
            {
                var leaf = Path.GetFileName(existing);
                if (IsShortAlias(leaf))
                    throw new ArgumentException("Use the normal file name instead of a short-name alias.");
                existing = Path.GetDirectoryName(existing);
                if (existing == null) throw new ArgumentException("File memory requires an accessible path root.");
            }
            var longPath = new System.Text.StringBuilder(32_768);
            var length = GetLongPathName(existing, longPath, (uint)longPath.Capacity);
            if (length == 0 || length >= longPath.Capacity)
                throw new UnauthorizedAccessException("The normal file path could not be verified.");
            if (!string.Equals(existing.TrimEnd(Path.DirectorySeparatorChar),
                    longPath.ToString().TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Use the normal file name instead of a short-name alias.");
        }
        return normalized;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "GetLongPathNameW", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern uint GetLongPathName(string shortPath, System.Text.StringBuilder longPath, uint bufferLength);

    private static bool IsShortAlias(string component)
    {
        var stem = component.Split('.')[0];
        var tilde = stem.LastIndexOf('~');
        return tilde >= 0 && tilde < stem.Length - 1 && stem[(tilde + 1)..].All(char.IsAsciiDigit);
    }

    private bool IsAllowed(string path)
    {
        try
        {
            var root = Path.GetFullPath(_paths.Root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !_isPathAllowed(path)) return false;
            for (var parent = path; parent != null; parent = Path.GetDirectoryName(parent))
                if ((File.Exists(parent) || Directory.Exists(parent)) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0) return false;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static (string? Hash, long? Size, string State) ReadIdentity(string path, CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(path)) return (null, null, "Missing");
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length > MaxHashBytes) return (null, file.Length, "Unavailable");
            ct.ThrowIfCancellationRequested();
            var hash = Convert.ToHexString(SHA256.HashData(file));
            ct.ThrowIfCancellationRequested();
            return (hash, file.Length, "Verified");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return (null, null, "Unavailable"); }
    }

    private static string NewEventId() => "file_identity_" + Guid.NewGuid().ToString("N");
}
