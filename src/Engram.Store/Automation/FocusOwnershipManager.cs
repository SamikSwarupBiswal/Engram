using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Engram.Store.Automation;

public class FocusOwnershipManager
{
    private readonly IUiEmbodimentProvider _uiProvider;
    private string? _lockedProcess;

    public FocusOwnershipManager(IUiEmbodimentProvider uiProvider)
    {
        _uiProvider = uiProvider ?? throw new ArgumentNullException(nameof(uiProvider));
    }

    public async Task<bool> VerifyFocusAsync(string expectedProcess, string? expectedTitlePattern = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(expectedProcess)) return true;

        if (_lockedProcess != null && !_lockedProcess.Equals(expectedProcess, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var (proc, title) = await _uiProvider.GetActiveWindowAsync(ct);

        var cleanExpected = expectedProcess.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) 
            ? expectedProcess[..^4] 
            : expectedProcess;
        var cleanActual = proc.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) 
            ? proc[..^4] 
            : proc;

        if (!cleanActual.Equals(cleanExpected, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (expectedTitlePattern != null)
        {
            return Regex.IsMatch(title, expectedTitlePattern, RegexOptions.IgnoreCase);
        }

        return true;
    }

    public async Task<bool> DetectOverlayOrOcclusionAsync(CancellationToken ct = default)
    {
        var (proc, title) = await _uiProvider.GetActiveWindowAsync(ct);

        // Common overlay/occlusion indicators in Windows
        if (title.Contains("Notification Center", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("Windows Update", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("User Account Control", StringComparison.OrdinalIgnoreCase) ||
            proc.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase) ||
            proc.Equals("SearchHost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public void LockFocus(string processName)
    {
        _lockedProcess = processName;
    }

    public void ReleaseFocus()
    {
        _lockedProcess = null;
    }
}
