using System;
using System.Threading;
using System.Threading.Tasks;
using Engram.Store.Automation;
using Xunit;
using ExecutionContext = Engram.Store.Automation.ExecutionContext;

namespace Engram.Store.Tests;

public class SemanticActionConfirmationTests
{
    private class FakeUiEmbodimentProvider : IUiEmbodimentProvider
    {
        public bool IsSimulationMode { get; set; }
        public string ProcessName { get; set; } = "explorer";
        public string WindowTitle { get; set; } = "My Documents";
        public string Url { get; set; } = "https://example.com/home";

        public Task<string> ExecuteActionAsync(AutomationAction action, CancellationToken ct = default)
        {
            action.Status = ActionStatus.Completed;
            return Task.FromResult("success");
        }

        public Task<(string ProcessName, string WindowTitle)> GetActiveWindowAsync(CancellationToken ct = default)
        {
            return Task.FromResult((ProcessName, WindowTitle));
        }

        public Task<string> GetUrlAsync(CancellationToken ct = default)
        {
            return Task.FromResult(Url);
        }
    }

    [Fact]
    public async Task OcrVerifier_ReturnsTrue_WhenTextInWindowTitle()
    {
        // Arrange
        var provider = new FakeUiEmbodimentProvider
        {
            WindowTitle = "Success - Task Complete"
        };
        var engine = new StateVerificationEngine(provider);
        var context = new ExecutionContext();
        context.SetVariable("StateVerificationEngine", engine);

        var verifier = new OcrVerifier("Task Complete");

        // Act
        var result = await verifier.VerifyAsync(context, CancellationToken.None);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task OcrVerifier_ReturnsTrue_WhenTextInUrl()
    {
        // Arrange
        var provider = new FakeUiEmbodimentProvider
        {
            WindowTitle = "Google Search",
            Url = "https://example.com/settings/profile?success=true"
        };
        var engine = new StateVerificationEngine(provider);
        var context = new ExecutionContext();
        context.SetVariable("StateVerificationEngine", engine);

        var verifier = new OcrVerifier("settings/profile");

        // Act
        var result = await verifier.VerifyAsync(context, CancellationToken.None);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task OcrVerifier_ReturnsFalse_WhenTextNotFound()
    {
        // Arrange
        var provider = new FakeUiEmbodimentProvider
        {
            WindowTitle = "Login Screen",
            Url = "https://example.com/login"
        };
        var engine = new StateVerificationEngine(provider);
        var context = new ExecutionContext();
        context.SetVariable("StateVerificationEngine", engine);

        var verifier = new OcrVerifier("Welcome Back");

        // Act
        var result = await verifier.VerifyAsync(context, CancellationToken.None);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task StateDeltaVerifier_ReturnsTrue_WhenStateChangesSequentially()
    {
        // Arrange
        var provider = new FakeUiEmbodimentProvider();
        var engine = new StateVerificationEngine(provider);
        var context = new ExecutionContext();
        context.SetVariable("StateVerificationEngine", engine);

        int callCount = 0;
        Func<ExecutionContext, Task<object>> captureState = ctx =>
        {
            callCount++;
            return Task.FromResult<object>(callCount);
        };

        Func<object, object, bool> evaluateDelta = (before, after) =>
        {
            var b = (int)before;
            var a = (int)after;
            return a > b;
        };

        var verifier = new StateDeltaVerifier("counter", captureState, evaluateDelta);

        // Act
        var result = await verifier.VerifyAsync(context, CancellationToken.None);

        // Assert
        Assert.True(result);
        Assert.Equal(2, callCount); // Called twice to check sequential state delta
    }

    [Fact]
    public async Task StateDeltaVerifier_UsesBeforeStateFromContext_WhenPresent()
    {
        // Arrange
        var provider = new FakeUiEmbodimentProvider();
        var engine = new StateVerificationEngine(provider);
        var context = new ExecutionContext();
        context.SetVariable("StateVerificationEngine", engine);
        context.SetVariable("counter_before", 42); // Seed prior state

        Func<ExecutionContext, Task<object>> captureState = ctx => Task.FromResult<object>(100);
        Func<object, object, bool> evaluateDelta = (before, after) =>
        {
            var b = (int)before;
            var a = (int)after;
            return b == 42 && a == 100;
        };

        var verifier = new StateDeltaVerifier("counter", captureState, evaluateDelta);

        // Act
        var result = await verifier.VerifyAsync(context, CancellationToken.None);

        // Assert
        Assert.True(result);
    }
}
