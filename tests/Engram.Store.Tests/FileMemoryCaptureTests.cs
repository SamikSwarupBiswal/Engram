using Engram.Store.Events;
using Engram.Store.Governance;
using Engram.Store.Memory;
using Engram.Store.Wiki;

namespace Engram.Store.Tests;

public sealed class FileMemoryCaptureTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "engram-file-memory-capture-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task UserFileInCustomFolderReachesDurableMemoryWithoutAnAutomationTask()
    {
        var input = Path.Combine(_root, "custom-location");
        Directory.CreateDirectory(input);
        var paths = new WorkspacePaths(Path.Combine(_root, "memory"));
        new WorkspaceInitializer().Initialize(paths);
        using var writer = new RawEventWriter(paths, new ContentHasher());
        using var nodes = new WikiNodeStore(paths);
        var sovereignty = new MemorySovereigntySystem(nodes, paths);
        bool Allowed(string path) => path == input || path.StartsWith(input + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        var registry = new FileIdentityRegistry(paths, writer, nodes, sovereignty, Allowed);
        using var bus = new InMemoryEventBus();
        using var timeline = new TimelineSubscriber(bus, writer);
        timeline.Start();
        using var capture = new FileMemoryCapture(registry, bus, Allowed);
        capture.Watch([input]);
        var subdirectory = Path.Combine(input, "new-folder");
        Directory.CreateDirectory(subdirectory);
        var original = Path.Combine(input, "team-report.docx");
        await File.WriteAllBytesAsync(original, [1, 2, 3]);
        var before = await WaitFor(() => registry.FindCandidates("team-report").FirstOrDefault());
        var renamed = Path.Combine(input, "team-report-revised.docx");
        File.Move(original, renamed);
        var after = await WaitFor(() => registry.FindCandidates("team-report-revised").FirstOrDefault());
        Assert.Equal(before.FileId, after.FileId);
        Assert.Equal(renamed, after.Path);
        Assert.Null(after.DownloadSourceUrl);
        capture.Stop();
        var unseen = Path.Combine(input, "not-captured.docx");
        await File.WriteAllBytesAsync(unseen, [4, 5, 6]);
        await Task.Delay(100);
        Assert.Empty(registry.FindCandidates("not-captured"));
        var restored = new FileIdentityRegistry(paths, writer, nodes, sovereignty, Allowed);
        Assert.Equal(after.FileId, Assert.Single(restored.FindCandidates("team-report-revised")).FileId);
        Assert.Equal(0, capture.Failures);
        sovereignty.Forget(after.FileId);
        capture.Watch([input]);
        var rawCount = Directory.GetFiles(paths.Raw, "*.json", SearchOption.AllDirectories).Length;
        await File.WriteAllBytesAsync(renamed, [7, 8, 9]);
        await Task.Delay(150);
        Assert.Equal(rawCount, Directory.GetFiles(paths.Raw, "*.json", SearchOption.AllDirectories).Length);
        Assert.Empty(registry.FindCandidates("team-report-revised"));
        var forgottenRename = Path.Combine(input, "forgotten-renamed.docx");
        File.Move(renamed, forgottenRename);
        await Task.Delay(150);
        await File.WriteAllBytesAsync(forgottenRename, [10, 11, 12]);
        await Task.Delay(150);
        Assert.Equal(rawCount, Directory.GetFiles(paths.Raw, "*.json", SearchOption.AllDirectories).Length);
        Assert.Empty(registry.FindCandidates("forgotten-renamed"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectoryRenameSuspendsCaptureAndPreservesForgettingAcrossRestart(bool throughExcludedFolder)
    {
        var input = Path.Combine(_root, "watched");
        var folder = Path.Combine(input, "folder");
        Directory.CreateDirectory(folder);
        var excluded = Path.Combine(input, "excluded");
        Directory.CreateDirectory(excluded);
        var paths = new WorkspacePaths(Path.Combine(_root, "memory"));
        new WorkspaceInitializer().Initialize(paths);
        using var writer = new RawEventWriter(paths, new ContentHasher());
        using var nodes = new WikiNodeStore(paths);
        var sovereignty = new MemorySovereigntySystem(nodes, paths);
        bool Allowed(string path) => (path == input || path.StartsWith(input + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) &&
            path != excluded && !path.StartsWith(excluded + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        var registry = new FileIdentityRegistry(paths, writer, nodes, sovereignty, Allowed);
        using var bus = new InMemoryEventBus();
        using var timeline = new TimelineSubscriber(bus, writer);
        timeline.Start();
        using var capture = new FileMemoryCapture(registry, bus, Allowed);
        capture.Watch([input]);
        var path = Path.Combine(folder, "private-note.txt");
        await File.WriteAllTextAsync(path, "original private contents");
        var reference = await WaitFor(() => registry.FindCandidates("private-note").FirstOrDefault());
        sovereignty.Forget(reference.FileId);
        var movedFolder = Path.Combine(input, "renamed-folder");
        var intermediate = throughExcludedFolder ? Path.Combine(excluded, "moved") : movedFolder;
        Directory.Move(folder, intermediate);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        while (!registry.ReconciliationRequired) await Task.Delay(20, timeout.Token);
        Assert.True(capture.ReconciliationRequired);
        if (throughExcludedFolder) Directory.Move(intermediate, movedFolder);
        await Task.Delay(100);
        var rawCount = Directory.GetFiles(paths.Raw, "*.json", SearchOption.AllDirectories).Length;
        var movedPath = Path.Combine(movedFolder, "private-note.txt");
        await File.WriteAllTextAsync(movedPath, "edited private contents");
        await Task.Delay(150);
        Assert.Equal(rawCount, Directory.GetFiles(paths.Raw, "*.json", SearchOption.AllDirectories).Length);
        var restarted = new FileIdentityRegistry(paths, writer, nodes, sovereignty, Allowed);
        Assert.True(restarted.ReconciliationRequired);
        Assert.False(restarted.CanObservePath(movedPath));
        Assert.Empty(restarted.FindCandidates("private-note"));
    }

    private static async Task<FileMemoryReference> WaitFor(Func<FileMemoryReference?> find)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        while (true)
        {
            if (find() is { } found) return found;
            await Task.Delay(20, timeout.Token);
        }
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
