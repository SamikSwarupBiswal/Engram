using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Engram.Store.Automation;
using Xunit;
using ExecutionContext = Engram.Store.Automation.ExecutionContext;

namespace Engram.Store.Tests;

public class Wave2SafetyTests
{
    [Fact]
    public async Task StateVerificationEngine_VerifiesFileExists()
    {
        var mockProvider = new MockUiProvider();
        var engine = new StateVerificationEngine(mockProvider);

        var tempFile = Path.GetTempFileName();
        try
        {
            var exists = await engine.VerifyFileExistsAsync(tempFile, CancellationToken.None);
            Assert.True(exists);

            var doesNotExist = await engine.VerifyFileExistsAsync(tempFile + "-nonexistent", CancellationToken.None);
            Assert.False(doesNotExist);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task StateVerificationEngine_VerifiesActiveWindow()
    {
        var mockProvider = new MockUiProvider
        {
            MockProcessName = "chrome",
            MockWindowTitle = "Google - Search"
        };
        var engine = new StateVerificationEngine(mockProvider);

        var match1 = await engine.VerifyActiveWindowAsync("chrome", ".*search.*", CancellationToken.None);
        Assert.True(match1);

        var match2 = await engine.VerifyActiveWindowAsync("firefox", null, CancellationToken.None);
        Assert.False(match2);

        var match3 = await engine.VerifyActiveWindowAsync("chrome", ".*firefox.*", CancellationToken.None);
        Assert.False(match3);
    }

    [Fact]
    public async Task StateVerificationEngine_VerifiesUrl()
    {
        var mockProvider = new MockUiProvider { MockUrl = "https://github.com/google/engram" };
        var engine = new StateVerificationEngine(mockProvider);

        var match1 = await engine.VerifyUrlAsync("github.com", CancellationToken.None);
        Assert.True(match1);

        var match2 = await engine.VerifyUrlAsync("^https://github.com/.*", CancellationToken.None);
        Assert.True(match2);

        var match3 = await engine.VerifyUrlAsync("microsoft.com", CancellationToken.None);
        Assert.False(match3);
    }

    [Fact]
    public async Task StateVerificationEngine_VerifiesDomNode()
    {
        var mockProvider = new MockUiProvider
        {
            ActionHandler = action =>
            {
                if (action.Type == ActionType.Wait && action.Target?.Selector == "#submit-button")
                {
                    action.Status = ActionStatus.Completed;
                    return Task.FromResult("Element found");
                }
                action.Status = ActionStatus.Failed;
                return Task.FromResult("fail");
            }
        };
        var engine = new StateVerificationEngine(mockProvider);

        var exists = await engine.VerifyDomNodeExistsAsync("#submit-button", CancellationToken.None);
        Assert.True(exists);

        var missing = await engine.VerifyDomNodeExistsAsync("#cancel-button", CancellationToken.None);
        Assert.False(missing);
    }

    [Fact]
    public void ReversibilityEvaluator_ClassifiesActionsCorrectly()
    {
        var evaluator = new ReversibilityEvaluator();

        // Reversible
        var nav = new AutomationAction { Type = ActionType.Navigate, Value = "https://example.com" };
        Assert.Equal(ReversibilityScore.Reversible, evaluator.Evaluate(nav));
        Assert.False(evaluator.IsIrreversible(nav));

        // Mostly
        var type = new AutomationAction { Type = ActionType.Type, Value = "John Doe" };
        Assert.Equal(ReversibilityScore.Mostly, evaluator.Evaluate(type));
        Assert.False(evaluator.IsIrreversible(type));

        // Irreversible
        var deleteFile = new AutomationAction { Type = ActionType.Click, Description = "Delete the selected database log file" };
        Assert.Equal(ReversibilityScore.No, evaluator.Evaluate(deleteFile));
        Assert.True(evaluator.IsIrreversible(deleteFile));

        var sendEmail = new AutomationAction { Type = ActionType.Click, Description = "Send monthly report email" };
        Assert.Equal(ReversibilityScore.No, evaluator.Evaluate(sendEmail));
        Assert.True(evaluator.IsIrreversible(sendEmail));
    }

    [Fact]
    public void SemanticSummarizer_GeneratesReadableSummaries()
    {
        var summarizer = new SemanticSummarizer();

        var nav = new AutomationAction { Type = ActionType.Navigate, Value = "https://example.com" };
        Assert.Equal("Navigate to URL 'https://example.com'", summarizer.Summarize(nav));

        var clickCoord = new AutomationAction
        {
            Type = ActionType.Click,
            Target = new ActionTarget { X = 500, Y = 300 }
        };
        Assert.Equal("Click screen coordinates (500, 300)", summarizer.Summarize(clickCoord));

        var clickText = new AutomationAction
        {
            Type = ActionType.Click,
            Target = new ActionTarget { Text = "Submit Request" }
        };
        Assert.Equal("Click element containing text 'Submit Request'", summarizer.Summarize(clickText));

        var type = new AutomationAction
        {
            Type = ActionType.Type,
            Value = "hello",
            Target = new ActionTarget { Selector = "#username" },
            Description = "Enter login username"
        };
        Assert.Equal("[Enter login username] Type 'hello' into field matching selector '#username'", summarizer.Summarize(type));
    }

    [Fact]
    public async Task ActionRuntime_BlocksIrreversibleActions_FromAutoApproval()
    {
        var context = new ExecutionContext();
        var mockProvider = new MockUiProvider();
        context.SetVariable("UiEmbodimentProvider", mockProvider);

        var executor = new ActionExecutor();
        var gate = new PermissionGate(); // Gate auto-approves safe actions (e.g. Wait, Screenshot)
        var safety = new ExecutionSafetyManager();
        var trustManager = new TrustTierManager(TrustTier.Privileged);

        using var runtime = new ActionRuntime(executor, gate, safety, trustManager);

        var plan = new ExecutionPlan
        {
            Goal = "Run irreversible action",
            PlanId = "irreversible-plan"
        };

        // Although Screenshot is technically a safe/auto-approved action type, 
        // the description containing "delete" keyword makes it irreversible!
        var action = new AutomationAction
        {
            Type = ActionType.Screenshot,
            Description = "Delete system directories screenshot",
            Permission = ActionPermission.Pending
        };

        plan.Steps["step1"] = new ExecutionStep
        {
            Id = "step1",
            Action = action,
            Status = StepStatus.Pending
        };

        // Should throw because irreversible action bypasses auto-approval and fails when not explicitly approved.
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => 
            runtime.ExecutePlanAsync(plan, context, CancellationToken.None));

        Assert.Contains("action is irreversible", exception.Message);
        Assert.Equal(StepStatus.Failed, plan.Steps["step1"].Status);
        Assert.Empty(mockProvider.ExecutedActions);
    }

    [Fact]
    public async Task ActionRuntime_ExecutesVerificationLoops_UsingStateVerificationEngine()
    {
        var context = new ExecutionContext();
        var mockProvider = new MockUiProvider();
        context.SetVariable("UiEmbodimentProvider", mockProvider);

        var executor = new ActionExecutor();
        var gate = new PermissionGate();
        var safety = new ExecutionSafetyManager();
        var trustManager = new TrustTierManager(TrustTier.Privileged);

        using var runtime = new ActionRuntime(executor, gate, safety, trustManager);

        var tempFile = Path.GetTempFileName();
        try
        {
            var plan = new ExecutionPlan
            {
                Goal = "File verification test",
                PlanId = "file-verify-plan"
            };

            var action = new AutomationAction
            {
                Type = ActionType.Wait,
                Value = "100",
                Permission = ActionPermission.Approved
            };

            plan.Steps["step1"] = new ExecutionStep
            {
                Id = "step1",
                Action = action,
                Status = StepStatus.Pending,
                Verifier = new FileExistsVerifier(tempFile)
            };

            await runtime.ExecutePlanAsync(plan, context, CancellationToken.None);

            Assert.Equal(StepStatus.Completed, plan.Steps["step1"].Status);
            
            // Check that StateVerificationEngine was registered in context
            var engine = context.GetVariable<StateVerificationEngine>("StateVerificationEngine");
            Assert.NotNull(engine);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
