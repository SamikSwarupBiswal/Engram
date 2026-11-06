using System.Text.Json;
using Engram.Store.Governance;
using Engram.Store.Memory;
using Engram.Store.Metabolism;
using Engram.Store.Wiki;

namespace Engram.Store.Tests;

public sealed class FileIdentityRegistryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "engram-file-memory-" + Guid.NewGuid().ToString("N"));
    private readonly WorkspacePaths _paths;
    private readonly WikiNodeStore _nodes;
    private readonly RawEventWriter _writer;
    private readonly MemorySovereigntySystem _sovereignty;
    private readonly FileIdentityRegistry _registry;
    private DateTimeOffset _time = DateTimeOffset.UtcNow.AddMinutes(-1);

    public FileIdentityRegistryTests()
    {
        Directory.CreateDirectory(_root);
        _paths = new(Path.Combine(_root, "memory"));
        _nodes = new(_paths);
        _writer = new(_paths, new ContentHasher());
        _sovereignty = new(_nodes, _paths);
        _registry = NewRegistry();
    }

    private FileIdentityRegistry NewRegistry() => new(_paths, _writer, _nodes, _sovereignty,
        path => path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

    private FileMemoryReference Observe(string path, string type = "created", string? oldPath = null, string? sourceUrl = null) =>
        _registry.Observe(new(path, type, _time = _time.AddSeconds(1), oldPath, sourceUrl));

    private string Create(string relative = "note.txt", string content = "original")
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task DeleteAllClearsCachedIdentitiesBeforeWatchingIsEnabledAgain()
    {
        var enabled = true;
        var registry = new FileIdentityRegistry(_paths, _writer, _nodes, _sovereignty, _ => enabled);
        var path = Create();
        var original = registry.Observe(new(path, "created", DateTimeOffset.UtcNow));
        Assert.Equal(original.FileId, registry.FindCandidates("note").Single().FileId);
        enabled = false;

        registry.ResetForDeletion(() =>
        {
            var result = new Engram.Store.Security.DataDelete(_paths.Root).DeleteAll();
            if (!result.Success) throw new IOException(string.Join("; ", result.Errors));
        });

        Assert.Empty(registry.FindCandidates("note"));
        Assert.Null(await registry.RevalidateAsync(original.FileId));
        enabled = true;
        Assert.Empty(registry.FindCandidates("note"));
        Assert.True(File.Exists(path));
        var fresh = registry.Observe(new(path, "created", DateTimeOffset.UtcNow));
        Assert.NotEqual(original.FileId, fresh.FileId);
        Assert.Equal(fresh.FileId, registry.FindCandidates("note").Single().FileId);
        Assert.Equal(fresh.FileId, NewRegistry().FindCandidates("note").Single().FileId);
    }

    [Fact]
    public void SuccessfulDeletionClearsForgottenPathAndReconciliationCaches()
    {
        var path = Create();
        var original = Observe(path);
        _sovereignty.Forget(original.FileId);
        _registry.SuspendCaptureForReconciliation();
        Assert.True(_registry.IsKnownForgottenPath(path));

        _registry.ResetForDeletion(() =>
        {
            var result = new Engram.Store.Security.DataDelete(_paths.Root).DeleteAll();
            if (!result.Success) throw new IOException(string.Join("; ", result.Errors));
        });

        Assert.False(_registry.ReconciliationRequired);
        Assert.False(_registry.IsKnownForgottenPath(path));
        Assert.True(_registry.CanObservePath(path));
        Assert.NotEqual(original.FileId, Observe(path).FileId);
    }

    [Fact]
    public async Task FailedDeletionRetainsSourceIdentityAndBlocksFurtherCapture()
    {
        var path = Create();
        var original = Observe(path);

        Assert.Throws<IOException>(() => _registry.ResetForDeletion(() => throw new IOException("Deletion failed.")));

        Assert.True(_registry.ReconciliationRequired);
        Assert.False(_registry.CanObservePath(path));
        Assert.Empty(_registry.FindCandidates("note"));
        Assert.Throws<InvalidOperationException>(() => Observe(path, "changed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _registry.RevalidateAsync(original.FileId));
        Assert.NotNull(_nodes.Load(original.FileId));
        Assert.Single(Directory.GetFiles(_paths.Raw, "file_identity_*.json", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ExternalCreateEditRenameDeleteRecreatePreservesDocumentHistory()
    {
        var path = Create();
        var created = Observe(path);
        Assert.Equal("Present", created.State);
        Assert.Null(created.Sha256);
        var verified = await _registry.RevalidateAsync(created.FileId);
        Assert.Equal("Verified", verified!.State);
        Assert.NotNull(verified.Sha256);

        File.WriteAllText(path, "edited by user");
        var changed = _registry.Observe(new(path, "changed", DateTimeOffset.UtcNow.AddSeconds(1)));
        Assert.Equal(created.FileId, changed.FileId);
        Assert.Null(changed.Sha256);
        var edited = await _registry.RevalidateAsync(created.FileId);
        Assert.NotEqual(verified.Sha256, edited!.Sha256);

        var destination = Path.Combine(_root, "renamed.txt");
        File.Move(path, destination);
        var renamed = _registry.Observe(new(destination, "renamed", DateTimeOffset.UtcNow.AddSeconds(2), path));
        Assert.Equal(created.FileId, renamed.FileId);
        Assert.Equal(destination, renamed.Path);
        File.Delete(destination);
        var deleted = _registry.Observe(new(destination, "deleted", DateTimeOffset.UtcNow.AddSeconds(3)));
        Assert.Equal(created.FileId, deleted.FileId);
        Assert.Equal("Deleted", deleted.State);
        File.WriteAllText(destination, "replacement document");
        var recreated = _registry.Observe(new(destination, "created", DateTimeOffset.UtcNow.AddSeconds(4)));
        Assert.NotEqual(created.FileId, recreated.FileId);
    }

    [Fact]
    public void SameBasenameAndContentsAreDistinctAndCannotBeMerged()
    {
        var first = Observe(Create(Path.Combine("one", "note.txt")));
        var second = Observe(Create(Path.Combine("two", "note.txt")));
        Assert.NotEqual(first.FileId, second.FileId);
        Assert.Equal(2, _registry.FindCandidates("note").Count);
        Assert.Equal(0, new SemanticDeduplicator(_nodes) { SimilarityThreshold = 0 }.Deduplicate().MergesPerformed);
        Assert.Empty(new SemanticCompactor(_nodes).FindMergePairs(_nodes.LoadAll(), 0));
        Assert.NotNull(_nodes.Load(first.FileId));
        Assert.NotNull(_nodes.Load(second.FileId));
    }

    [Fact]
    public void RestartRetainsLogicalIdentityAndExplicitDownloadSource()
    {
        var path = Create("download.txt");
        var original = Observe(path, sourceUrl: "https://example.org/report");
        var restored = NewRegistry().FindCandidates("download").Single();
        Assert.Equal(original.FileId, restored.FileId);
        Assert.Equal("https://example.org/report", restored.DownloadSourceUrl);
        var source = Directory.GetFiles(_paths.Raw, "file_identity_*.json", SearchOption.AllDirectories).Single();
        using var raw = JsonDocument.Parse(File.ReadAllText(source));
        Assert.Equal("private", raw.RootElement.GetProperty("privacy_class").GetString());
        Assert.DoesNotContain("original", File.ReadAllText(source));
    }

    [Fact]
    public void DownloadsFolderDoesNotInventSourceUrl()
    {
        var reference = Observe(Create(Path.Combine("Downloads", "report.txt")));
        Assert.Null(reference.DownloadSourceUrl);
    }

    [Fact]
    public async Task TaskOutputJoinsExistingFileAndHashIsRevalidated()
    {
        var path = Create();
        var existing = Observe(path);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
        var output = _registry.RegisterVerifiedFile(path, hash, new FileInfo(path).Length, "task_source", DateTimeOffset.UtcNow);
        Assert.Equal(existing.FileId, output.FileId);
        Assert.Equal(hash, output.Sha256);
        Assert.Equal("Verified", output.State);
        File.Delete(path);
        Assert.Equal("Missing", (await NewRegistry().RevalidateAsync(output.FileId))!.State);
    }

    [Fact]
    public void FalseVerifiedHashIsRejectedWithoutPersistingSuccess()
    {
        var path = Create();
        Assert.Throws<InvalidDataException>(() =>
            _registry.RegisterVerifiedFile(path, new string('0', 64), new FileInfo(path).Length, "task_source", DateTimeOffset.UtcNow));
        Assert.Empty(_registry.FindCandidates("note"));
    }

    [Fact]
    public void ForgetPreventsLateWatcherResurrectionAfterRestart()
    {
        var path = Create();
        var reference = Observe(path);
        Assert.True(_registry.CanObservePath(path));
        _sovereignty.Forget(reference.FileId);
        Assert.False(_registry.CanObservePath(path));
        var restarted = new FileIdentityRegistry(_paths, _writer, _nodes,
            new MemorySovereigntySystem(_nodes, _paths), _ => true);
        Assert.Empty(restarted.FindCandidates("note"));
        Assert.False(restarted.CanObservePath(path));
        Assert.Throws<InvalidOperationException>(() => restarted.Observe(new(path, "changed", DateTimeOffset.UtcNow)));
        Assert.Null(_nodes.Load(reference.FileId));
    }

    [Fact]
    public void ForgetFollowsRenamesWithoutPersistingPlaintextSuppressedPaths()
    {
        var path = Create("private-original.txt");
        var reference = Observe(path);
        _sovereignty.Forget(reference.FileId);
        var renamed = Path.Combine(_root, "private-renamed.txt");
        File.Move(path, renamed);
        Assert.False(_registry.CanObserveRename(path, renamed));
        Assert.False(_registry.CanObservePath(renamed));
        Assert.Throws<InvalidOperationException>(() => Observe(renamed, "changed"));
        var suppressionFile = Path.Combine(_paths.Config, "file-memory-suppressed-paths.json");
        var suppression = File.ReadAllText(suppressionFile);
        Assert.DoesNotContain("private", suppression, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_root, suppression, StringComparison.OrdinalIgnoreCase);
        var restarted = NewRegistry();
        Assert.False(restarted.CanObservePath(renamed));
        var renamedAgain = Path.Combine(_root, "second-rename.txt");
        File.Move(renamed, renamedAgain);
        Assert.False(restarted.CanObserveRename(renamed, renamedAgain));
        Assert.False(NewRegistry().CanObservePath(renamedAgain));
        Assert.Throws<InvalidOperationException>(() => restarted.Observe(new(renamedAgain, "changed", DateTimeOffset.UtcNow)));
    }

    [Fact]
    public void DamagedSuppressionStateFailsClosedAfterRestart()
    {
        var path = Create();
        File.WriteAllText(Path.Combine(_paths.Config, "file-memory-suppressed-paths.json"), "invalid-json");
        var restarted = NewRegistry();
        Assert.False(restarted.CanObservePath(path));
        Assert.False(restarted.CanObserveRename(path, Path.Combine(_root, "next.txt")));
        Assert.Throws<InvalidOperationException>(() => restarted.Observe(new(path, "created", DateTimeOffset.UtcNow)));
        Assert.Empty(restarted.FindCandidates("note"));
    }

    [Fact]
    public void OrdinaryRenameFilterRequiresBothPathsWithinAllowedScope()
    {
        var path = Create();
        Assert.True(_registry.CanObserveRename(path, Path.Combine(_root, "next.txt")));
        Assert.False(_registry.CanObserveRename(path, Path.Combine(Path.GetTempPath(), "not-selected.txt")));
    }

    [Fact]
    public void LostCoverageSuspendsCaptureDurablyUntilActualReconciliation()
    {
        var path = Create();
        Assert.False(_registry.ReconciliationRequired);
        _registry.SuspendCaptureForReconciliation();
        Assert.True(_registry.ReconciliationRequired);
        Assert.False(_registry.CanObservePath(path));
        Assert.False(_registry.CanObserveRename(path, Path.Combine(_root, "new.txt")));
        Assert.Throws<InvalidOperationException>(() => Observe(path));
        var marker = File.ReadAllText(Path.Combine(_paths.Config, "file-memory-reconciliation-required.json"));
        Assert.DoesNotContain(_root, marker, StringComparison.OrdinalIgnoreCase);
        var restarted = NewRegistry();
        Assert.True(restarted.ReconciliationRequired);
        Assert.False(restarted.CanObservePath(path));
        Assert.Throws<InvalidOperationException>(() => restarted.Observe(new(path, "created", DateTimeOffset.UtcNow)));
    }

    [Fact]
    public void MissingProjectionDoesNotLoseLogicalIdentity()
    {
        var path = Create();
        var reference = Observe(path);
        _nodes.Delete(reference.FileId);
        var renewed = NewRegistry().Observe(new(path, "changed", DateTimeOffset.UtcNow));
        Assert.Equal(reference.FileId, renewed.FileId);
    }

    [Fact]
    public void OlderWatcherEventCannotReplaceNewerVerifiedVersion()
    {
        var path = Create();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
        var verified = _registry.RegisterVerifiedFile(path, hash, new FileInfo(path).Length, "task_source", DateTimeOffset.UtcNow);
        var old = Observe(path, "changed");
        Assert.Equal(verified, old);
    }

    [Fact]
    public void DefaultPolicyAndOwnWorkspaceAreExcludedBeforeWriting()
    {
        var path = Create();
        var denied = new FileIdentityRegistry(_paths, _writer, _nodes, _sovereignty);
        Assert.Throws<UnauthorizedAccessException>(() => denied.Observe(new(path, "created", DateTimeOffset.UtcNow)));
        var ownPath = Path.Combine(_paths.Root, "private.txt");
        Assert.Throws<UnauthorizedAccessException>(() => Observe(ownPath));
        Assert.Empty(_registry.FindCandidates("note"));
    }

    [Fact]
    public void DirectoryAndRelativePathsCannotBecomeFileMemories()
    {
        Assert.Throws<ArgumentException>(() => Observe("relative.txt"));
        var directory = Path.Combine(_root, "a-directory");
        Directory.CreateDirectory(directory);
        Assert.Throws<InvalidDataException>(() => Observe(directory));
    }

    [Fact]
    public async Task ScopeRevocationImmediatelyRemovesCandidatesAndPreventsRevalidation()
    {
        var allowed = true;
        var registry = new FileIdentityRegistry(_paths, _writer, _nodes, _sovereignty, _ => allowed);
        var reference = registry.Observe(new(Create(), "created", DateTimeOffset.UtcNow));
        allowed = false;
        Assert.False(registry.CanObservePath(reference.Path));
        Assert.Empty(registry.FindCandidates("note"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => registry.RevalidateAsync(reference.FileId));
    }

    [Fact]
    public void LinkToAnotherFolderIsExcluded()
    {
        var realDirectory = Path.Combine(_root, "real");
        var link = Path.Combine(_root, "link");
        Directory.CreateDirectory(realDirectory);
        File.WriteAllText(Path.Combine(realDirectory, "note.txt"), "private content");
        Directory.CreateSymbolicLink(link, realDirectory);
        try { Assert.Throws<UnauthorizedAccessException>(() => Observe(Path.Combine(link, "note.txt"))); }
        finally { Directory.Delete(link); }
        Assert.Empty(_registry.FindCandidates("note"));
    }

    [Fact]
    public async Task OversizeFileDoesNotReadOrStoreContent()
    {
        var path = Create();
        using (var file = File.OpenWrite(path)) file.SetLength(16 * 1024 * 1024 + 1);
        var reference = Observe(path);
        var current = await _registry.RevalidateAsync(reference.FileId);
        Assert.Equal("Unavailable", current!.State);
        Assert.Null(current.Sha256);
    }

    [Fact]
    public void InvalidDownloadUrlIsRejected()
    {
        Assert.Throws<ArgumentException>(() => Observe(Create(), sourceUrl: "file:///C:/private.txt"));
    }

    [Fact]
    public void WindowsPathCaseUsesSameIdentityAndTrailingDotAliasIsRejected()
    {
        var path = Create();
        var original = Observe(path);
        var same = Observe(path.ToUpperInvariant(), "changed");
        Assert.Equal(original.FileId, same.FileId);
        Assert.Throws<ArgumentException>(() => Observe(path + "."));
        Assert.False(_registry.CanObservePath(path + "."));
    }

    [Fact]
    public void PrePublicationFilterAcceptsAllowedDirectoryRootAndRejectsOwnWorkspace()
    {
        var allowedRoot = Path.Combine(_root, "watched");
        Directory.CreateDirectory(allowedRoot);
        Assert.True(_registry.CanObservePath(allowedRoot));
        Assert.False(_registry.CanObservePath(_paths.Root));
        Assert.False(_registry.CanObservePath("relative.txt"));
    }

    [Fact]
    public void UnknownRenameCannotTakeOverAnExistingIdentity()
    {
        var path = Create();
        var original = Observe(path);
        Assert.Throws<InvalidOperationException>(() =>
            Observe(path, "renamed", Path.Combine(_root, "unknown.txt")));
        Assert.Equal(original.FileId, _registry.FindCandidates("note").Single().FileId);
    }

    [Fact]
    public void ChangedAfterDeletionCannotReviveDeletedIdentity()
    {
        var path = Create();
        var original = Observe(path);
        File.Delete(path);
        Observe(path, "deleted");
        File.WriteAllText(path, "recreated");
        var replacement = Observe(path, "changed");
        Assert.NotEqual(original.FileId, replacement.FileId);
        Assert.Equal(replacement.FileId, _registry.FindCandidates("note").Single().FileId);
        Assert.Equal(replacement.FileId, Observe(path).FileId);
    }

    [Fact]
    public void MissingShortNameAliasCannotBypassForgottenPathFilter()
    {
        var path = Path.Combine(_root, "FORGOT~1.TXT");
        Assert.False(_registry.CanObservePath(path));
        Assert.Throws<ArgumentException>(() => Observe(path));
    }

    [Fact]
    public void ExistingWindowsShortAliasIsRejectedWhenFilesystemProvidesOne()
    {
        var path = Create("a-file-with-a-long-descriptive-name.txt");
        var original = Observe(path);
        var shortPath = new System.Text.StringBuilder(32_768);
        Assert.NotEqual(0u, GetShortPathName(path, shortPath, (uint)shortPath.Capacity));
        if (!string.Equals(path, shortPath.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            Assert.False(_registry.CanObservePath(shortPath.ToString()));
            Assert.Throws<ArgumentException>(() => Observe(shortPath.ToString(), "changed"));
            _sovereignty.Forget(original.FileId);
            Assert.False(NewRegistry().CanObservePath(shortPath.ToString()));
        }
        else
        {
            // This volume has no alternate short spelling for the file.
            Assert.True(_registry.CanObservePath(path));
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "GetShortPathNameW", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathName(string longPath, System.Text.StringBuilder shortPath, uint bufferLength);

    public void Dispose()
    {
        _writer.Dispose();
        _nodes.Dispose();
        Directory.Delete(_root, true);
    }
}
