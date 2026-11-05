using System.Collections.Concurrent;
using System.Text.Json;
using Engram.Store.Events;
using Engram.Store.Perception;

namespace Engram.Store.Tests;

public sealed class FilePerceptionLifecycleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "engram-file-perception-" + Guid.NewGuid().ToString("N"));
    public FilePerceptionLifecycleTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task UserCreateEditRenameDeleteRetainsBothPathsAndLaterEdits()
    {
        using var bus = new InMemoryEventBus();
        var events = new ConcurrentQueue<SemanticFileEvent>();
        using var all = bus.SubscribeAll(envelope =>
        {
            if (envelope.Payload is SemanticFileEvent file) events.Enqueue(file);
        });
        using var watcher = new FileWatcherService(bus);
        watcher.WatchPath(_root);
        var source = Path.Combine(_root, "note.txt");
        var renamed = Path.Combine(_root, "renamed.txt");

        await File.WriteAllTextAsync(source, "first version");
        await WaitUntil(() => events.Any(e => e.FilePath == source && e.ChangeType == "created"));
        await WaitUntil(() => events.Any(e => e.FilePath == source && e.ChangeType == "changed"));
        var changed = events.Count(e => e.ChangeType == "changed");
        await File.WriteAllTextAsync(source, "second version");
        await WaitUntil(() => events.Count(e => e.ChangeType == "changed") > changed);
        File.Move(source, renamed);
        await WaitUntil(() => events.Any(e => e.ChangeType == "renamed"));
        var rename = events.First(e => e.ChangeType == "renamed");
        Assert.Equal(renamed, rename.FilePath);
        Assert.Equal(source, JsonSerializer.SerializeToElement(rename).GetProperty("OldFilePath").GetString());
        File.Delete(renamed);
        await WaitUntil(() => events.Any(e => e.FilePath == renamed && e.ChangeType == "deleted"));
        watcher.StopAll();
        var count = events.Count;
        await File.WriteAllTextAsync(Path.Combine(_root, "after-stop.txt"), "not observed");
        await Task.Delay(100);
        Assert.Equal(count, events.Count);
    }

    [Fact]
    public async Task ExcludedAndLinkedFilesAreNotObserved()
    {
        using var bus = new InMemoryEventBus();
        var events = new ConcurrentQueue<SemanticFileEvent>();
        using var all = bus.SubscribeAll(envelope => { if (envelope.Payload is SemanticFileEvent file) events.Enqueue(file); });
        using var watcher = new FileWatcherService(bus) { PathFilter = path => !path.Contains("excluded", StringComparison.OrdinalIgnoreCase) };
        watcher.WatchPath(_root);
        watcher.WatchPath(_root);
        Assert.Single(watcher.GetWatchedPaths());
        var excluded = Path.Combine(_root, "excluded.txt");
        await File.WriteAllTextAsync(excluded, "private contents");
        var visible = Path.Combine(_root, "visible.txt");
        await File.WriteAllTextAsync(visible, "visible");
        await WaitUntil(() => events.Any(e => e.FilePath == visible));
        Assert.DoesNotContain(events, e => e.FilePath == excluded);
        var link = Path.Combine(_root, "linked.txt");
        File.CreateSymbolicLink(link, visible);
        try
        {
            await Task.Delay(100);
            Assert.DoesNotContain(events, e => e.FilePath == link);
            File.Move(visible, excluded + ".moved");
            await WaitUntil(() => events.Any(e => e.FilePath == visible && e.ChangeType == "deleted"));
            Assert.DoesNotContain(events, e => e.FilePath == excluded + ".moved");
        }
        finally { File.Delete(link); }
    }

    [Fact]
    public void WatcherOverflowReportsCoverageUncertainty()
    {
        using var bus = new InMemoryEventBus();
        EventEnvelope? observed = null;
        using var subscription = bus.Subscribe("perception.file_coverage_uncertain", envelope => observed = envelope);
        using var watcher = new FileWatcherService(bus);
        watcher.WatchPath(_root);
        typeof(FileWatcherService).GetMethod("OnWatcherError", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(watcher, [this, new ErrorEventArgs(new InternalBufferOverflowException())]);
        Assert.True(watcher.ReconciliationRequired);
        Assert.NotNull(observed);
    }

    [Fact]
    public async Task StopWaitsForRenamePolicyAndLateCallbacksCannotRunIt()
    {
        using var bus = new InMemoryEventBus();
        using var watcher = new FileWatcherService(bus);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        watcher.RenameFilter = (_, _) =>
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            release.Wait(TimeSpan.FromSeconds(3));
            return true;
        };
        watcher.WatchPath(_root);
        var callback = typeof(FileWatcherService).GetMethod("OnFileRenamed", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var args = new RenamedEventArgs(WatcherChangeTypes.Renamed, _root, "after.txt", "before.txt");
        var renaming = Task.Run(() => callback.Invoke(watcher, [this, args]));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var stopping = Task.Run(watcher.StopAll);
            await Task.Delay(100);
            Assert.False(stopping.IsCompleted);
            release.Set();
            await Task.WhenAll(renaming, stopping);
            callback.Invoke(watcher, [this, args]);
            Assert.Equal(1, calls);
        }
        finally { release.Set(); await renaming; }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        while (!condition()) await Task.Delay(20, timeout.Token);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
