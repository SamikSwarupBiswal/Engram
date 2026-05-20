using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using System.Runtime.Versioning;

namespace Engram.Store.Perception;

/// <summary>
/// Captures screen frames and active window information.
/// Runs on Windows via the .NET sidecar.
/// Captures at configurable intervals (default 1-2s).
/// </summary>
[SupportedOSPlatform("windows")]
public class ScreenCaptureService : IDisposable
{
    private readonly ILogger<ScreenCaptureService>? _logger;
    private readonly List<ScreenFrame> _recentFrames = new();
    private readonly object _lock = new();
    private Timer? _captureTimer;
    private bool _disposed;
    private int _frameCount;

    public bool IsCapturing => _captureTimer != null;
    public int FrameCount => _frameCount;

    public ScreenCaptureService(ILogger<ScreenCaptureService>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Start continuous screen capture at the specified interval.
    /// </summary>
    public void StartCapture(TimeSpan? interval = null)
    {
        if (_captureTimer != null) return;

        var captureInterval = interval ?? TimeSpan.FromSeconds(2);
        _captureTimer = new Timer(CaptureFrame, null, TimeSpan.Zero, captureInterval);
        _logger?.LogInformation("Screen capture started (interval: {Interval}ms)", captureInterval.TotalMilliseconds);
    }

    /// <summary>
    /// Stop continuous capture.
    /// </summary>
    public void StopCapture()
    {
        _captureTimer?.Dispose();
        _captureTimer = null;
        _logger?.LogInformation("Screen capture stopped ({Frames} frames)", _frameCount);
    }

    /// <summary>
    /// Capture a single screen frame.
    /// </summary>
    public ScreenFrame CaptureSingle()
    {
        return CaptureFrame();
    }

    /// <summary>
    /// Get recent frames (last N).
    /// </summary>
    public List<ScreenFrame> GetRecentFrames(int count = 10)
    {
        lock (_lock)
        {
            return _recentFrames.TakeLast(count).ToList();
        }
    }

    /// <summary>
    /// Get the active window title.
    /// </summary>
    public static string GetActiveWindowTitle()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return string.Empty;

            var length = GetWindowTextLength(hwnd);
            if (length == 0) return string.Empty;

            var sb = new StringBuilder(length + 1);
            GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Get the active window process name.
    /// </summary>
    public static string GetActiveWindowProcess()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return string.Empty;

            GetWindowThreadProcessId(hwnd, out var processId);
            var process = System.Diagnostics.Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    private void CaptureFrame(object? state)
    {
        CaptureFrame();
    }

    private ScreenFrame CaptureFrame()
    {
        var frame = new ScreenFrame
        {
            Timestamp = DateTimeOffset.UtcNow,
            ActiveWindowTitle = GetActiveWindowTitle(),
            ActiveWindowProcess = GetActiveWindowProcess()
        };

        try
        {
            var bounds = GetScreenBounds();
            using var bitmap = new Bitmap(bounds.Width, bounds.Height);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);

            // Convert to byte array (JPEG for smaller size)
            using var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Jpeg);
            frame.ImageData = ms.ToArray();
            frame.Width = bounds.Width;
            frame.Height = bounds.Height;
            frame.Success = true;

            Interlocked.Increment(ref _frameCount);
        }
        catch (Exception ex)
        {
            frame.Success = false;
            frame.Error = ex.Message;
            _logger?.LogWarning(ex, "Screen capture failed");
        }

        lock (_lock)
        {
            _recentFrames.Add(frame);
            if (_recentFrames.Count > 100)
                _recentFrames.RemoveAt(0);
        }

        return frame;
    }

    private static Rectangle GetScreenBounds()
    {
        var width = GetSystemMetrics(SM_CXSCREEN);
        var height = GetSystemMetrics(SM_CYSCREEN);
        return new Rectangle(0, 0, width, height);
    }

    // ─── Windows API ───

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int maxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    public void Dispose()
    {
        if (!_disposed)
        {
            StopCapture();
            _disposed = true;
        }
    }
}

/// <summary>
/// A single captured screen frame.
/// </summary>
public class ScreenFrame
{
    public DateTimeOffset Timestamp { get; init; }
    public string ActiveWindowTitle { get; init; } = string.Empty;
    public string ActiveWindowProcess { get; init; } = string.Empty;
    public byte[]? ImageData { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }

    /// <summary>Text extracted via OCR (populated after capture).</summary>
    public string? ExtractedText { get; set; }

    /// <summary>UI state changes detected from this frame.</summary>
    public List<UiStateChange> StateChanges { get; set; } = new();
}

public class UiStateChange
{
    public string Type { get; init; } = string.Empty; // "new_window", "text_change", "notification"
    public string Description { get; init; } = string.Empty;
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
}
