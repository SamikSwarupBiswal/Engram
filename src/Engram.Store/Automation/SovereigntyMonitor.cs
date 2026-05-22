using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Engram.Store.Automation;

/// <summary>
/// Monitors native human keyboard/mouse inputs and asserts foreground sovereignty
/// to back off automated threads when the user actively interacts with the environment.
/// </summary>
public class SovereigntyMonitor
{
    private readonly int _backoffThresholdMs;

    public SovereigntyMonitor(int backoffThresholdMs = 2000)
    {
        _backoffThresholdMs = backoffThresholdMs;
    }

    /// <summary>
    /// Checks if the human user performed any input (keyboard/mouse) within the backoff threshold.
    /// </summary>
    public bool DetectUserActivity()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        try
        {
            var lastInput = GetLastInputTimeMs();
            var idleTime = Environment.TickCount - lastInput;
            return idleTime >= 0 && idleTime < _backoffThresholdMs;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Asserts foreground sovereignty. Throws InvalidOperationException if the human user is active.
    /// </summary>
    public void VerifySovereignty()
    {
        if (DetectUserActivity())
        {
            throw new InvalidOperationException("Execution halted: User activity detected. Foreground sovereignty reclaimed by human.");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    private static uint GetLastInputTimeMs()
    {
        var info = new LASTINPUTINFO();
        info.cbSize = (uint)Marshal.SizeOf(info);
        if (GetLastInputInfo(ref info))
        {
            return info.dwTime;
        }
        return 0;
    }
}
