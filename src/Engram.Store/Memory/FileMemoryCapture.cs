using Engram.Store.Events;
using Engram.Store.Perception;

namespace Engram.Store.Memory;

/// <summary>Connects consented filesystem observations to shared durable file identities.</summary>
public sealed class FileMemoryCapture : IDisposable
{
    private readonly FileWatcherService _watcher;
    private readonly FileIdentityRegistry _registry;
    private readonly IDisposable[] _subscriptions;
    private readonly object _gate = new();
    private bool _disposed;
    public int Failures { get; private set; }
    public string? LastError { get; private set; }
    public bool ReconciliationRequired => _registry.ReconciliationRequired || _watcher.ReconciliationRequired || Failures > 0;
    public IReadOnlyList<string> WatchedPaths => _watcher.GetWatchedPaths();

    public FileMemoryCapture(FileIdentityRegistry registry, IEventBus bus, Func<string, bool> pathFilter)
    {
        _registry = registry;
        _watcher = new(bus)
        {
            PathFilter = path => pathFilter(path) && registry.CanObservePath(path),
            RenameFilter = registry.CanObserveRename
        };
        _subscriptions = new[] { "created", "changed", "renamed", "deleted" }
            .Select(change => bus.Subscribe("perception.file_" + change, envelope =>
            {
                lock (_gate)
                {
                    if (_disposed || envelope.Source != "file_watcher_service" || envelope.Payload is not SemanticFileEvent file) return;
                    try
                    {
                        registry.Observe(new(file.FilePath, file.ChangeType, file.Timestamp, file.OldFilePath));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
                    {
                        Failures++;
                        LastError = ex.Message;
                    }
                }
            })).Append(bus.Subscribe("perception.file_coverage_uncertain", envelope =>
            {
                lock (_gate)
                {
                    if (_disposed || envelope.Source != "file_watcher_service") return;
                    try { registry.SuspendCaptureForReconciliation(); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                    {
                        Failures++;
                        LastError = ex.Message;
                    }
                }
            })).ToArray();
    }

    public void Watch(IEnumerable<string> paths)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _watcher.StopAll();
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase)) _watcher.WatchPath(path);
    }

    public void Stop() => _watcher.StopAll();

    public void Dispose()
    {
        _watcher.Dispose();
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var subscription in _subscriptions) subscription.Dispose();
        }
    }
}
