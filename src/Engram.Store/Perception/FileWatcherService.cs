using Engram.Store.Events;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Perception;

/// <summary>
/// Semantic file watcher service.
/// 
/// NOT raw filesystem spam. Semantic density.
/// 
/// Emits semantic events like:
/// - "New project folder created"
/// - "PDF downloaded"
/// - "Resume modified"
/// - "Git repo initialized"
/// 
/// NEVER ingests:
/// - entire file contents automatically
/// - all filesystem data
/// - recursive full-drive scans
/// 
/// That becomes spyware architecture.
/// </summary>
public class FileWatcherService : IDisposable
{
    private readonly IEventBus _eventBus;
    private readonly ILogger<FileWatcherService>? _logger;

    private readonly List<string> _watchedPaths = new();
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private volatile bool _disposed;

    /// <summary>Legacy setting retained for callers. Lifecycle events are forwarded without dropping later edits.</summary>
    public TimeSpan DeduplicationWindow { get; set; } = TimeSpan.FromSeconds(5);
    public Func<string, bool> PathFilter { get; set; } = _ => true;
    public Func<string, string, bool> RenameFilter { get; set; } = (_, _) => true;
    public bool ReconciliationRequired { get; private set; }

    public FileWatcherService(
        IEventBus eventBus,
        ILogger<FileWatcherService>? logger = null)
    {
        _eventBus = eventBus;
        _logger = logger;
    }

    /// <summary>
    /// Add a path to watch.
    /// </summary>
    public void WatchPath(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        path = Path.GetFullPath(path);
        if (!PathFilter(path) || HasReparsePoint(path)) return;
        lock (_lock)
        {
            if (_watchedPaths.Contains(path, StringComparer.OrdinalIgnoreCase)) return;
        }
        if (!Directory.Exists(path))
        {
            _logger?.LogWarning("Watch path does not exist: {Path}", path);
            return;
        }

        var watcher = new FileSystemWatcher(path)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName,
            IncludeSubdirectories = true,
            InternalBufferSize = 64 * 1024
        };

        watcher.Created += OnFileCreated;
        watcher.Changed += OnFileChanged;
        watcher.Renamed += OnFileRenamed;
        watcher.Deleted += OnFileDeleted;
        watcher.Error += OnWatcherError;

        lock (_lock)
        {
            if (_disposed || _watchedPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                watcher.Dispose();
                return;
            }
            _watchers.Add(watcher);
            _watchedPaths.Add(path);
            _directories.Add(path);
            var scannedDirectories = 0;
            foreach (var directory in Directory.EnumerateDirectories(path, "*", new EnumerationOptions
                     { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true }).Take(10_001))
            {
                if (++scannedDirectories > 10_000) { ReconciliationRequired = true; break; }
                if (PathFilter(directory)) _directories.Add(directory);
            }
            watcher.EnableRaisingEvents = true;
            if (ReconciliationRequired)
                _eventBus.Publish(new EventEnvelope
                {
                    EventType = "perception.file_coverage_uncertain", Source = "file_watcher_service",
                    Payload = new { reason = "The directory discovery limit was reached. Reconcile file identities before capture continues." }
                });
        }

