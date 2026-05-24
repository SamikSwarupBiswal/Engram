using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Engram.Store.Events;

public sealed class PowerBroadcastListener : IDisposable
{
    public event Action? SystemSuspending;
    public event Action? SystemResuming;

    private readonly Thread? _listenerThread;
    private readonly CancellationTokenSource _cts = new();
    private IntPtr _hwnd = IntPtr.Zero;
    private bool _isDisposed;

    private const int WM_POWERBROADCAST = 0x0218;
    private const int PBT_APMSUSPEND = 0x0004;
    private const int PBT_APMRESUMESUSPEND = 0x0007;

    private const uint WS_POPUP = 0x80000000;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    // WndProc delegate field to prevent GC collection of delegate
    private WndProc? _wndProcDelegate;

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASS
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClass(ref WNDCLASS lpWndClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern sbyte GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_CLOSE = 0x0010;

    public PowerBroadcastListener()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _listenerThread = new Thread(RunMessageLoop)
            {
                IsBackground = true,
                Name = "EngramPowerBroadcastListener"
            };
            _listenerThread.Start();
        }
    }

    private void RunMessageLoop()
    {
        try
        {
            var className = "EngramPowerListenerClass_" + Guid.NewGuid().ToString("N");
            _wndProcDelegate = CustomWndProc;

            var wndClass = new WNDCLASS
            {
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
                lpszClassName = className,
                hInstance = Marshal.GetHINSTANCE(typeof(PowerBroadcastListener).Module)
            };

            var regResult = RegisterClass(ref wndClass);
            if (regResult == 0)
            {
                return;
            }

            _hwnd = CreateWindowEx(
                0,
                className,
                "EngramPowerListenerWindow",
                WS_POPUP,
                0, 0, 0, 0,
                HWND_MESSAGE, // Message-only window
                IntPtr.Zero,
                wndClass.hInstance,
                IntPtr.Zero);

            if (_hwnd == IntPtr.Zero)
            {
                return;
            }

            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (_cts.Token.IsCancellationRequested)
                {
                    break;
                }
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        catch
        {
            // Thread exiting
        }
    }

    private IntPtr CustomWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_POWERBROADCAST)
        {
            var eventType = (int)wParam;
            if (eventType == PBT_APMSUSPEND)
            {
                SystemSuspending?.Invoke();
            }
            else if (eventType == PBT_APMRESUMESUSPEND)
            {
                SystemResuming?.Invoke();
            }
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _cts.Cancel();

        if (_hwnd != IntPtr.Zero)
        {
            PostMessage(_hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            _hwnd = IntPtr.Zero;
        }

        if (_listenerThread != null && _listenerThread.IsAlive)
        {
            _listenerThread.Join(500);
        }

        _cts.Dispose();
    }
}
