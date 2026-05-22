using System;

namespace Engram.Store.Automation;

public enum TrustTier
{
    Observe,      // Read-only sensing (process listing, file verification, UI tree scanning)
    Suggest,      // Suggests actions, blocks all direct execution
    Assist,       // Low-risk, fully reversible actions (navigation, viewport scrolling, waiting)
    Operate,      // Moderate-risk workflows (clicking UI elements, typing inputs)
    Restricted,   // Blocks all dangerous actions (deletes, installations, external transfers)
    Privileged    // Allows high-risk critical operations after explicit user override/auth
}

/// <summary>
/// Manages the active TrustTier and validates whether actions are permitted under the current safety rules.
/// </summary>
public class TrustTierManager
{
    public TrustTier CurrentTier { get; set; } = TrustTier.Observe;

    public TrustTierManager(TrustTier initialTier = TrustTier.Observe)
    {
        CurrentTier = initialTier;
    }

    /// <summary>
    /// Checks if the action is allowed under the current trust tier.
    /// Throws InvalidOperationException if the action is blocked.
    /// </summary>
    public void ValidateAction(AutomationAction action)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));

        switch (CurrentTier)
        {
            case TrustTier.Suggest:
                throw new InvalidOperationException($"Action blocked: Trust tier is Suggest, which blocks all direct execution.");

            case TrustTier.Observe:
                if (!IsObserveAction(action.Type))
                {
                    throw new InvalidOperationException($"Action blocked: Trust tier is Observe, but action type is {action.Type}. Only read-only sensing is allowed.");
                }
                break;

            case TrustTier.Assist:
                if (!IsAssistAction(action.Type))
                {
                    throw new InvalidOperationException($"Action blocked: Trust tier is Assist, but action type is {action.Type}. Assist only allows low-risk navigation and viewport control.");
                }
                break;

            case TrustTier.Restricted:
                if (IsDangerousAction(action))
                {
                    throw new InvalidOperationException($"Action blocked: Trust tier is Restricted, which blocks dangerous action: '{action.Description}'.");
                }
                break;

            case TrustTier.Operate:
                if (IsPrivilegedAction(action))
                {
                    throw new InvalidOperationException($"Action blocked: Action requires Privileged tier, but current tier is {CurrentTier}.");
                }
                break;

            case TrustTier.Privileged:
                // All actions are allowed under Privileged tier
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(CurrentTier), $"Unknown trust tier: {CurrentTier}");
        }
    }

    private static bool IsObserveAction(ActionType type)
    {
        return type == ActionType.Screenshot || type == ActionType.Wait;
    }

    private static bool IsAssistAction(ActionType type)
    {
        return type == ActionType.Screenshot || 
               type == ActionType.Wait || 
               type == ActionType.Navigate || 
               type == ActionType.Scroll;
    }

    private static bool IsDangerousAction(AutomationAction action)
    {
        // Block destructive actions
        if (action.Type == ActionType.Upload || action.Type == ActionType.Download)
        {
            return true;
        }

        var description = action.Description ?? string.Empty;
        var value = action.Value ?? string.Empty;

        // Pattern matching for deletion or system modifications
        if (ContainsDestructiveKeyword(description) || ContainsDestructiveKeyword(value))
        {
            return true;
        }

        return false;
    }

    private static bool IsPrivilegedAction(AutomationAction action)
    {
        // High-risk/critical operations
        var description = action.Description ?? string.Empty;
        var value = action.Value ?? string.Empty;

        if (description.Contains("registry", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("system32", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("database", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("commit", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (value.Contains("registry", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("system32", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("database", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("commit", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool ContainsDestructiveKeyword(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        string[] keywords = { "delete", "remove", "rm ", "destroy", "format", "drop ", "purge", "uninstall" };
        foreach (var keyword in keywords)
        {
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
