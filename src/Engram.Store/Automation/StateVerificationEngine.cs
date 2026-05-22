using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Engram.Store.Automation;

/// <summary>
/// Verifies the actual environment state (reality check) after action execution.
/// Decouples verification logic from specific browser/desktop engines.
/// </summary>
public class StateVerificationEngine
{
    private readonly IUiEmbodimentProvider _uiProvider;

    public StateVerificationEngine(IUiEmbodimentProvider uiProvider)
    {
        _uiProvider = uiProvider ?? throw new ArgumentNullException(nameof(uiProvider));
    }

    /// <summary>
    /// Checks if a file exists on the local filesystem.
    /// </summary>
    public async Task<bool> VerifyFileExistsAsync(string filePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        return await Task.Run(() => File.Exists(filePath), ct);
    }

    /// <summary>
    /// Checks if the active desktop window matches process name and title expectations.
    /// </summary>
    public async Task<bool> VerifyActiveWindowAsync(string? expectedProcessName, string? expectedWindowTitlePattern = null, CancellationToken ct = default)
    {
        if (expectedProcessName == null && expectedWindowTitlePattern == null) return true;
        
        var (proc, title) = await _uiProvider.GetActiveWindowAsync(ct);
        
        if (expectedProcessName != null && !proc.Equals(expectedProcessName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (expectedWindowTitlePattern != null)
        {
            return Regex.IsMatch(title, expectedWindowTitlePattern, RegexOptions.IgnoreCase);
        }

        return true;
    }

    /// <summary>
    /// Checks if the current browser URL matches the pattern.
    /// </summary>
    public async Task<bool> VerifyUrlAsync(string urlPattern, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(urlPattern)) return false;
        var currentUrl = await _uiProvider.GetUrlAsync(ct);
        if (string.IsNullOrEmpty(currentUrl)) return false;

        return Regex.IsMatch(currentUrl, urlPattern, RegexOptions.IgnoreCase) || currentUrl.Contains(urlPattern, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if a DOM node exists in the browser using the embodiment provider.
    /// </summary>
    public async Task<bool> VerifyDomNodeExistsAsync(string selector, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(selector)) return false;

        try
        {
            var action = new AutomationAction
            {
                Type = ActionType.Wait,
                Target = new ActionTarget { Selector = selector },
                Value = "0" // Instant check
            };
            var result = await _uiProvider.ExecuteActionAsync(action, ct);
            return action.Status == ActionStatus.Completed && !result.Contains("fail", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
