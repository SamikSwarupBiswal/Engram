using System;
using System.IO;

namespace Engram.Store.Automation;

/// <summary>
/// Computes fine-grained, context-aware capability fingerprints for automation actions
/// based on action type, scope boundaries, danger, and reversibility.
/// </summary>
public static class CapabilityFingerprint
{
    public static string Compute(AutomationAction action)
    {
        string typeStr = action.Type.ToString();
        string targetScope = "system";

        if (action.Type == ActionType.Navigate && !string.IsNullOrEmpty(action.Value))
        {
            try
            {
                var uri = new Uri(action.Value);
                targetScope = uri.Host;
            }
            catch
            {
                targetScope = "invalid_url";
            }
        }
        else if (action.Target != null)
        {
            if (!string.IsNullOrEmpty(action.Target.Selector))
            {
                // Extract base domain from selector or keep as selector type
                targetScope = "browser_selector";
            }
            else if (action.Target.X.HasValue && action.Target.Y.HasValue)
            {
                targetScope = "coordinate_click";
            }
        }

        // Check if value is a path for file actions
        if (!string.IsNullOrEmpty(action.Value) && 
            (action.Value.Contains(":\\") || action.Value.Contains("/") || action.Description.Contains("file", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var path = Path.GetDirectoryName(action.Value);
                if (!string.IsNullOrEmpty(path))
                {
                    targetScope = path;
                }
            }
            catch
            {
                // Fallback
            }
        }

        string danger = GetDangerTier(action);
        string reversibility = GetReversibilityTier(action);

        return $"{typeStr}:{targetScope}:{danger}:{reversibility}";
    }

    private static string GetDangerTier(AutomationAction action)
    {
        var desc = action.Description.ToLowerInvariant();
        if (desc.Contains("delete") || desc.Contains("remove") || desc.Contains("purge") || desc.Contains("format"))
        {
            return "High";
        }
        if (action.Type == ActionType.Click || action.Type == ActionType.Type)
        {
            return "Medium";
        }
        return "Low";
    }

    private static string GetReversibilityTier(AutomationAction action)
    {
        var desc = action.Description.ToLowerInvariant();
        if (desc.Contains("delete") || desc.Contains("remove") || desc.Contains("purge"))
        {
            return "Irreversible";
        }
        return "Reversible";
    }
}
