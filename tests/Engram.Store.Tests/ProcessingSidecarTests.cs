using System.Text.Json;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for per-event processing sidecar.
/// Derived from: D-012, REQ-004 (immutable raw events)
/// </summary>
public class ProcessingSidecarTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void Sidecar_Write_CreatesMetaFile()
    {
        var sidecar = new ProcessingSidecar(_workspace.Paths);
        var eventPath = Path.Combine(_workspace.Paths.Raw, "2026-05-13", "test-event.json");
        Directory.CreateDirectory(Path.GetDirectoryName(eventPath)!);

        var result = sidecar.Write(eventPath, new ProcessingState
        {
            Status = "pending",
            LastProcessedAt = null,
            Error = null,
            RetryCount = 0
        });

        Assert.True(File.Exists(result));
        Assert.EndsWith(".meta.json", result);
    }

    [Fact]
    public void Sidecar_Read_RoundTrips()
    {
        var sidecar = new ProcessingSidecar(_workspace.Paths);
        var eventPath = Path.Combine(_workspace.Paths.Raw, "2026-05-13", "test-event.json");
        Directory.CreateDirectory(Path.GetDirectoryName(eventPath)!);

        var state = new ProcessingState
        {
            Status = "processed",
            LastProcessedAt = new DateTimeOffset(2026, 5, 13, 10, 0, 0, TimeSpan.Zero),
            Error = "timeout on first try",
            RetryCount = 2
        };

        sidecar.Write(eventPath, state);
        var read = sidecar.Read(eventPath);

        Assert.NotNull(read);
        Assert.Equal("processed", read!.Status);
        Assert.Equal(state.LastProcessedAt, read.LastProcessedAt);
        Assert.Equal("timeout on first try", read.Error);
        Assert.Equal(2, read.RetryCount);
    }

    [Fact]
    public void Sidecar_Read_ReturnsNull_WhenNoSidecar()
    {
        var sidecar = new ProcessingSidecar(_workspace.Paths);
        var result = sidecar.Read("/nonexistent/event.json");
        Assert.Null(result);
    }

    [Fact]
    public void Sidecar_Write_DoesNotModifyRawEventPayload()
    {
        var sidecar = new ProcessingSidecar(_workspace.Paths);
        var eventPath = Path.Combine(_workspace.Paths.Raw, "2026-05-13", "test-event.json");
        Directory.CreateDirectory(Path.GetDirectoryName(eventPath)!);

        var originalContent = "{\"event_id\":\"test\"}";
        File.WriteAllText(eventPath, originalContent);

        sidecar.Write(eventPath, new ProcessingState { Status = "processed" });

        Assert.Equal(originalContent, File.ReadAllText(eventPath));
    }

    [Fact]
    public void Sidecar_UpdateStatus_WithoutMutatingPayload()
    {
        var sidecar = new ProcessingSidecar(_workspace.Paths);
        var eventPath = Path.Combine(_workspace.Paths.Raw, "2026-05-13", "test-event.json");
        Directory.CreateDirectory(Path.GetDirectoryName(eventPath)!);

        File.WriteAllText(eventPath, "{\"immutable\":true}");

        sidecar.Write(eventPath, new ProcessingState { Status = "pending" });
        sidecar.Write(eventPath, new ProcessingState { Status = "processed", LastProcessedAt = DateTimeOffset.UtcNow });

        var state = sidecar.Read(eventPath);
        Assert.Equal("processed", state!.Status);
        Assert.Equal("{\"immutable\":true}", File.ReadAllText(eventPath));
    }

    [Fact]
    public void Sidecar_SnakeCaseJson()
    {
        var sidecar = new ProcessingSidecar(_workspace.Paths);
        var eventPath = Path.Combine(_workspace.Paths.Raw, "2026-05-13", "test.json");
        Directory.CreateDirectory(Path.GetDirectoryName(eventPath)!);

        sidecar.Write(eventPath, new ProcessingState { Status = "pending", RetryCount = 1 });
        var json = File.ReadAllText(eventPath + ".meta.json");

        Assert.Contains("\"processing_status\"", json);
        Assert.Contains("\"last_processed_at\"", json);
        Assert.Contains("\"processing_error\"", json);
        Assert.Contains("\"retry_count\"", json);
    }
}
