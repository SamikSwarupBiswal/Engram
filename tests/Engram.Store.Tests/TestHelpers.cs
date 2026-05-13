using System.Text.Json;

namespace Engram.Store.Tests;

/// <summary>
/// Creates a temporary directory for test isolation.
/// Automatically cleans up on dispose.
/// </summary>
public sealed class TempWorkspace : IDisposable
{
    public string Root { get; }
    public WorkspacePaths Paths { get; }

    public TempWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "engram_test_" + Guid.NewGuid().ToString("N")[..8]);
        Paths = new WorkspacePaths(Root);
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            try { Directory.Delete(Root, recursive: true); }
            catch { /* best effort cleanup */ }
        }
    }
}

/// <summary>
/// Helper to create test raw events with sensible defaults.
/// </summary>
public static class TestEvents
{
    public static RawEvent Create(
        string? eventType = null,
        string? source = null,
        string? text = null,
        DateTimeOffset? capturedAt = null,
        string? privacyClass = null)
    {
        return new RawEvent
        {
            EventId = Guid.NewGuid().ToString(),
            EventType = eventType ?? "test_event",
            Source = source ?? "test_source",
            Text = text ?? "Hello, Engram!",
            CapturedAt = capturedAt ?? DateTimeOffset.UtcNow,
            PrivacyClass = privacyClass ?? "private",
            ProcessingStatus = "pending",
            SourceUri = null,
            ActiveWindow = null,
            Metadata = null,
            Hash = string.Empty
        };
    }

    public static RawEvent CreateWithMetadata()
    {
        return new RawEvent
        {
            EventId = Guid.NewGuid().ToString(),
            EventType = "file_change",
            Source = "file_watcher",
            Text = "New document created",
            CapturedAt = new DateTimeOffset(2026, 5, 13, 10, 0, 0, TimeSpan.Zero),
            PrivacyClass = "sensitive",
            ProcessingStatus = "pending",
            SourceUri = "file:///C:/Users/Samik/Documents/report.pdf",
            ActiveWindow = "Explorer",
            Metadata = new Dictionary<string, string>
            {
                ["file_name"] = "report.pdf",
                ["file_size"] = "1024"
            },
            Hash = string.Empty
        };
    }
}
