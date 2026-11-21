namespace Engram.Store.Automation.Plugins;

public sealed record AutomationPluginGrant(
    string PluginId,
    string CapabilityId,
    IReadOnlySet<AutomationPluginPermission> Permissions,
    IReadOnlyList<string>? AllowedOrigins = null);

public static class AutomationPluginGrantEvaluator
{
    public static void Demand(
        AutomationPluginGrant grant,
        AutomationPluginCapability capability,
        AutomationAction action)
    {
        var missing = capability.RequiredPermissions.Where(permission => !grant.Permissions.Contains(permission)).ToArray();
        if (missing.Length > 0)
            throw new AutomationPluginAuthorizationException(
                $"Capability '{capability.Id}' has no Engram grant for: {string.Join(", ", missing)}.");

        if (grant.Permissions.Contains(AutomationPluginPermission.Network) &&
            action.CapabilityArguments.TryGetValue("url", out var url))
            DemandOrigin(grant, url);

        if (grant.Permissions.Contains(AutomationPluginPermission.LocalFileRead))
            DemandApprovedAbsolutePath(action, "path", "local file read");
        if (grant.Permissions.Contains(AutomationPluginPermission.LocalFileWrite) &&
            action.CapabilityArguments.ContainsKey("destination"))
            DemandApprovedAbsolutePath(action, "destination", "local file write");
        else if (grant.Permissions.Contains(AutomationPluginPermission.LocalFileWrite) &&
                 !capability.RequiredPermissions.Contains(AutomationPluginPermission.OfficeWrite))
            throw new AutomationPluginAuthorizationException(
                $"Capability '{capability.Id}' has no action-scoped destination for local file write.");

        if (grant.Permissions.Any(permission => permission is
                AutomationPluginPermission.OfficeRead or AutomationPluginPermission.OfficeWrite or
                AutomationPluginPermission.DesktopLaunch))
        {
            if (!action.CapabilityArguments.TryGetValue("bindingId", out var bindingId) ||
                bindingId.Length != 32 || bindingId.Any(character => !Uri.IsHexDigit(character)))
                throw new AutomationPluginAuthorizationException(
                    $"Capability '{capability.Id}' requires one GUID-scoped Office ownership binding.");
        }
    }

    private static void DemandOrigin(AutomationPluginGrant grant, string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0)
            throw new AutomationPluginAuthorizationException("Network grant requires an absolute HTTP(S) URL without credentials.");
        var origin = uri.GetLeftPart(UriPartial.Authority);
        var allowed = grant.AllowedOrigins?.Any(pattern => MatchesOrigin(pattern, origin)) == true;
        if (!allowed)
            throw new AutomationPluginAuthorizationException(
                $"Origin '{origin}' is outside the core grant for capability '{grant.CapabilityId}'.");
    }

    private static bool MatchesOrigin(string pattern, string origin)
    {
        if (pattern.Equals("https://*", StringComparison.OrdinalIgnoreCase))
            return origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        if (pattern.EndsWith(":*", StringComparison.Ordinal))
        {
            var hostOrigin = new UriBuilder(origin) { Port = -1 }.Uri.GetLeftPart(UriPartial.Authority);
            return hostOrigin.Equals(pattern[..^2], StringComparison.OrdinalIgnoreCase);
        }
        return origin.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }

    private static void DemandApprovedAbsolutePath(AutomationAction action, string argument, string operation)
    {
        if (action.Permission != ActionPermission.Approved &&
            !(action.Permission == ActionPermission.AutoApproved && AutomationApprovalPolicy.IsAutomatic(action)))
            throw new AutomationPluginAuthorizationException(
                $"Plugin {operation} requires explicit human approval for the exact path.");
        if (!action.CapabilityArguments.TryGetValue(argument, out var value) || !Path.IsPathFullyQualified(value))
            throw new AutomationPluginAuthorizationException(
                $"Plugin {operation} requires an absolute, action-scoped path.");
    }
}
