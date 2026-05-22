using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Automation;

public class DesktopOperator : IDesktopOperator
{
    private readonly ILogger<DesktopOperator>? _logger;
    private bool _isSimulationMode = true; // Default to simulation mode for safety

    public bool IsSimulationMode
    {
        get => _isSimulationMode || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        set
        {
            if (value == false && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _logger?.LogWarning("Cannot disable simulation mode: not running on Windows.");
                _isSimulationMode = true;
            }
            else
            {
                _isSimulationMode = value;
            }
        }
    }

    public DesktopOperator(ILogger<DesktopOperator>? logger = null)
    {
        _logger = logger;
        _isSimulationMode = true; // Safe by default, requires explicit activation
    }

    public async Task ClickAsync(int x, int y, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _logger?.LogInformation("Click requested at ({X}, {Y}). Simulation={Sim}", x, y, IsSimulationMode);

        // Bounds check
        var (screenWidth, screenHeight) = GetScreenResolution();
        if (x < 0 || y < 0 || x > screenWidth || y > screenHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"Coordinates ({x}, {y}) are outside screen boundaries (0, 0) to ({screenWidth}, {screenHeight})");
        }

        if (IsSimulationMode)
        {
            await Task.Delay(100, ct); // Simulate action latency
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            SendWindowsMouseClick(x, y, screenWidth, screenHeight);
        }
    }

    public async Task TypeAsync(string text, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(text)) return;

        _logger?.LogInformation("Type requested for: '{Text}'. Simulation={Sim}", text, IsSimulationMode);

        if (IsSimulationMode)
        {
            await Task.Delay(text.Length * 20, ct); // Simulate typing latency
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            SendWindowsUnicodeText(text);
        }
    }

    public async Task KeyPressAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(key)) return;

        _logger?.LogInformation("KeyPress requested for: '{Key}'. Simulation={Sim}", key, IsSimulationMode);

        if (IsSimulationMode)
        {
            await Task.Delay(50, ct);
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            SendWindowsKeyPress(key);
        }
    }

    public Task<(string ProcessName, string WindowTitle)> GetActiveWindowAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var title = GetActiveWindowTitle();
            var process = GetActiveWindowProcess();
            return Task.FromResult((process, title));
        }

        return Task.FromResult(("stub_process", "Stub Active Window"));
    }

    private (int Width, int Height) GetScreenResolution()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                var w = GetSystemMetrics(SM_CXSCREEN);
                var h = GetSystemMetrics(SM_CYSCREEN);
                if (w > 0 && h > 0) return (w, h);
            }
            catch
            {
                // Fall through
            }
        }
        return (1920, 1080); // Default fallback resolution
    }

    // ─── Windows Native Implementation ───

    [SupportedOSPlatform("windows")]
    private void SendWindowsMouseClick(int x, int y, int screenWidth, int screenHeight)
    {
        // Convert to absolute mouse coordinates (0 to 65535)
        var dx = (x * 65536) / screenWidth;
        var dy = (y * 65536) / screenHeight;

        var inputs = new INPUT[3];

        // Move to absolute coordinates
        inputs[0] = new INPUT
        {
            type = INPUT_MOUSE,
            u = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = dx,
                    dy = dy,
                    dwFlags = MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_MOVE,
                    time = 0,
                    dwExtraInfo = UIntPtr.Zero
                }
            }
        };

        // Left button down
        inputs[1] = new INPUT
        {
            type = INPUT_MOUSE,
            u = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = dx,
                    dy = dy,
                    dwFlags = MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_LEFTDOWN,
                    time = 0,
                    dwExtraInfo = UIntPtr.Zero
                }
            }
        };

        // Left button up
        inputs[2] = new INPUT
        {
            type = INPUT_MOUSE,
            u = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = dx,
                    dy = dy,
                    dwFlags = MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_LEFTUP,
                    time = 0,
                    dwExtraInfo = UIntPtr.Zero
                }
            }
        };

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
    }

    [SupportedOSPlatform("windows")]
    private void SendWindowsUnicodeText(string text)
    {
        var inputs = new List<INPUT>();

        foreach (var ch in text)
        {
            // Character key down
            inputs.Add(new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = ch,
                        dwFlags = KEYEVENTF_UNICODE,
                        time = 0,
                        dwExtraInfo = UIntPtr.Zero
                    }
                }
            });

            // Character key up
            inputs.Add(new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = ch,
                        dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP,
                        time = 0,
                        dwExtraInfo = UIntPtr.Zero
                    }
                }
            });
        }

        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(INPUT)));
    }

    [SupportedOSPlatform("windows")]
    private void SendWindowsKeyPress(string key)
    {
        var vk = MapKeyToVk(key);
        if (vk == 0)
        {
            _logger?.LogWarning("Unsupported key input: '{Key}'", key);
            return;
        }

        var inputs = new INPUT[2];

        // Key down
        inputs[0] = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = 0,
                    time = 0,
                    dwExtraInfo = UIntPtr.Zero
                }
            }
        };

        // Key up
        inputs[1] = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = KEYEVENTF_KEYUP,
                    time = 0,
                    dwExtraInfo = UIntPtr.Zero
                }
            }
        };

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
    }

    private static ushort MapKeyToVk(string key)
    {
        return key.ToLowerInvariant() switch
        {
            "enter" or "return" => 0x0D,
            "escape" or "esc" => 0x1B,
            "tab" => 0x09,
            "backspace" or "back" => 0x08,
            "space" => 0x20,
            "delete" or "del" => 0x2E,
            "insert" or "ins" => 0x2D,
            "home" => 0x24,
            "end" => 0x23,
            "pageup" or "pgup" => 0x21,
            "pagedown" or "pgdn" => 0x22,
            "up" => 0x26,
            "down" => 0x28,
            "left" => 0x25,
            "right" => 0x27,
            _ => 0
        };
    }

    // ─── P/Invoke Structures & Functions ───

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;

    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder sb, int maxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private static string GetActiveWindowTitle()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return string.Empty;

            var length = GetWindowTextLength(hwnd);
            if (length == 0) return string.Empty;

            var sb = new System.Text.StringBuilder(length + 1);
            GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetActiveWindowProcess()
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
}
