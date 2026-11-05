using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace Engram.Store.Automation.Applications;

internal static class PackagedWindowsApplication
{
    internal const string AppsFolder = "shell:AppsFolder";
    private const string LaunchPrefix = AppsFolder + @"\";
    private const int MaximumIdLength = 129;
    private static readonly Guid ActivationManagerClass = new("45BA127D-10A8-46EA-8AB7-56EA9078943C");
    private static readonly Regex IdPattern = new(
        @"\A[A-Za-z0-9.-]{3,50}_[0-9a-hjkmnp-tv-zA-HJKMNP-TV-Z]{13}!(?=[A-Za-z0-9.]{1,64}\z)[A-Za-z][A-Za-z0-9]*(?:\.[A-Za-z][A-Za-z0-9]*)*\z",
        RegexOptions.CultureInvariant);

    internal static bool TryGetId(string? launchTarget, out string id)
    {
        id = string.Empty;
        if (launchTarget?.StartsWith(LaunchPrefix, StringComparison.OrdinalIgnoreCase) != true)
            return false;

        var candidate = launchTarget[LaunchPrefix.Length..];
        if (!IsValidId(candidate)) return false;
        id = candidate;
        return true;
    }

    internal static string CreateLaunchTarget(string id) => LaunchPrefix + id;

    private static bool IsValidId(string id) => id.Length <= MaximumIdLength && IdPattern.IsMatch(id);

    [SupportedOSPlatform("windows")]
    internal static uint Activate(string id)
    {
        if (!IsValidId(id))
            throw new ArgumentException("A packaged Windows application requires a valid app identity.", nameof(id));

        var managerType = Type.GetTypeFromCLSID(ActivationManagerClass, throwOnError: true)!;
        var manager = (IApplicationActivationManager)Activator.CreateInstance(managerType)!;
        try
        {
            CoAllowSetForegroundWindow(manager, IntPtr.Zero);
            Marshal.ThrowExceptionForHR(manager.ActivateApplication(id, null, 0, out var processId));
            return processId != 0 ? processId : throw new InvalidOperationException("Windows returned no process for the packaged application.");
        }
        finally
        {
            Marshal.ReleaseComObject(manager);
        }
    }

    [SupportedOSPlatform("windows")]
    internal static bool ProcessMatchesId(uint processId, string id)
    {
        const uint processQueryLimitedInformation = 0x1000;
        if (processId == 0) return false;
        using var process = OpenProcess(processQueryLimitedInformation, false, processId);
        if (process.IsInvalid) return false;

        uint length = MaximumIdLength + 1;
        var actualId = new StringBuilder((int)length);
        return GetApplicationUserModelId(process, ref length, actualId) == 0 &&
            string.Equals(actualId.ToString(), id, StringComparison.OrdinalIgnoreCase);
    }

    [ComImport]
    [Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string? arguments,
            uint options,
            out uint processId);
    }

    [DllImport("ole32.dll")]
    private static extern int CoAllowSetForegroundWindow([MarshalAs(UnmanagedType.IUnknown)] object manager, IntPtr reserved);

    [DllImport("kernel32.dll")]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetApplicationUserModelId(SafeProcessHandle process, ref uint length, StringBuilder appUserModelId);
}
