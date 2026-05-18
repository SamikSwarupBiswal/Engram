using System.Drawing;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Perception;

/// <summary>
/// Windows Snap Layout integration.
/// Arranges windows on screen for side-by-side viewing.
/// Used by the research agent to snap source articles next to the summary.
/// </summary>
public class LayoutSnapService
{
    private readonly ILogger<LayoutSnapService>? _logger;

    public LayoutSnapService(ILogger<LayoutSnapService>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Snap a window to the left half of the screen.
    /// </summary>
    public bool SnapLeft(IntPtr windowHandle)
    {
        var screen = GetScreenBounds();
        var halfWidth = screen.Width / 2;
        return MoveWindow(windowHandle, screen.X, screen.Y, halfWidth, screen.Height, true);
    }

    /// <summary>
    /// Snap a window to the right half of the screen.
    /// </summary>
    public bool SnapRight(IntPtr windowHandle)
    {
        var screen = GetScreenBounds();
        var halfWidth = screen.Width / 2;
        return MoveWindow(windowHandle, screen.X + halfWidth, screen.Y, halfWidth, screen.Height, true);
    }

    /// <summary>
    /// Snap a window to a specific quadrant.
    /// </summary>
    public bool SnapToQuadrant(IntPtr windowHandle, Quadrant quadrant)
    {
        var screen = GetScreenBounds();
        var halfWidth = screen.Width / 2;
        var halfHeight = screen.Height / 2;

        return quadrant switch
        {
            Quadrant.TopLeft => MoveWindow(windowHandle, screen.X, screen.Y, halfWidth, halfHeight, true),
            Quadrant.TopRight => MoveWindow(windowHandle, screen.X + halfWidth, screen.Y, halfWidth, halfHeight, true),
            Quadrant.BottomLeft => MoveWindow(windowHandle, screen.X, screen.Y + halfHeight, halfWidth, halfHeight, true),
            Quadrant.BottomRight => MoveWindow(windowHandle, screen.X + halfWidth, screen.Y + halfHeight, halfWidth, halfHeight, true),
            _ => false
        };
    }

    /// <summary>
    /// Snap a window to a custom position.
    /// </summary>
    public bool SnapToPosition(IntPtr windowHandle, int x, int y, int width, int height)
    {
        return MoveWindow(windowHandle, x, y, width, height, true);
    }

    /// <summary>
    /// Maximize a window.
    /// </summary>
    public bool Maximize(IntPtr windowHandle)
    {
        ShowWindow(windowHandle, SW_MAXIMIZE);
        return true;
    }

    /// <summary>
    /// Restore a window to normal size.
    /// </summary>
    public bool Restore(IntPtr windowHandle)
    {
        ShowWindow(windowHandle, SW_RESTORE);
        return true;
    }

    /// <summary>
    /// Get the handle of a window by process name.
    /// </summary>
    public IntPtr FindWindowByProcess(string processName)
    {
        try
        {
            var processes = Process.GetProcessesByName(processName);
            foreach (var proc in processes)
            {
                if (proc.MainWindowHandle != IntPtr.Zero)
                    return proc.MainWindowHandle;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to find window for process {Process}", processName);
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// Get the handle of the foreground window.
    /// </summary>
    public IntPtr GetForegroundWindow()
    {
        return GetForegroundWindowNative();
    }

    /// <summary>
    /// Snap research layout: browser left, wiki right.
    /// Used by the research agent after collecting sources.
    /// </summary>
    public bool SnapResearchLayout(string browserProcess = "msedge", string editorProcess = "code")
    {
        var browserHandle = FindWindowByProcess(browserProcess);
        var editorHandle = FindWindowByProcess(editorProcess);

        if (browserHandle == IntPtr.Zero && editorHandle == IntPtr.Zero)
        {
            _logger?.LogWarning("No windows found for research layout");
            return false;
        }

        var snapped = false;

        if (browserHandle != IntPtr.Zero)
        {
            snapped |= SnapLeft(browserHandle);
            _logger?.LogInformation("Browser snapped left");
        }

        if (editorHandle != IntPtr.Zero)
        {
            snapped |= SnapRight(editorHandle);
            _logger?.LogInformation("Editor snapped right");
        }

        return snapped;
    }

    /// <summary>
    /// Snap source articles in a grid layout.
    /// Opens each URL in the browser and arranges windows.
    /// </summary>
    public bool SnapSourceGrid(List<string> urls, int columns = 2)
    {
        if (urls.Count == 0) return false;

        var screen = GetScreenBounds();
        var rows = (int)Math.Ceiling((double)urls.Count / columns);
        var cellWidth = screen.Width / columns;
        var cellHeight = screen.Height / rows;

        // This is a placeholder — real implementation would:
        // 1. Open each URL in a new browser tab
        // 2. Get the window handle for each tab
        // 3. Snap each to its grid position

        _logger?.LogInformation("Source grid: {Urls} urls in {Cols}x{Rows}", urls.Count, columns, rows);
        return true;
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
    private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindowNative();

    private const int SW_MAXIMIZE = 3;
    private const int SW_RESTORE = 9;
}

public enum Quadrant
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}
