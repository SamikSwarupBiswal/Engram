using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Engram.Store.Automation;

/// <summary>
/// Enforces overlay safety laws and modal classification.
/// Unknown and forbidden modals default to suspending execution.
/// </summary>
public class EnvironmentalInterruptGraph
{
    private static readonly HashSet<string> ForbiddenProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "consent",             // UAC prompt
        "CredentialUIBroker",  // Windows Credential prompt
        "pinflow",             // Windows Hello PIN
    };

    public static readonly List<string> ForbiddenKeywords = new()
    {
        "security", "credential", "password", "login", "delete", "overwrite", 
        "confirm deletion", "permission", "update", "purchase", "checkout", 
        "agreement", "license", "uac", "authoriz", "signin", "admin"
    };

    private static readonly List<string> AutoDismissableKeywords = new()
    {
        "notification", "tips", "success", "saved successfully", "dismiss", "info"
    };

    private readonly Dictionary<string, Func<ExecutionContext, CancellationToken, Task<bool>>> _customHandlers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Register a custom recovery handler for a specific safe modal pattern.
    /// </summary>
    public void RegisterSafeInterruptHandler(string windowTitlePattern, Func<ExecutionContext, CancellationToken, Task<bool>> handler)
    {
        _customHandlers[windowTitlePattern] = handler;
    }

    /// <summary>
    /// Evaluates if an overlay/modal is safe or forbidden.
    /// Returns true if it was safely resolved autonomously, false if it requires human intervention (suspend/yield).
    /// </summary>
    public async Task<bool> AssessAndHandleInterruptAsync(
        string activeProcess, 
        string activeTitle, 
        ExecutionContext context, 
        CancellationToken ct)
    {
        // 1. Check if the process is forbidden
        if (ForbiddenProcessNames.Contains(activeProcess))
        {
            return false; // Forbidden (Human Required)
        }

        // 2. Check for forbidden keywords in the window title
        if (ForbiddenKeywords.Any(k => activeTitle.Contains(k, StringComparison.OrdinalIgnoreCase)))
        {
            return false; // Forbidden (Human Required)
        }

        // 3. Check if there is a registered safe handler matching the title
        foreach (var pair in _customHandlers)
        {
            if (activeTitle.Contains(pair.Key, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    return await pair.Value(context, ct);
                }
                catch
                {
                    return false; // Recovery failed, yield to human
                }
            }
        }

        // 4. Check if it matches safe auto-dismissable keywords (fallback handler)
        if (AutoDismissableKeywords.Any(k => activeTitle.Contains(k, StringComparison.OrdinalIgnoreCase)))
        {
            // Informational toast/modal can be bypassed or auto-dismissed by waiting it out
            // We simulate a successful auto-dismissal
            await Task.Delay(100, ct);
            return true; 
        }

        // 5. Unknown Modals default to false (human intervention required)
        return false;
    }
}
