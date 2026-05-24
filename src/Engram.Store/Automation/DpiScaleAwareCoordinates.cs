using System;
using System.Runtime.InteropServices;

namespace Engram.Store.Automation;

/// <summary>
/// Handles logical-to-physical coordinate translation across multiple monitors
/// with varying fractional DPI scale factors and virtual coordinate bounds.
/// </summary>
public static class DpiScaleAwareCoordinates
{
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    private enum MonitorDpiType
    {
        Effective = 0,
        Angular = 1,
        Raw = 2
    }

    /// <summary>
    /// Gets the DPI scaling factor for the monitor where the window resides.
    /// Returns 1.0 if not on Windows or if query fails.
    /// </summary>
    public static double GetScaleFactorForWindow(IntPtr hwnd)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || hwnd == IntPtr.Zero)
            return 1.0;

        try
        {
            var hMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (hMonitor == IntPtr.Zero) return 1.0;

            if (GetDpiForMonitor(hMonitor, MonitorDpiType.Effective, out var dpiX, out _) == 0)
            {
                return dpiX / 96.0;
            }
        }
        catch
        {
            // Fallback
        }

        return 1.0;
    }

    /// <summary>
    /// Gets the DPI scaling factor for the monitor closest to the specified physical point.
    /// </summary>
    public static double GetScaleFactorForPoint(int x, int y)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return 1.0;

        try
        {
            var pt = new POINT { x = x, y = y };
            var hMonitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
            if (hMonitor == IntPtr.Zero) return 1.0;

            if (GetDpiForMonitor(hMonitor, MonitorDpiType.Effective, out var dpiX, out _) == 0)
            {
                return dpiX / 96.0;
            }
        }
        catch
        {
            // Fallback
        }

        return 1.0;
    }

    /// <summary>
    /// Translates logical coordinates (obtained via UIA or system checks)
    /// to the physical coordinates based on monitor DPI scaling.
    /// </summary>
    public static (int X, int Y) TranslateLogicalToPhysical(int logicalX, int logicalY, IntPtr hwnd)
    {
        var scale = GetScaleFactorForWindow(hwnd);
        return ((int)(logicalX * scale), (int)(logicalY * scale));
    }

    /// <summary>
    /// Resolves virtual screen bounds across all active monitors.
    /// </summary>
    public static (int Left, int Top, int Width, int Height) GetVirtualScreenBounds()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return (0, 0, 1920, 1080);

        try
        {
            var left = GetSystemMetrics(SM_XVIRTUALSCREEN);
            var top = GetSystemMetrics(SM_YVIRTUALSCREEN);
            var width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            var height = GetSystemMetrics(SM_CYVIRTUALSCREEN);

            if (width > 0 && height > 0)
            {
                return (left, top, width, height);
            }
        }
        catch
        {
            // Fallback
        }

        return (0, 0, 1920, 1080);
    }

    /// <summary>
    /// Maps absolute screen coordinates into standard absolute mouse coordinate space (0 to 65535)
    /// spanning the entire virtual multi-monitor desktop.
    /// </summary>
    public static (int Dx, int Dy) MapToAbsoluteCoordinates(int physicalX, int physicalY)
    {
        var bounds = GetVirtualScreenBounds();
        
        // Offset coords by virtual screen start point
        var relativeX = physicalX - bounds.Left;
        var relativeY = physicalY - bounds.Top;

        // Map to 0-65535 range
        var dx = (relativeX * 65536) / bounds.Width;
        var dy = (relativeY * 65536) / bounds.Height;

        return (dx, dy);
    }
}
