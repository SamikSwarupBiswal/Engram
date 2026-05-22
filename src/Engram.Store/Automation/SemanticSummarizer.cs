using System;

namespace Engram.Store.Automation;

/// <summary>
/// Translates mechanical execution parameters (coordinates, selectors) into
/// clear, human-readable semantic summaries for user review.
/// </summary>
public class SemanticSummarizer
{
    /// <summary>
    /// Generates a human-friendly description of the automation action.
    /// </summary>
    public string Summarize(AutomationAction action)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));

        var prefix = string.IsNullOrWhiteSpace(action.Description) 
            ? string.Empty 
            : $"[{action.Description}] ";

        var summary = action.Type switch
        {
            ActionType.Navigate => $"Navigate to URL '{action.Value}'",
            
            ActionType.Click => SummarizeClick(action.Target),
            
            ActionType.Type => SummarizeType(action),
            
            ActionType.KeyPress => $"Press key '{action.Value}'",
            
            ActionType.Wait => $"Wait for {action.Value ?? "a condition"} ms",
            
            ActionType.Screenshot => "Capture a screenshot of the current viewport",
            
            ActionType.Scroll => $"Scroll page {action.Value ?? "down"}",
            
            ActionType.Select => $"Select option '{action.Value}' from {action.Target?.Selector ?? action.Target?.Text ?? "dropdown"}",
            
            ActionType.Upload => $"Upload file '{action.Value}'",
            
            ActionType.Download => $"Download file from '{action.Value}'",
            
            _ => $"Execute action '{action.Type}'"
        };

        return $"{prefix}{summary}";
    }

    private static string SummarizeClick(ActionTarget? target)
    {
        if (target == null) return "Click element";

        if (target.X.HasValue && target.Y.HasValue)
        {
            return $"Click screen coordinates ({target.X.Value}, {target.Y.Value})";
        }

        if (!string.IsNullOrEmpty(target.Text))
        {
            return $"Click element containing text '{target.Text}'";
        }

        if (!string.IsNullOrEmpty(target.Selector))
        {
            return $"Click element matching selector '{target.Selector}'";
        }

        return "Click element";
    }

    private static string SummarizeType(AutomationAction action)
    {
        var valueText = action.Value ?? string.Empty;
        var targetText = string.Empty;

        if (action.Target != null)
        {
            if (!string.IsNullOrEmpty(action.Target.Text))
            {
                targetText = $" into '{action.Target.Text}' field";
            }
            else if (!string.IsNullOrEmpty(action.Target.Selector))
            {
                targetText = $" into field matching selector '{action.Target.Selector}'";
            }
        }

        return $"Type '{valueText}'{targetText}";
    }
}