        _logger?.LogInformation("Watching path: {Path}", path);
    }

    /// <summary>
    /// Stop watching all paths.
    /// </summary>
    public void StopAll()
    {
        lock (_lock)
        {
            foreach (var watcher in _watchers)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            _watchers.Clear();
            _watchedPaths.Clear();
            _directories.Clear();
        }

        _logger?.LogInformation("Stopped all file watchers");
    }

    /// <summary>
    /// Get watched paths.
    /// </summary>
    public IReadOnlyList<string> GetWatchedPaths()
    {
        lock (_lock) return _watchedPaths.ToArray();
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        ProcessFileEvent(e.FullPath, "created");
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        ProcessFileEvent(e.FullPath, "changed");
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        lock (_lock)
        {
            if (_disposed || _watchedPaths.Count == 0) return;
            // Directory moves do not produce child rename events. Suspend before
            // file-level filters can hide a move through an excluded location.
            var knownDirectory = _directories.Contains(e.OldFullPath);
            if (knownDirectory || Directory.Exists(e.FullPath))
            {
                if (knownDirectory || PathFilter(e.FullPath))
                {
                    _directories.Remove(e.OldFullPath);
                    OnWatcherError(this, new ErrorEventArgs(new IOException("A watched directory moved; file identities need reconciliation.")));
                }
                return;
            }
            if (!RenameFilter(e.OldFullPath, e.FullPath)) return;
            ProcessFileEvent(e.FullPath, "renamed", e.OldFullPath);
        }
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        ProcessFileEvent(e.FullPath, "deleted");
    }

    private void ProcessFileEvent(string filePath, string changeType, string? oldFilePath = null)
    {
        if (_disposed) return;
        if (!PathFilter(filePath) || HasReparsePoint(filePath))
        {
            if (oldFilePath != null && PathFilter(oldFilePath) && !HasReparsePoint(oldFilePath))
                ProcessFileEvent(oldFilePath, "deleted");
            return;
        }
        if (oldFilePath != null && (!PathFilter(oldFilePath) || HasReparsePoint(oldFilePath)))
        {
            oldFilePath = null;
            changeType = "created";
        }
        lock (_lock)
        {
            if (_disposed || _watchedPaths.Count == 0) return;
            var isKnownDirectory = _directories.Contains(oldFilePath ?? filePath);
            if (Directory.Exists(filePath) || isKnownDirectory)
            {
                if (changeType == "renamed" || changeType == "deleted")
                {
                    ReconciliationRequired = true;
                    _directories.Remove(oldFilePath ?? filePath);
                    _eventBus.Publish(new EventEnvelope
                    {
                        EventType = "perception.file_coverage_uncertain", Source = "file_watcher_service",
                        Payload = new { reason = "A watched directory moved or was deleted. Reconcile contained file identities." }
                    });
                }
                if (Directory.Exists(filePath)) _directories.Add(filePath);
                return;
            }
        }

        // Classify the file event semantically
        var semanticEvent = ClassifyFileEvent(filePath, changeType);
        if (semanticEvent != null)
        {
            semanticEvent.OldFilePath = oldFilePath;
            try
            {
                lock (_lock)
                {
                    if (_disposed || _watchedPaths.Count == 0) return;
                    _eventBus.Publish(new EventEnvelope
                    {
                        EventType = $"perception.file_{changeType}",
                        Source = "file_watcher_service",
                        Payload = semanticEvent
                    });
                }

                _logger?.LogDebug("File event: {Type} - {Path} ({Semantic})",
                    changeType, filePath, semanticEvent.Category);
            }
            catch (ObjectDisposedException ex)
            {
                _logger?.LogWarning(ex, "Could not publish file event because event bus is disposed");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Unexpected error publishing file event");
            }
        }
    }

    /// <summary>
    /// Classify a file event into a semantic category.
    /// </summary>
    private static SemanticFileEvent? ClassifyFileEvent(string filePath, string changeType)
    {
        var fileName = Path.GetFileName(filePath);
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var directory = Path.GetDirectoryName(filePath);
        var directoryName = directory != null ? Path.GetFileName(directory) : string.Empty;

        var category = ClassifyCategory(extension, fileName, directoryName);
        var significance = ClassifySignificance(extension, fileName, changeType);

        return new SemanticFileEvent
        {
            FilePath = filePath,
            FileName = fileName,
            Extension = extension,
            Directory = directoryName,
            ChangeType = changeType,
            Category = category,
            Significance = significance,
            Description = GenerateDescription(changeType, category, fileName),
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    private static string ClassifyCategory(string extension, string fileName, string directory)
    {
        // Project-related
        if (extension is ".cs" or ".ts" or ".js" or ".py" or ".rs" or ".go")
            return "source_code";
        if (extension is ".csproj" or ".sln" or ".json" && fileName.Contains("package"))
            return "project_config";
        if (fileName == ".gitignore" || fileName == ".git")
            return "version_control";

        // Documents
        if (extension is ".pdf" or ".docx" or ".doc")
            return "document";
        if (extension is ".md" or ".txt")
            return "text";

        // Media
        if (extension is ".png" or ".jpg" or ".jpeg" or ".gif")
            return "image";
        if (extension is ".mp4" or ".avi" or ".mov")
            return "video";

        // Downloads
        if (directory.Contains("Downloads", StringComparison.OrdinalIgnoreCase))
            return "download";

        // Data
        if (extension is ".csv" or ".xlsx" or ".json" or ".xml")
            return "data";

        return "other";
    }

    private static EventSignificance ClassifySignificance(string extension, string fileName, string changeType)
    {
        // High significance: new projects, git repos, important documents
        if (fileName == ".git" || fileName == ".gitignore")
            return EventSignificance.High;
        if (extension is ".sln" or ".csproj" && changeType == "created")
            return EventSignificance.High;
        if (extension is ".pdf" or ".docx" && changeType == "created")
            return EventSignificance.High;

        // Medium significance: source code changes, config changes
        if (extension is ".cs" or ".ts" or ".js" or ".py")
            return EventSignificance.Medium;
        if (extension is ".json" or ".xml" or ".yaml")
            return EventSignificance.Medium;

        // Low significance: temporary files, caches
        if (extension is ".tmp" or ".cache" or ".log")
            return EventSignificance.Low;

        return EventSignificance.Medium;
    }

    private static string GenerateDescription(string changeType, string category, string fileName)
    {
        return category switch
        {
            "source_code" => $"Source code {changeType}: {fileName}",
            "project_config" => $"Project configuration {changeType}: {fileName}",
            "version_control" => $"Version control {changeType}: {fileName}",
            "document" => $"Document {changeType}: {fileName}",
            "download" => $"Download {changeType}: {fileName}",
            _ => $"File {changeType}: {fileName}"
        };
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        lock (_lock)
        {
            if (_disposed || _watchedPaths.Count == 0) return;
            ReconciliationRequired = true;
            _logger?.LogError(e.GetException(), "File watcher coverage is uncertain; reconciliation is required");
            _eventBus.Publish(new EventEnvelope
            {
                EventType = "perception.file_coverage_uncertain", Source = "file_watcher_service",
                Payload = new { reason = "The file watcher lost events. Current file state must be reconciled." }
            });
        }
    }

    private static bool HasReparsePoint(string path)
    {
        try
        {
            for (var current = path; current != null; current = Path.GetDirectoryName(current))
                if ((File.Exists(current) || Directory.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            StopAll();
        }
    }
}

/// <summary>
/// Semantic file event.
/// </summary>
public class SemanticFileEvent
{
    public string FilePath { get; set; } = string.Empty;
    public string? OldFilePath { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string Directory { get; set; } = string.Empty;
    public string ChangeType { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public EventSignificance Significance { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
}

public enum EventSignificance
{
    Low,      // Temporary files, caches
    Medium,   // Source code, config
    High      // Projects, git repos, important documents
}
