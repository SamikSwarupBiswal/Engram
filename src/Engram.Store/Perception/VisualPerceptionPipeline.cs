using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Perception;

/// <summary>
/// Orchestrates the visual perception pipeline.
/// Captures screen → OCR → detect state changes → generate raw events.
/// Runs continuously at 1-2s intervals.
/// </summary>
public class VisualPerceptionPipeline : IDisposable
{
    private readonly ScreenCaptureService _capture;
    private readonly OcrService _ocr;
    private readonly UiStateDetector _detector;
    private readonly string _eventsDir;
    private readonly ILogger<VisualPerceptionPipeline>? _logger;
    private bool _disposed;
    private CancellationTokenSource? _cts;
    private Task? _pipelineTask;

    public bool IsRunning => _pipelineTask != null && !_pipelineTask.IsCompleted;
    public int FramesProcessed { get; private set; }
    public int EventsGenerated { get; private set; }

    public VisualPerceptionPipeline(
        string eventsDir,
        ScreenCaptureService? capture = null,
        OcrService? ocr = null,
        UiStateDetector? detector = null,
        ILogger<VisualPerceptionPipeline>? logger = null)
    {
        _eventsDir = eventsDir;
        _capture = capture ?? new ScreenCaptureService(logger as ILogger<ScreenCaptureService>);
        _ocr = ocr ?? new OcrService(logger as ILogger<OcrService>);
        _detector = detector ?? new UiStateDetector(logger as ILogger<UiStateDetector>);
        _logger = logger;
    }

    /// <summary>
    /// Start the perception pipeline.
    /// </summary>
    public async Task StartAsync(TimeSpan? captureInterval = null)
    {
        if (IsRunning) return;

        await _ocr.InitializeAsync();
        _cts = new CancellationTokenSource();

        var interval = captureInterval ?? TimeSpan.FromSeconds(2);
        _pipelineTask = RunPipelineAsync(interval, _cts.Token);

        _logger?.LogInformation("Visual perception pipeline started");
    }

    /// <summary>
    /// Stop the perception pipeline.
    /// </summary>
    public async Task StopAsync()
    {
        if (_cts == null) return;

        _cts.Cancel();
        _capture.StopCapture();

        if (_pipelineTask != null)
        {
            try { await _pipelineTask; } catch { }
        }

        _cts.Dispose();
        _cts = null;
        _pipelineTask = null;

        _logger?.LogInformation("Visual perception pipeline stopped ({Frames} frames, {Events} events)",
            FramesProcessed, EventsGenerated);
    }

    /// <summary>
    /// Process a single frame (for manual/testing use).
    /// </summary>
    public async Task<PerceptionResult> ProcessSingleFrameAsync()
    {
        var frame = _capture.CaptureSingle();
        return await ProcessFrameAsync(frame);
    }

    private async Task RunPipelineAsync(TimeSpan interval, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var frame = _capture.CaptureSingle();
                var result = await ProcessFrameAsync(frame);
                FramesProcessed++;

                if (result.Events.Count > 0)
                {
                    EventsGenerated += result.Events.Count;
                    await PersistEventsAsync(result.Events, ct);
                }

                await Task.Delay(interval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Pipeline frame processing failed");
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    private async Task<PerceptionResult> ProcessFrameAsync(ScreenFrame frame)
    {
        var result = new PerceptionResult { Frame = frame };

        // 1. OCR the frame
        if (_ocr.IsAvailable && frame.ImageData != null)
        {
            frame.ExtractedText = await _ocr.ExtractTextAsync(frame);
        }

        // 2. Detect state changes
        var changes = _detector.DetectChanges(frame);
        frame.StateChanges = changes;

        // 3. Generate raw events from state changes
        foreach (var change in changes)
        {
            result.Events.Add(new PerceptionEvent
            {
                Timestamp = frame.Timestamp,
                Type = change.Type,
                ActiveWindow = frame.ActiveWindowTitle,
                ActiveProcess = frame.ActiveWindowProcess,
                Description = change.Description,
                OldValue = change.OldValue,
                NewValue = change.NewValue,
                ExtractedText = frame.ExtractedText
            });
        }

        // 4. Check for notifications
        if (_detector.IsNotification(frame))
        {
            result.Events.Add(new PerceptionEvent
            {
                Timestamp = frame.Timestamp,
                Type = "notification",
                ActiveWindow = frame.ActiveWindowTitle,
                Description = $"Notification detected: {frame.ActiveWindowTitle}",
                ExtractedText = frame.ExtractedText
            });
        }

        return result;
    }

    private async Task PersistEventsAsync(List<PerceptionEvent> events, CancellationToken ct)
    {
        var dateDir = Path.Combine(_eventsDir, DateTimeOffset.UtcNow.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(dateDir);

        foreach (var evt in events)
        {
            var eventId = Guid.NewGuid().ToString("N")[..12];
            var path = Path.Combine(dateDir, $"{eventId}.json");
            var json = JsonSerializer.Serialize(evt, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                WriteIndented = true
            });
            await File.WriteAllTextAsync(path, json, ct);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _capture.Dispose();
            _ocr.Dispose();
            _cts?.Dispose();
            _disposed = true;
        }
    }
}

public class PerceptionResult
{
    public ScreenFrame Frame { get; init; } = new();
    public List<PerceptionEvent> Events { get; init; } = new();
}

public class PerceptionEvent
{
    public DateTimeOffset Timestamp { get; init; }
    public string Type { get; init; } = string.Empty;
    public string ActiveWindow { get; init; } = string.Empty;
    public string ActiveProcess { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
    public string? ExtractedText { get; init; }
}
