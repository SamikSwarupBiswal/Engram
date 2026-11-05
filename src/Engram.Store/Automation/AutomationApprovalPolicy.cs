using System.Security.Cryptography;
using System.Text.Json;

namespace Engram.Store.Automation;

internal static class AutomationApprovalPolicy
{
    private static readonly HashSet<string> BoundLocalCapabilities = new(StringComparer.Ordinal)
    {
        "document.pdf.extract", "document.pdf.merge", "document.pdf.form.fill",
        "office.word.sequence.launch", "office.word.sequence.type", "office.word.sequence.verify",
        "office.excel.formula.create", "office.excel.formula.verify",
        "office.excel.chart.create", "office.excel.chart.verify",
        "office.excel.format.apply", "office.excel.format.verify",
        "office.excel.csv.clean", "office.excel.csv.verify",
        "office.excel.table.append-verified",
        "browser.page.navigate", "browser.page.extract", "browser.page.screenshot",
        "filesystem.organization.execute"
    };

    internal static void GrantAutomaticExecution(AutomationAction action)
    {
        if (action.Type == ActionType.Navigate && !CanAutomaticallyNavigate(action))
            throw new InvalidOperationException("This navigation requires explicit approval.");
        if (action.Type is ActionType.RunCommand or ActionType.DeleteFile or ActionType.Upload or ActionType.Download)
            throw new InvalidOperationException("This operation requires explicit approval.");
        if (!string.IsNullOrWhiteSpace(action.CapabilityId) && !BoundLocalCapabilities.Contains(action.CapabilityId))
            throw new InvalidOperationException("The tool has no automatic approval policy.");
        if (string.IsNullOrWhiteSpace(action.CapabilityId) && action.Type is not
            (ActionType.Calculate or ActionType.LaunchApplication or ActionType.FocusApplication or
             ActionType.Navigate or ActionType.Wait or ActionType.Screenshot or ActionType.RescanApplications or
             ActionType.FileWrite or ActionType.MoveFile or ActionType.Type or ActionType.KeyPress or ActionType.Select or ActionType.Scroll))
            throw new InvalidOperationException("This interaction requires a verified tool before automatic execution.");
        action.AutomaticApprovalFingerprint = Fingerprint(action);
    }

    internal static bool IsAutomatic(AutomationAction action) =>
        action.Permission != ActionPermission.Denied &&
        action.AutomaticApprovalFingerprint is { } approved &&
        string.Equals(approved, Fingerprint(action), StringComparison.Ordinal);

    internal static bool CanAutomaticallyNavigate(AutomationAction action) =>
        action.Type == ActionType.Navigate && Uri.TryCreate(action.Value, UriKind.Absolute, out var url) &&
        url.Scheme is "http" or "https" && string.IsNullOrEmpty(url.UserInfo) &&
        !ExternalImpactGate.HasPotentialExternalEffect(action) && !new ReversibilityEvaluator().IsIrreversible(action) &&
        (!action.CapabilityArguments.TryGetValue("url", out var boundUrl) ||
         string.Equals(boundUrl, action.Value, StringComparison.Ordinal));

    // Only code which has validated the operation and selected resources can
    // grant this scope. Model/API JSON cannot provide it, and substituted
    // arguments must continue to match before execution.
    private static string Fingerprint(AutomationAction action) => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            action.ActionId, action.Type, value = action.Value ?? string.Empty,
            target = new
            {
                selector = action.Target?.Selector ?? string.Empty, text = action.Target?.Text ?? string.Empty,
                action.Target?.X, action.Target?.Y, action.Target?.X2, action.Target?.Y2
            },
            capabilityId = action.CapabilityId ?? string.Empty,
            arguments = action.CapabilityArguments.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => new { key = pair.Key.ToLowerInvariant(), value = pair.Value })
        })));

    internal static bool IsBoundLocalCapability(string? capability) =>
        capability != null && BoundLocalCapabilities.Contains(capability);
}
