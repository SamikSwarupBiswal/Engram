using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Engram.Store.Automation;

/// <summary>
/// Manages safety boundaries, process and URL blacklists, rate limits, and physical mouse override detection.
/// </summary>
public class ExecutionSafetyManager
{
    private readonly HashSet<string> _blacklistedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd", "powershell", "regedit", "bash", "wsl", "cmd.exe", "powershell.exe", "regedit.exe"
    };

    private readonly List<string> _blacklistedUrlPatterns = new()
    {
        @"^https?://169\.254\.169\.254", // Cloud metadata
        @"localhost",
        @"127\.0\.0\.1",
        @"delete-account",
        @"reset-database"
    };

    private readonly int _maxActionsPerMinute;
    private readonly List<DateTimeOffset> _actionTimestamps = new();
    
    private bool _mouseFailsafeInitialized = false;
    private Win32Point _expectedMousePosition;
    private const int MouseFailsafeThreshold = 50; // pixels

    public bool IsSimulationMode { get; set; } = true;

    // Custom mouse position for testing and simulation
    private Win32Point _simulatedMousePosition;
    public Win32Point SimulatedMousePosition
    {
        get => _simulatedMousePosition;
        set
        {
            _simulatedMousePosition = value;
            if (IsSimulationMode)
            {
                // In simulation, we update expected too so we don't trip ourselves
                if (!_mouseFailsafeInitialized)
                {
                    _expectedMousePosition = value;
                    _mouseFailsafeInitialized = true;
                }
            }
        }
    }

    public ExecutionSafetyManager(
        IEnumerable<string>? customBlacklistedProcesses = null,
        IEnumerable<string>? customBlacklistedUrls = null,
        int maxActionsPerMinute = 60)
    {
        if (customBlacklistedProcesses != null)
        {
            foreach (var proc in customBlacklistedProcesses)
            {
                _blacklistedProcesses.Add(proc);
            }
        }

        if (customBlacklistedUrls != null)
        {
            _blacklistedUrlPatterns.AddRange(customBlacklistedUrls);
        }

        _maxActionsPerMinute = maxActionsPerMinute;
    }

    public void VerifyCoordinateBounds(int x, int y, int screenWidth = 1920, int screenHeight = 1080)
    {
        if (x < 0 || y < 0 || x > screenWidth || y > screenHeight)
        {
            throw new InvalidOperationException($"Safety Violation: Coordinates ({x}, {y}) are outside allowed screen boundaries ({screenWidth}x{screenHeight}).");
        }
    }

    public void VerifyProcessSafety(string processName, string windowTitle)
    {
        if (string.IsNullOrWhiteSpace(processName)) return;

        var cleanProc = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) 
            ? processName[..^4] 
            : processName;

        if (_blacklistedProcesses.Contains(cleanProc) || _blacklistedProcesses.Contains(processName))
        {
            throw new InvalidOperationException($"Safety Violation: Attempted to interact with blacklisted process '{processName}'.");
        }

        if (windowTitle.Contains("Administrator:", StringComparison.OrdinalIgnoreCase) ||
            windowTitle.Contains("Command Prompt", StringComparison.OrdinalIgnoreCase) ||
            windowTitle.Contains("Windows PowerShell", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Safety Violation: Active window '{windowTitle}' is a privileged or blacklisted application.");
        }
    }

    public void VerifyUrlSafety(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        foreach (var pattern in _blacklistedUrlPatterns)
        {
            if (Regex.IsMatch(url, pattern, RegexOptions.IgnoreCase))
            {
                throw new InvalidOperationException($"Safety Violation: URL '{url}' matches blacklisted pattern/substring '{pattern}'.");
            }
        }
    }

    public void VerifyRateLimit()
    {
        lock (_actionTimestamps)
        {
            var now = DateTimeOffset.UtcNow;
            _actionTimestamps.RemoveAll(t => (now - t).TotalMinutes > 1);

            if (_actionTimestamps.Count >= _maxActionsPerMinute)
            {
                throw new InvalidOperationException($"Safety Violation: Rate limit exceeded. Maximum of {_maxActionsPerMinute} actions per minute allowed.");
            }

            _actionTimestamps.Add(now);
        }
    }

    public void InitializeMouseFailsafe()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !IsSimulationMode)
        {
            if (GetCursorPos(out var pt))
            {
                _expectedMousePosition = pt;
                _mouseFailsafeInitialized = true;
            }
        }
        else
        {
            _expectedMousePosition = SimulatedMousePosition;
            _mouseFailsafeInitialized = true;
        }
    }

    public void UpdateExpectedMousePosition(int x, int y)
    {
        _expectedMousePosition = new Win32Point { X = x, Y = y };
        if (IsSimulationMode)
        {
            _simulatedMousePosition = _expectedMousePosition;
        }
        _mouseFailsafeInitialized = true;
    }

    public void VerifyMouseFailsafe()
    {
        if (!_mouseFailsafeInitialized)
        {
            InitializeMouseFailsafe();
            return;
        }

        Win32Point currentPos;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !IsSimulationMode)
        {
            if (!GetCursorPos(out currentPos))
            {
                return; // Can't read, skip check
            }
        }
        else
        {
            currentPos = SimulatedMousePosition;
        }

        var dx = currentPos.X - _expectedMousePosition.X;
        var dy = currentPos.Y - _expectedMousePosition.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);

        if (distance > MouseFailsafeThreshold)
        {
            throw new InvalidOperationException($"Safety Violation: Physical mouse movement override detected. Distance: {distance:F1}px (Threshold: {MouseFailsafeThreshold}px). Execution aborted.");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Win32Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Win32Point lpPoint);
}
