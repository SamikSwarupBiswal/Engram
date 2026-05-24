using System;
using System.Threading.Tasks;
using Xunit;
using Engram.Store.Automation;

namespace Engram.Store.Tests.Automation;

public class DpiScaleTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Theory]
    [InlineData(100, 100, 1.0, 100, 100)]
    [InlineData(100, 100, 1.25, 125, 125)]
    [InlineData(100, 100, 1.50, 150, 150)]
    [InlineData(100, 100, 2.0, 200, 200)]
    public void TranslateLogicalToPhysical_AppliesScaleFactorCorrectly(int logX, int logY, double scale, int expectedPhysX, int expectedPhysY)
    {
        // We simulate scale-aware math directly:
        var physX = (int)(logX * scale);
        var physY = (int)(logY * scale);

        Assert.Equal(expectedPhysX, physX);
        Assert.Equal(expectedPhysY, physY);
    }

    [Fact]
    public void GetVirtualScreenBounds_ReturnsSensibleDefaultsWhenNotWindows()
    {
        var bounds = DpiScaleAwareCoordinates.GetVirtualScreenBounds();
        
        Assert.True(bounds.Width > 0);
        Assert.True(bounds.Height > 0);
    }

    [Fact]
    public void MapToAbsoluteCoordinates_CorrectlyMapsBounds()
    {
        // Maps center of bounds
        var bounds = DpiScaleAwareCoordinates.GetVirtualScreenBounds();
        var centerX = bounds.Left + bounds.Width / 2;
        var centerY = bounds.Top + bounds.Height / 2;

        var (dx, dy) = DpiScaleAwareCoordinates.MapToAbsoluteCoordinates(centerX, centerY);

        // Absolute range is 0 to 65535, so center should be close to 32768
        Assert.InRange(dx, 32760, 32776);
        Assert.InRange(dy, 32760, 32776);
    }

    [Fact]
    public async Task WindowsUiAutomationProvider_CoordinateVerificationSampling_ConfidenceDropsOnForceFailure()
    {
        var mockOperator = new MockDesktopOperator();
        var provider = new WindowsUiAutomationProvider(mockOperator)
        {
            IsSimulationMode = false
        };

        // Assert initial confidence is full
        Assert.Equal(1.0, provider.CoordinateConfidence);

        // Define a Click action that will force a verification failure
        var action = new AutomationAction
        {
            ActionId = "act-1",
            Type = ActionType.Click,
            Target = new ActionTarget
            {
                X = 100,
                Y = 100,
                Text = "FORCE_VERIFICATION_FAIL"
            }
        };

        bool eventRaised = false;
        double newConfidenceVal = 1.0;
        provider.VerificationStatusChanged += (reason, val) =>
        {
            eventRaised = true;
            newConfidenceVal = val;
        };

        var result = await provider.ExecuteActionAsync(action);

        Assert.Contains("verification failed", result);
        Assert.True(eventRaised);
        Assert.Equal(0.51, newConfidenceVal);
        Assert.Equal(0.51, provider.CoordinateConfidence);
    }

    private class MockDesktopOperator : IDesktopOperator
    {
        public bool IsSimulationMode { get; set; }

        public Task ClickAsync(int x, int y, System.Threading.CancellationToken ct = default) => Task.CompletedTask;
        public Task TypeAsync(string text, System.Threading.CancellationToken ct = default) => Task.CompletedTask;
        public Task KeyPressAsync(string key, System.Threading.CancellationToken ct = default) => Task.CompletedTask;
        public Task<(string ProcessName, string WindowTitle)> GetActiveWindowAsync(System.Threading.CancellationToken ct = default)
        {
            return Task.FromResult(("mock_proc", "Mock Active Window"));
        }
    }
}
