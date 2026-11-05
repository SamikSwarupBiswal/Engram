using Engram.Store.Automation;
using System.Text.Json;

namespace Engram.Store.Tests;

public sealed class AutomaticApprovalScopeTests
{
    [Fact]
    public void SerializedActionCannotCarryAutomaticExecutionAuthority()
    {
        var action = new AutomationAction
        {
            Type = ActionType.Calculate, Value = "42*7", RiskLevel = AutomationRiskLevel.High
        };
        AutomationApprovalPolicy.GrantAutomaticExecution(action);
        var json = JsonSerializer.Serialize(action);
        Assert.DoesNotContain("AutomaticApprovalFingerprint", json);
        var restored = JsonSerializer.Deserialize<AutomationAction>(json)!;

        Assert.False(AutomationApprovalPolicy.IsAutomatic(restored));
        Assert.Equal(ActionPermission.Pending, new PermissionGate { IsActiveAutomation = true }.CheckPermission(restored));
    }

    [Fact]
    public void ActiveAutomationCannotApproveAnUnscopedSendButton()
    {
        var action = new AutomationAction
        {
            Type = ActionType.Click, Description = "Click button",
            Target = new ActionTarget { Selector = "#send" }
        };
        Assert.Equal(ActionPermission.Pending, new PermissionGate { IsActiveAutomation = true }.CheckPermission(action));
    }

    [Fact]
    public void BoundBrowserNavigationCannotGrantSubmissionAutomatically()
    {
        var action = new AutomationAction
        {
            Type = ActionType.Navigate, Value = "https://example.test/submit?amount=1",
            CapabilityId = "browser.page.navigate",
            CapabilityArguments = new() { ["url"] = "https://example.test/submit?amount=1" }
        };

        Assert.Throws<InvalidOperationException>(() => AutomationApprovalPolicy.GrantAutomaticExecution(action));
    }

    [Fact]
    public async Task OrdinaryNavigationHasNoDetectedExternalEffect()
    {
        var action = new AutomationAction { Type = ActionType.Navigate, Value = "https://example.test/docs" };

        Assert.False(ExternalImpactGate.HasPotentialExternalEffect(action));
        Assert.True(await new ExternalImpactGate().ValidateActionSafetyAsync(action));
    }

    [Theory]
    [InlineData("https://example.test/submit", "Continue", null, null)]
    [InlineData("https://example.test/s%75bmit", "Continue", null, null)]
    [InlineData("https://example.test/%2564elete", "Continue", null, null)]
    [InlineData("https://example.test/page", "Send the prepared message", null, null)]
    [InlineData("https://example.test/page", "Continue", "#checkout", null)]
    [InlineData("https://example.test/page", "Continue", null, "purchase")]
    public async Task PotentialExternalEffectIsDetectedAcrossNavigationFields(
        string url, string description, string? selector, string? text)
    {
        var action = new AutomationAction
        {
            Type = ActionType.Navigate, Value = url, Description = description,
            Target = new ActionTarget { Selector = selector, Text = text }
        };

        Assert.True(ExternalImpactGate.HasPotentialExternalEffect(action));
        Assert.False(await new ExternalImpactGate().ValidateActionSafetyAsync(action));
    }

    [Fact]
    public void CoreGrantedCalculatorSkipsWarmupAndExplicitApproval()
    {
        var action = new AutomationAction
        {
            Type = ActionType.Calculate, Value = "42*7", RiskLevel = AutomationRiskLevel.High
        };
        var gate = new PermissionGate();
        Assert.True(gate.RequiresExplicitApproval(action));

        AutomationApprovalPolicy.GrantAutomaticExecution(action);

        Assert.False(gate.RequiresExplicitApproval(action));
        Assert.Equal(ActionPermission.AutoApproved, gate.CheckPermission(action));

        action.Value = "42*8";

        Assert.True(gate.RequiresExplicitApproval(action));
        Assert.Equal(ActionPermission.Pending, gate.CheckPermission(action));
    }

    [Fact]
    public async Task SelectedDocumentDataDoesNotBecomeAnExternalInstruction()
    {
        var action = new AutomationAction
        {
            Type = ActionType.Type,
            Value = "The document says delete a draft before you send the mail.",
            Description = "Insert the selected document text",
            RiskLevel = AutomationRiskLevel.High
        };
        AutomationApprovalPolicy.GrantAutomaticExecution(action);

        Assert.False(new ReversibilityEvaluator().IsIrreversible(action));
        Assert.True(await new ExternalImpactGate().ValidateActionSafetyAsync(action));

        action.Type = ActionType.Upload;

        Assert.True(new PermissionGate().RequiresExplicitApproval(action));
        Assert.False(await new ExternalImpactGate().ValidateActionSafetyAsync(action));
    }

}
