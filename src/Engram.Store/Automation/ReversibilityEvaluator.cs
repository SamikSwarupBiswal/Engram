using System;

namespace Engram.Store.Automation;

public enum ReversibilityScore
{
    Reversible,   // Action has no state-changing side effects (e.g. screenshot, wait, navigation, scrolling)
    Mostly,       // State changes are easily undone (e.g. typing text which can be cleared, hover, focus)
    Maybe,        // Might affect local state (e.g. saving a draft, window resize)
    Partially,    // Difficult to undo or has external side effects (e.g. API POST requests, submitting forms)
    No            // Irreversible actions (e.g. file deletes, database drops, email/message sends, purchases)
}

/// <summary>
/// Evaluates and assigns reversibility scores to automation actions.
/// Ensures that irreversible actions are flagged and bypass auto-approval rules.
/// </summary>
public class ReversibilityEvaluator
{
    /// <summary>
    /// Evaluates the reversibility of the specified automation action.
    /// </summary>
    public ReversibilityScore Evaluate(AutomationAction action)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));

        // Block downloads/uploads or destructive actions into dangerous or irreversible categories
        if (action.Type == ActionType.Upload || action.Type == ActionType.Download)
        {
            return ReversibilityScore.Partially;
        }

        var description = action.Description ?? string.Empty;
        var value = action.Value ?? string.Empty;

        // Check for highly irreversible/destructive keywords
        if (ContainsIrreversibleKeyword(description) || ContainsIrreversibleKeyword(value))
        {
            return ReversibilityScore.No;
        }

        return action.Type switch
        {
            ActionType.Wait or ActionType.Screenshot or ActionType.Scroll or ActionType.Navigate => ReversibilityScore.Reversible,
            ActionType.Type or ActionType.KeyPress or ActionType.Select => ReversibilityScore.Mostly,
            ActionType.Click => EvaluateClickReversibility(description, value),
            _ => ReversibilityScore.Maybe
        };
    }

    /// <summary>
    /// Helper to identify if an action is completely irreversible.
    /// </summary>
    public bool IsIrreversible(AutomationAction action)
    {
        return Evaluate(action) == ReversibilityScore.No;
    }

    private static ReversibilityScore EvaluateClickReversibility(string description, string value)
    {
        // Clicks can be safe or dangerous depending on what they target.
        if (ContainsHighRiskKeyword(description) || ContainsHighRiskKeyword(value))
        {
            return ReversibilityScore.Partially;
        }

        return ReversibilityScore.Maybe;
    }

    private static bool ContainsIrreversibleKeyword(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        string[] irreversibleKeywords = 
        { 
            "delete", "remove", "rm ", "destroy", "format", "drop ", "purge", 
            "uninstall", "send", "submit", "publish", "purchase", "buy", "pay", 
            "terminate", "kill " 
        };

        foreach (var keyword in irreversibleKeywords)
        {
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool ContainsHighRiskKeyword(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        string[] highRiskKeywords = 
        { 
            "confirm", "apply", "update", "save", "edit", "write", "install", 
            "enable", "disable", "start", "stop" 
        };

        foreach (var keyword in highRiskKeywords)
        {
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
