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
    private readonly HashSet<string> _recentEvents = new();
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>Minimum interval between duplicate events for the same file.</summary>
    public TimeSpan DeduplicationWindow { get; set; } = TimeSpan.FromSeconds(5);

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

        watcher.EnableRaisingEvents = true;
        _watchers.Add(watcher);
        _watchedPaths.Add(path);

        _logger?.LogInformation("Watching path: {Path}", path);
    }

    /// <summary>
    /// Stop watching all paths.
    /// </summary>
    public void StopAll()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();
        _watchedPaths.Clear();

        _logger?.LogInformation("Stopped all file watchers");
    }

    /// <summary>
    /// Get watched paths.
    /// </summary>
    public IReadOnlyList<string> GetWatchedPaths()
    {
        return _watchedPaths.AsReadOnly();
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
        ProcessFileEvent(e.FullPath, "renamed");
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        ProcessFileEvent(e.FullPath, "deleted");
    }

    private void ProcessFileEvent(string filePath, string changeType)
    {
        // Deduplicate rapid events for the same file
        var dedupeKey = $"{filePath}:{changeType}";
        lock (_lock)
        {
            if (_recentEvents.Contains(dedupeKey)) return;
            _recentEvents.Add(dedupeKey);

            // Clean up old entries after deduplication window
            _ = Task.Delay(DeduplicationWindow).ContinueWith(_ =>
            {
                lock (_lock) _recentEvents.Remove(dedupeKey);
            });
        }

        // Classify the file event semantically
        var semanticEvent = ClassifyFileEvent(filePath, changeType);
        if (semanticEvent != null)
        {
            _eventBus.Publish(new EventEnvelope
            {
                EventType = $"perception.file_{changeType}",
                Source = "file_watcher_service",
                Payload = semanticEvent
            });

            _logger?.LogDebug("File event: {Type} - {Path} ({Semantic})",
                changeType, filePath, semanticEvent.Category);
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
        _logger?.LogError(e.GetException(), "FileWatcher error — attempting recovery");
        // Recovery logic would go here
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            StopAll();
            _disposed = true;
        }
    }
}

/// <summary>
/// Semantic file event.
/// </summary>
public class SemanticFileEvent
{
    public string FilePath { get; set; } = string.Empty;
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
