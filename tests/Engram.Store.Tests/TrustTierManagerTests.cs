using System;
using Engram.Store.Automation;
using Xunit;

namespace Engram.Store.Tests;

public class TrustTierManagerTests
{
    [Fact]
    public void ObserveTier_OnlyAllowsReadSensing()
    {
        var manager = new TrustTierManager(TrustTier.Observe);

        // Allowed actions
        manager.ValidateAction(new AutomationAction { Type = ActionType.Screenshot });
        manager.ValidateAction(new AutomationAction { Type = ActionType.Wait });

        // Blocked actions
        Assert.Throws<InvalidOperationException>(() => 
            manager.ValidateAction(new AutomationAction { Type = ActionType.Navigate, Value = "https://example.com" }));
        Assert.Throws<InvalidOperationException>(() => 
            manager.ValidateAction(new AutomationAction { Type = ActionType.Click }));
        Assert.Throws<InvalidOperationException>(() => 
            manager.ValidateAction(new AutomationAction { Type = ActionType.Type, Value = "test" }));
    }

    [Fact]
    public void SuggestTier_BlocksAllDirectExecution()
    {
        var manager = new TrustTierManager(TrustTier.Suggest);

        Assert.Throws<InvalidOperationException>(() => manager.ValidateAction(new AutomationAction { Type = ActionType.Screenshot }));
        Assert.Throws<InvalidOperationException>(() => manager.ValidateAction(new AutomationAction { Type = ActionType.Wait }));
        Assert.Throws<InvalidOperationException>(() => manager.ValidateAction(new AutomationAction { Type = ActionType.Click }));
    }

    [Fact]
    public void AssistTier_AllowsLowRiskViewportActions()
    {
        var manager = new TrustTierManager(TrustTier.Assist);

        // Allowed
        manager.ValidateAction(new AutomationAction { Type = ActionType.Screenshot });
        manager.ValidateAction(new AutomationAction { Type = ActionType.Wait });
        manager.ValidateAction(new AutomationAction { Type = ActionType.Navigate });
        manager.ValidateAction(new AutomationAction { Type = ActionType.Scroll });

        // Blocked
        Assert.Throws<InvalidOperationException>(() => manager.ValidateAction(new AutomationAction { Type = ActionType.Click }));
        Assert.Throws<InvalidOperationException>(() => manager.ValidateAction(new AutomationAction { Type = ActionType.Type }));
    }

    [Fact]
    public void OperateTier_AllowsWorkflowsButBlocksPrivileged()
    {
        var manager = new TrustTierManager(TrustTier.Operate);

        // Allowed
        manager.ValidateAction(new AutomationAction { Type = ActionType.Click });
        manager.ValidateAction(new AutomationAction { Type = ActionType.Type });
        manager.ValidateAction(new AutomationAction { Type = ActionType.KeyPress });

        // Blocked privileged operations
        Assert.Throws<InvalidOperationException>(() => 
            manager.ValidateAction(new AutomationAction { Type = ActionType.Click, Description = "Edit registry keys" }));
        Assert.Throws<InvalidOperationException>(() => 
            manager.ValidateAction(new AutomationAction { Type = ActionType.Type, Value = "C:\\Windows\\System32\\cmd.exe" }));
    }

    [Fact]
    public void RestrictedTier_BlocksDangerousAndDestructiveActions()
    {
        var manager = new TrustTierManager(TrustTier.Restricted);

        // Allowed
        manager.ValidateAction(new AutomationAction { Type = ActionType.Click });

        // Blocked (Upload/Download/Delete keyword)
        Assert.Throws<InvalidOperationException>(() => manager.ValidateAction(new AutomationAction { Type = ActionType.Upload }));
        Assert.Throws<InvalidOperationException>(() => manager.ValidateAction(new AutomationAction { Type = ActionType.Download }));
        Assert.Throws<InvalidOperationException>(() => 
            manager.ValidateAction(new AutomationAction { Type = ActionType.Click, Description = "Delete temporary file" }));
        Assert.Throws<InvalidOperationException>(() => 
            manager.ValidateAction(new AutomationAction { Type = ActionType.Click, Description = "uninstall software" }));
    }

    [Fact]
    public void PrivilegedTier_AllowsAllActions()
    {
        var manager = new TrustTierManager(TrustTier.Privileged);

        // All should pass
        manager.ValidateAction(new AutomationAction { Type = ActionType.Upload });
        manager.ValidateAction(new AutomationAction { Type = ActionType.Download });
        manager.ValidateAction(new AutomationAction { Type = ActionType.Click, Description = "Delete database registry files" });
    }
}
