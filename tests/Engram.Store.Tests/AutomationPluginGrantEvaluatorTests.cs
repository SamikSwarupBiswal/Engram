using Engram.Store.Automation;
using Engram.Store.Automation.Plugins;

namespace Engram.Store.Tests;

public sealed class AutomationPluginGrantEvaluatorTests
{
    [Theory]
    [InlineData(AutomationPluginPermission.LocalFileRead)]
    [InlineData(AutomationPluginPermission.LocalFileWrite)]
    [InlineData(AutomationPluginPermission.OfficeWrite)]
    [InlineData(AutomationPluginPermission.Network)]
    public void CoreScopedGrants_RequireContextEvenWhenManifestOmitsPermission(AutomationPluginPermission permission)
    {
        var action = new AutomationAction
        {
            CapabilityId = "test.capability.run",
            CapabilityArguments = new() { ["url"] = "https://example.invalid" }
        };
        var capability = new AutomationPluginCapability { Id = action.CapabilityId! };
        var grant = new AutomationPluginGrant("engram.test.scoped", capability.Id,
            new HashSet<AutomationPluginPermission> { permission });

        Assert.Throws<AutomationPluginAuthorizationException>(() =>
            AutomationPluginGrantEvaluator.Demand(grant, capability, action));
    }

    [Fact]
    public void ApprovedReadRequiresGrantedPermissionAndAnAbsolutePath()
    {
        var action = new AutomationAction
        {
            CapabilityId = "test.file.read",
            Permission = ActionPermission.Approved,
            CapabilityArguments = new() { ["path"] = Path.GetFullPath("selected.txt") }
        };
        var capability = new AutomationPluginCapability
        {
            Id = action.CapabilityId, RequiredPermissions = [AutomationPluginPermission.LocalFileRead]
        };
        var grant = new AutomationPluginGrant("engram.test.scoped", capability.Id,
            new HashSet<AutomationPluginPermission>(capability.RequiredPermissions));

        AutomationPluginGrantEvaluator.Demand(grant, capability, action);
        Assert.Throws<AutomationPluginAuthorizationException>(() =>
            AutomationPluginGrantEvaluator.Demand(grant with { Permissions = new HashSet<AutomationPluginPermission>() },
                capability, action));
        action.CapabilityArguments["path"] = "relative.txt";
        Assert.Throws<AutomationPluginAuthorizationException>(() =>
            AutomationPluginGrantEvaluator.Demand(grant, capability, action));
    }

    [Theory]
    [InlineData("http://localhost:*", "http://localhost/", true)]
    [InlineData("http://localhost:*", "http://localhost:4321/", true)]
    [InlineData("http://localhost:*", "http://example.invalid:4321/", false)]
    [InlineData("https://*", "https://example.invalid/path", true)]
    [InlineData("https://example*", "https://example.invalid/", false)]
    [InlineData("https://example.invalid", "https://example.invalid:8443/", false)]
    public void NetworkScopeMatchesOrigins(string allowed, string url, bool expected)
    {
        var capability = new AutomationPluginCapability { Id = "test.navigate" };
        var action = new AutomationAction { CapabilityId = capability.Id, CapabilityArguments = new() { ["url"] = url } };
        var grant = new AutomationPluginGrant("engram.test.scoped", capability.Id,
            new HashSet<AutomationPluginPermission> { AutomationPluginPermission.Network }, [allowed]);
        if (expected)
            AutomationPluginGrantEvaluator.Demand(grant, capability, action);
        else
            Assert.Throws<AutomationPluginAuthorizationException>(() =>
                AutomationPluginGrantEvaluator.Demand(grant, capability, action));
    }
}
