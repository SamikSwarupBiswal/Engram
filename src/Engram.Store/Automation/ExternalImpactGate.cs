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

        // 1. Explicitly check type for Upload
        if (action.Type == ActionType.Upload)
        {
            return false; // Force approval for file uploads
        }

        // 2. Check value and description for impactful terms
        if (!string.IsNullOrEmpty(action.Value) && ImpactRegex.IsMatch(action.Value))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(action.Description) && ImpactRegex.IsMatch(action.Description))
        {
            return false;
        }

        if (action.Target != null)
        {
            if (!string.IsNullOrEmpty(action.Target.Selector) && ImpactRegex.IsMatch(action.Target.Selector))
            {
                return false;
            }
            if (!string.IsNullOrEmpty(action.Target.Text) && ImpactRegex.IsMatch(action.Target.Text))
            {
                return false;
            }
        }

        return true;
    }
}
