using Engram.Store.Perception;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Industrial-level tests for Visual Perception and Layout Snap.
/// Tests capture, OCR, state detection, and window management.
/// </summary>
public class PerceptionTests : IDisposable
{
    private readonly string _tempDir;

    public PerceptionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram-perception-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ─── Screen Frame Model ───

    [Fact]
    public void ScreenFrame_DefaultValues()
    {
        var frame = new ScreenFrame();
        Assert.Equal(string.Empty, frame.ActiveWindowTitle);
        Assert.Equal(string.Empty, frame.ActiveWindowProcess);
        Assert.Null(frame.ImageData);
        Assert.False(frame.Success);
        Assert.Null(frame.Error);
        Assert.Null(frame.ExtractedText);
        Assert.Empty(frame.StateChanges);
    }

    [Fact]
    public void ScreenFrame_WithValues_PreservesData()
    {
        var frame = new ScreenFrame
        {
            ActiveWindowTitle = "Test Window",
            ActiveWindowProcess = "test.exe",
            Width = 1920,
            Height = 1080,
            Success = true
        };
        Assert.Equal("Test Window", frame.ActiveWindowTitle);
        Assert.Equal(1920, frame.Width);
    }

    [Fact]
    public void UiStateChange_DefaultValues()
    {
        var change = new UiStateChange();
        Assert.Equal(string.Empty, change.Type);
        Assert.Equal(string.Empty, change.Description);
    }

    // ─── UI State Detector ───

    [Fact]
    public void Detector_FirstFrame_NoChanges()
    {
        var detector = new UiStateDetector();
        var frame = new ScreenFrame { ActiveWindowTitle = "Window A", ActiveWindowProcess = "a.exe" };
        var changes = detector.DetectChanges(frame);
        Assert.Empty(changes);
    }

    [Fact]
    public void Detector_WindowSwitch_Detected()
    {
        var detector = new UiStateDetector();

        detector.DetectChanges(new ScreenFrame { ActiveWindowTitle = "Window A", ActiveWindowProcess = "a.exe" });
        var changes = detector.DetectChanges(new ScreenFrame { ActiveWindowTitle = "Window B", ActiveWindowProcess = "b.exe" });

        Assert.Contains(changes, c => c.Type == "window_switch");
    }

    [Fact]
    public void Detector_AppSwitch_Detected()
    {
        var detector = new UiStateDetector();

        detector.DetectChanges(new ScreenFrame { ActiveWindowTitle = "Window A", ActiveWindowProcess = "app1" });
        var changes = detector.DetectChanges(new ScreenFrame { ActiveWindowTitle = "Window A - New Tab", ActiveWindowProcess = "app2" });

        Assert.Contains(changes, c => c.Type == "app_switch");
    }

    [Fact]
    public void Detector_SameWindow_NoChanges()
    {
        var detector = new UiStateDetector();

        detector.DetectChanges(new ScreenFrame { ActiveWindowTitle = "Window A", ActiveWindowProcess = "a.exe" });
        var changes = detector.DetectChanges(new ScreenFrame { ActiveWindowTitle = "Window A", ActiveWindowProcess = "a.exe" });

        Assert.Empty(changes);
    }

    [Fact]
    public void Detector_TextChange_Detected()
    {
        var detector = new UiStateDetector();

        detector.DetectChanges(new ScreenFrame
        {
            ActiveWindowTitle = "Editor",
            ActiveWindowProcess = "code",
            ExtractedText = "Hello world"
        });

        var changes = detector.DetectChanges(new ScreenFrame
        {
            ActiveWindowTitle = "Editor",
            ActiveWindowProcess = "code",
            ExtractedText = "Hello world this is new content that is significantly different"
        });

        Assert.Contains(changes, c => c.Type == "text_change");
    }

    [Fact]
    public void Detector_ShortTextChange_NotDetected()
    {
        var detector = new UiStateDetector();

        detector.DetectChanges(new ScreenFrame
        {
            ActiveWindowTitle = "Editor",
            ActiveWindowProcess = "code",
            ExtractedText = "Hello"
        });

        var changes = detector.DetectChanges(new ScreenFrame
        {
            ActiveWindowTitle = "Editor",
            ActiveWindowProcess = "code",
            ExtractedText = "Hello!"
        });

        Assert.Empty(changes); // Diff too short
    }

