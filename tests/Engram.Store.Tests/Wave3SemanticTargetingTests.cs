using System;
using System.Threading;
using System.Threading.Tasks;
using Engram.Store.Automation;
using Xunit;

namespace Engram.Store.Tests;

public class Wave3SemanticTargetingTests
{
    [Fact]
    public async Task SemanticElementResolver_SimulationMode_ReturnsMockCoordinates()
    {
        var mockProvider = new MockUiProvider { IsSimulationMode = true };
        var resolver = new SemanticElementResolver(mockProvider);

        var target1 = await resolver.ResolveElementAsync("Save button", CancellationToken.None);
        var target2 = await resolver.ResolveElementAsync("Save button", CancellationToken.None);

        Assert.NotNull(target1);
        Assert.Equal(target1.X, target2.X);
        Assert.Equal(target1.Y, target2.Y);
        Assert.Contains("save-button", target1.Selector);
    }

    [Fact]
    public async Task WindowsUiAutomationProvider_SimulationMode_DoesNotInvokeDesktopOperator()
    {
        var desktopMock = new MockDesktopOperator();
        var winProvider = new WindowsUiAutomationProvider(desktopMock) { IsSimulationMode = true };

        var action = new AutomationAction
        {
            Type = ActionType.Click,
            Target = new ActionTarget { X = 100, Y = 200 },
            Description = "Test Click"
        };

        var result = await winProvider.ExecuteActionAsync(action, CancellationToken.None);

        Assert.Contains("Simulated:", result);
        Assert.Equal(ActionStatus.Completed, action.Status);
        Assert.False(desktopMock.ClickCalled);
    }

    [Fact]
    public async Task WindowsUiAutomationProvider_ActiveMode_InvokesDesktopOperator()
    {
        var desktopMock = new MockDesktopOperator();
        var winProvider = new WindowsUiAutomationProvider(desktopMock) { IsSimulationMode = false };

        var action = new AutomationAction
        {
            Type = ActionType.Click,
            Target = new ActionTarget { X = 350, Y = 400 },
            Description = "Real Click"
        };

        await winProvider.ExecuteActionAsync(action, CancellationToken.None);

        Assert.True(desktopMock.ClickCalled);
        Assert.Equal(350, desktopMock.LastX);
        Assert.Equal(400, desktopMock.LastY);
        Assert.Equal(ActionStatus.Completed, action.Status);
    }

    [Fact]
    public async Task WindowsUiAutomationProvider_TypesTextCorrectly()
    {
        var desktopMock = new MockDesktopOperator();
        var winProvider = new WindowsUiAutomationProvider(desktopMock) { IsSimulationMode = false };

        var action = new AutomationAction
        {
            Type = ActionType.Type,
            Value = "Hello, World!",
            Description = "Real Type"
        };

        await winProvider.ExecuteActionAsync(action, CancellationToken.None);

        Assert.True(desktopMock.TypeCalled);
        Assert.Equal("Hello, World!", desktopMock.LastTypedText);
    }

    private class MockDesktopOperator : IDesktopOperator
    {
        public bool IsSimulationMode { get; set; }
        public bool ClickCalled { get; private set; }
        public int LastX { get; private set; }
        public int LastY { get; private set; }
        public bool TypeCalled { get; private set; }
        public string? LastTypedText { get; private set; }
        public bool KeyPressCalled { get; private set; }
        public string? LastKeyPressed { get; private set; }

        public Task ClickAsync(int x, int y, CancellationToken ct = default)
        {
            ClickCalled = true;
            LastX = x;
            LastY = y;
            return Task.CompletedTask;
        }

        public Task TypeAsync(string text, CancellationToken ct = default)
        {
            TypeCalled = true;
            LastTypedText = text;
            return Task.CompletedTask;
        }

        public Task KeyPressAsync(string key, CancellationToken ct = default)
        {
            KeyPressCalled = true;
            LastKeyPressed = key;
            return Task.CompletedTask;
        }

        public Task<(string ProcessName, string WindowTitle)> GetActiveWindowAsync(CancellationToken ct = default)
        {
            return Task.FromResult(("explorer", "File Explorer"));
        }
    }
}
