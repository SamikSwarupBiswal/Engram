using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Engram.Store.Automation;
using Xunit;
using ExecutionContext = Engram.Store.Automation.ExecutionContext;

namespace Engram.Store.Tests;

public class SafetyAndRecoveryTests
{
    [Fact]
    public async Task HtmlContentVerifier_ReturnsTrue_IfContentMatches()
    {
        var context = new ExecutionContext();
        using var browser = new BrowserAgentRuntime { IsSimulationMode = true };
        context.SetVariable("BrowserAgent", browser);

        var driver = (StubBrowserDriver)await browser.GetDriverAsync();
        driver.CurrentHtml = "<html><body><div id='status'>Operation Success</div></body></html>";

        var verifier = new HtmlContentVerifier("#status", "Success");
        var result = await verifier.VerifyAsync(context, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task HtmlContentVerifier_ReturnsFalse_IfContentMismatches()
    {
        var context = new ExecutionContext();
        using var browser = new BrowserAgentRuntime { IsSimulationMode = true };
        context.SetVariable("BrowserAgent", browser);

        var driver = (StubBrowserDriver)await browser.GetDriverAsync();
        driver.CurrentHtml = "<html><body><div id='status'>Operation Failed</div></body></html>";

        var verifier = new HtmlContentVerifier("#status", "Success");
        var result = await verifier.VerifyAsync(context, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task ActiveWindowVerifier_MatchesProcessAndTitle()
    {
        var context = new ExecutionContext();
        var desktopOp = new StubDesktopOperator();
        context.SetVariable("DesktopOperator", desktopOp);

        var verifier = new ActiveWindowVerifier("explorer", ".*File Explorer.*");
        var result = await verifier.VerifyAsync(context, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task UrlPatternVerifier_MatchesUrl()
    {
        var context = new ExecutionContext();
        using var browser = new BrowserAgentRuntime { IsSimulationMode = true };
        context.SetVariable("BrowserAgent", browser);

        await browser.NavigateAsync("https://example.com/checkout/success");

        var verifier = new UrlPatternVerifier(".*/success");
        var result = await verifier.VerifyAsync(context, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task RetryWithDelayRecovery_LimitsRetries()
    {
        var context = new ExecutionContext();
        var recovery = new RetryWithDelayRecovery(2, TimeSpan.FromMilliseconds(5));

        // 1st retry
        var ok1 = await recovery.RecoverAsync(context, new Exception(), CancellationToken.None);
        Assert.True(ok1);

        // 2nd retry
        var ok2 = await recovery.RecoverAsync(context, new Exception(), CancellationToken.None);
        Assert.True(ok2);

        // 3rd retry should exceed limit
        var ok3 = await recovery.RecoverAsync(context, new Exception(), CancellationToken.None);
        Assert.False(ok3);
    }

    [Fact]
    public async Task AlternativeStepRecovery_ExecutesAlternativeActions()
    {
        var context = new ExecutionContext();
        var executor = new ActionExecutor();
        context.SetVariable("ActionExecutor", executor);

        var altAction = new AutomationAction
        {
            Type = ActionType.Navigate,
            Value = "https://fallback.com",
            Description = "Go to fallback page"
        };

        var recovery = new AlternativeStepRecovery(altAction);
        var result = await recovery.RecoverAsync(context, new Exception(), CancellationToken.None);

        Assert.True(result);
        Assert.Equal(ActionStatus.Completed, altAction.Status);
        Assert.Equal("Navigated to https://fallback.com", altAction.Result);
    }

    [Fact]
    public async Task NavigateBackRollback_NavigatesToPreviousUrl()
    {
        var context = new ExecutionContext();
        using var browser = new BrowserAgentRuntime { IsSimulationMode = true };
        context.SetVariable("BrowserAgent", browser);

        var rollback = new NavigateBackRollback("https://homepage.com");
        await rollback.RollbackAsync(context, CancellationToken.None);

        Assert.Equal("https://homepage.com", await browser.GetUrlAsync());
    }

    [Fact]
    public async Task CloseWindowRollback_TriggersAltF4()
    {
        var context = new ExecutionContext();
        var desktopOp = new DesktopOperator { IsSimulationMode = true };
        context.SetVariable("DesktopOperator", desktopOp);

        var rollback = new CloseWindowRollback();
        var exception = await Record.ExceptionAsync(() => rollback.RollbackAsync(context, CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public void ExecutionContext_SerializeAndDeserializeState_Succeeds()
    {
        var context = new ExecutionContext();
        context.SetVariable("test_str", "hello");
        context.SetVariable("test_int", 42);
        context.SetVariable("test_bool", true);
        context.SetVariable("test_unserializable", new ActionExecutor());

        var json = context.SerializeState();

        var context2 = new ExecutionContext();
        context2.DeserializeState(json);

        Assert.Equal("hello", context2.GetVariable<string>("test_str"));
        Assert.Equal(42, context2.GetVariable<int>("test_int"));
        Assert.True(context2.GetVariable<bool>("test_bool"));
        Assert.Null(context2.GetVariable<ActionExecutor>("test_unserializable"));
    }

    [Fact]
    public async Task ExecutionPlanHistoryStore_SaveAndLoad_Succeeds()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ExecutionPlanHistoryStore(tempDir);
            var plan = new ExecutionPlan
            {
                Goal = "test-goal",
                PlanId = "testplan123"
            };
            var action = new AutomationAction
            {
                Type = ActionType.Navigate,
                Value = "https://test.com",
                Description = "Navigate to test"
            };
            plan.Steps["step1"] = new ExecutionStep
            {
                Id = "step1",
                Action = action,
                Status = StepStatus.Completed
            };

            var context = new ExecutionContext();
            context.SetVariable("key1", "val1");

            await store.SaveRunAsync(plan, context);

            var runData = await store.LoadRunAsync("testplan123");
            Assert.NotNull(runData);
            Assert.Equal("testplan123", runData.PlanId);
            Assert.Equal("test-goal", runData.Goal);
            Assert.Single(runData.Steps);
            Assert.Equal("step1", runData.Steps[0].Id);
            Assert.Equal("Navigate", runData.Steps[0].ActionType);
            Assert.Equal("Completed", runData.Steps[0].Status);

            var context2 = new ExecutionContext();
            context2.DeserializeState(runData.SerializedContext);
            Assert.Equal("val1", context2.GetVariable<string>("key1"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    private class StubDesktopOperator : IDesktopOperator
    {
        public bool IsSimulationMode { get; set; } = true;
        public string ActiveProcess { get; set; } = "explorer";
        public string ActiveTitle { get; set; } = "File Explorer";

        public Task ClickAsync(int x, int y, CancellationToken ct = default) => Task.CompletedTask;
        public Task TypeAsync(string text, CancellationToken ct = default) => Task.CompletedTask;
        public Task KeyPressAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
        public Task<(string ProcessName, string WindowTitle)> GetActiveWindowAsync(CancellationToken ct = default)
        {
            return Task.FromResult((ActiveProcess, ActiveTitle));
        }
    }
}