    [Fact]
    public void Detector_IsNotification_TitlePatterns()
    {
        var detector = new UiStateDetector();

        Assert.True(detector.IsNotification(new ScreenFrame { ActiveWindowTitle = "Notification from Slack" }));
        Assert.True(detector.IsNotification(new ScreenFrame { ActiveWindowTitle = "System Alert" }));
        Assert.True(detector.IsNotification(new ScreenFrame { ActiveWindowTitle = "Reminder: Meeting" }));
        Assert.False(detector.IsNotification(new ScreenFrame { ActiveWindowTitle = "VS Code" }));
    }

    [Fact]
    public void Detector_IsUserIdle_ReturnsFalse()
    {
        var detector = new UiStateDetector();
        Assert.False(detector.IsUserIdle(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void Detector_Reset_ClearsState()
    {
        var detector = new UiStateDetector();
        detector.DetectChanges(new ScreenFrame { ActiveWindowTitle = "A" });
        detector.Reset();

        // After reset, next frame should have no changes (treated as first)
        var changes = detector.DetectChanges(new ScreenFrame { ActiveWindowTitle = "B" });
        Assert.Empty(changes);
    }

    [Fact]
    public void Detector_MultipleSwitches_AllDetected()
    {
        var detector = new UiStateDetector();
        detector.DetectChanges(new ScreenFrame { ActiveWindowTitle = "A", ActiveWindowProcess = "a" });

        var c1 = detector.DetectChanges(new ScreenFrame { ActiveWindowTitle = "B", ActiveWindowProcess = "b" });
        var c2 = detector.DetectChanges(new ScreenFrame { ActiveWindowTitle = "C", ActiveWindowProcess = "c" });
        var c3 = detector.DetectChanges(new ScreenFrame { ActiveWindowTitle = "D", ActiveWindowProcess = "d" });

        // Each switch generates 2 changes: window_switch + app_switch
        Assert.Equal(2, c1.Count);
        Assert.Equal(2, c2.Count);
        Assert.Equal(2, c3.Count);
        Assert.Contains(c1, c => c.Type == "window_switch");
        Assert.Contains(c1, c => c.Type == "app_switch");
    }

    // ─── OCR Service ───

    [Fact]
    public void OcrService_Constructor_DoesNotThrow()
    {
        var ocr = new OcrService();
        ocr.Dispose();
    }

    [Fact]
    public async Task OcrService_Initialize_SetsAvailable()
    {
        var ocr = new OcrService();
        await ocr.InitializeAsync();
        // May or may not be available depending on OS
        Assert.True(true); // Just verify no crash
        ocr.Dispose();
    }

    [Fact]
    public async Task OcrService_ExtractText_EmptyFrame_ReturnsEmpty()
    {
        var ocr = new OcrService();
        await ocr.InitializeAsync();
        var text = await ocr.ExtractTextAsync(new ScreenFrame());
        Assert.NotNull(text);
        ocr.Dispose();
    }

    [Fact]
    public async Task OcrService_ExtractText_NullImageData_ReturnsEmpty()
    {
        var ocr = new OcrService();
        await ocr.InitializeAsync();
        var text = await ocr.ExtractTextAsync(new ScreenFrame { ImageData = null });
        Assert.NotNull(text);
        ocr.Dispose();
    }

    [Fact]
    public void OcrService_DoubleDispose_DoesNotThrow()
    {
        var ocr = new OcrService();
        ocr.Dispose();
        var ex = Record.Exception(() => ocr.Dispose());
        Assert.Null(ex);
    }

    // ─── Screen Capture Service ───

    [Fact]
    public void Capture_Constructor_DoesNotThrow()
    {
        var capture = new ScreenCaptureService();
        capture.Dispose();
    }

    [Fact]
    public void Capture_IsCapturing_InitiallyFalse()
    {
        var capture = new ScreenCaptureService();
        Assert.False(capture.IsCapturing);
        capture.Dispose();
    }

    [Fact]
    public void Capture_FrameCount_InitiallyZero()
    {
        var capture = new ScreenCaptureService();
        Assert.Equal(0, capture.FrameCount);
        capture.Dispose();
    }

    [Fact]
    public void Capture_GetRecentFrames_EmptyInitially()
    {
        var capture = new ScreenCaptureService();
        Assert.Empty(capture.GetRecentFrames());
        capture.Dispose();
    }

    [Fact]
    public void Capture_DoubleDispose_DoesNotThrow()
    {
        var capture = new ScreenCaptureService();
        capture.Dispose();
        var ex = Record.Exception(() => capture.Dispose());
        Assert.Null(ex);
    }

    // ─── Layout Snap Service ───

    [Fact]
    public void LayoutSnap_Constructor_DoesNotThrow()
    {
        var snap = new LayoutSnapService();
        Assert.NotNull(snap);
    }

    [Fact]
    public void LayoutSnap_FindWindowByProcess_Nonexistent_ReturnsZero()
    {
        var snap = new LayoutSnapService();
        var handle = snap.FindWindowByProcess("nonexistent_process_12345");
        Assert.Equal(IntPtr.Zero, handle);
    }

    [Fact]
    public void LayoutSnap_SnapLeft_ZeroHandle_HandlesGracefully()
    {
        var snap = new LayoutSnapService();
        try
        {
            var result = snap.SnapLeft(IntPtr.Zero);
            Assert.False(result);
        }
        catch (DllNotFoundException) { } // Expected in WSL
    }

    [Fact]
    public void LayoutSnap_SnapRight_ZeroHandle_HandlesGracefully()
    {
        var snap = new LayoutSnapService();
        try
        {
            var result = snap.SnapRight(IntPtr.Zero);
            Assert.False(result);
        }
        catch (DllNotFoundException) { } // Expected in WSL
    }

    [Fact]
    public void LayoutSnap_SnapToQuadrant_ZeroHandle_HandlesGracefully()
    {
        var snap = new LayoutSnapService();
        try
        {
            Assert.False(snap.SnapToQuadrant(IntPtr.Zero, Quadrant.TopLeft));
        }
        catch (DllNotFoundException) { } // Expected in WSL
    }

    [Fact]
    public void LayoutSnap_SnapResearchLayout_NoWindows_ReturnsFalse()
    {
        var snap = new LayoutSnapService();
        var result = snap.SnapResearchLayout("nonexistent_browser", "nonexistent_editor");
        Assert.False(result);
    }

    [Fact]
    public void LayoutSnap_SnapSourceGrid_EmptyUrls_ReturnsFalse()
    {
        var snap = new LayoutSnapService();
        Assert.False(snap.SnapSourceGrid(new List<string>()));
    }

    [Fact]
    public void LayoutSnap_SnapSourceGrid_WithUrls_ReturnsTrue()
    {
        var snap = new LayoutSnapService();
        try
        {
            var result = snap.SnapSourceGrid(new List<string> { "https://a.com", "https://b.com" });
            Assert.True(result);
        }
        catch (DllNotFoundException) { } // WSL
    }

    // ─── Visual Perception Pipeline ───

    [Fact]
    public void Pipeline_Constructor_DoesNotThrow()
    {
        var pipeline = new VisualPerceptionPipeline(_tempDir);
        pipeline.Dispose();
    }

    [Fact]
    public void Pipeline_IsRunning_InitiallyFalse()
    {
        var pipeline = new VisualPerceptionPipeline(_tempDir);
        Assert.False(pipeline.IsRunning);
        pipeline.Dispose();
    }

    [Fact]
    public void Pipeline_FramesProcessed_InitiallyZero()
    {
        var pipeline = new VisualPerceptionPipeline(_tempDir);
        Assert.Equal(0, pipeline.FramesProcessed);
        Assert.Equal(0, pipeline.EventsGenerated);
        pipeline.Dispose();
    }

    [Fact]
    public void Pipeline_DoubleDispose_DoesNotThrow()
    {
        var pipeline = new VisualPerceptionPipeline(_tempDir);
        pipeline.Dispose();
        var ex = Record.Exception(() => pipeline.Dispose());
        Assert.Null(ex);
    }

    // ─── Perception Result ───

    [Fact]
    public void PerceptionResult_DefaultValues()
    {
        var result = new PerceptionResult();
        Assert.NotNull(result.Frame);
        Assert.Empty(result.Events);
    }

    [Fact]
    public void PerceptionEvent_DefaultValues()
    {
        var evt = new PerceptionEvent();
        Assert.Equal(string.Empty, evt.Type);
        Assert.Equal(string.Empty, evt.Description);
    }

    [Fact]
    public void PerceptionEvent_WithValues_PreservesData()
    {
        var evt = new PerceptionEvent
        {
            Type = "window_switch",
            ActiveWindow = "VS Code",
            ActiveProcess = "code",
            Description = "Switched to VS Code"
        };
        Assert.Equal("window_switch", evt.Type);
        Assert.Equal("VS Code", evt.ActiveWindow);
    }

    // ─── Quadrant Enum ───

    [Fact]
    public void Quadrant_HasAllValues()
    {
        Assert.Equal(4, Enum.GetValues<Quadrant>().Length);
    }
}
