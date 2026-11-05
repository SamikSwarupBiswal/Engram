using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Engram.Store.Automation;

public class ExternalImpactGate
{
    private static readonly Regex ImpactRegex = new(
        @"\b(send|submit|publish|post|email|mail|transfer|delete|destroy|remove|pay|buy|purchase|checkout|bank|financial|reset|wipe|erase)\b", 
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    public async Task<bool> ValidateActionSafetyAsync(AutomationAction action, CancellationToken ct = default)
    {
        if (action == null) return false;
        if (action.AutomaticApprovalFingerprint != null && !AutomationApprovalPolicy.IsAutomatic(action))
            return false;

        // 1. Explicitly check type for Upload
        if (action.Type == ActionType.Upload)
        {
            return false; // Force approval for file uploads
        }

        if (AutomationApprovalPolicy.IsAutomatic(action))
            return true;

        return !HasPotentialExternalEffect(action);
    }

    internal static bool HasPotentialExternalEffect(AutomationAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (action.Type == ActionType.Upload) return true;
        if (new[] { action.Value, action.Description, action.Target?.Selector, action.Target?.Text }
            .Any(value => !string.IsNullOrEmpty(value) && ImpactRegex.IsMatch(value))) return true;
        if (action.Type != ActionType.Navigate || action.Value == null) return false;
        var decoded = action.Value;
        for (var depth = 0; depth < 3; depth++)
        {
            var next = Uri.UnescapeDataString(decoded);
            if (next == decoded) return false;
            if (ImpactRegex.IsMatch(next)) return true;
            decoded = next;
        }
        // Deeper nested escaping is ambiguous; do not automatically navigate it.
        return Uri.UnescapeDataString(decoded) != decoded;
    }
}
