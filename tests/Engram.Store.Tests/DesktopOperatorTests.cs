using System;
using System.Threading;
using System.Threading.Tasks;
using Engram.Store.Automation;
using Xunit;

namespace Engram.Store.Tests;

public class DesktopOperatorTests
{
    [Fact]
    public void Constructor_SetsSimulationModeByDefault()
    {
        var op = new DesktopOperator();
        // Since we want default safety, it should start in simulation mode
        Assert.True(op.IsSimulationMode);
    }

    [Fact]
    public async Task ClickAsync_WithinBounds_SucceedsInSimulation()
    {
        var op = new DesktopOperator { IsSimulationMode = true };
        var exception = await Record.ExceptionAsync(() => op.ClickAsync(500, 500));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(-1, 100)]
    [InlineData(100, -1)]
    [InlineData(100000, 100)]
    [InlineData(100, 100000)]
    public async Task ClickAsync_OutOfBounds_ThrowsArgumentOutOfRangeException(int x, int y)
    {
        var op = new DesktopOperator { IsSimulationMode = true };
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => op.ClickAsync(x, y));
    }

    [Fact]
    public async Task ClickAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var op = new DesktopOperator { IsSimulationMode = true };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => op.ClickAsync(100, 100, cts.Token));
    }

    [Fact]
    public async Task TypeAsync_SucceedsInSimulation()
    {
        var op = new DesktopOperator { IsSimulationMode = true };
        var exception = await Record.ExceptionAsync(() => op.TypeAsync("Hello World"));
        Assert.Null(exception);
    }

    [Fact]
    public async Task TypeAsync_EmptyText_SucceedsImmediately()
    {
        var op = new DesktopOperator { IsSimulationMode = true };
        var exception = await Record.ExceptionAsync(() => op.TypeAsync(string.Empty));
        Assert.Null(exception);
    }

    [Fact]
    public async Task KeyPressAsync_SucceedsInSimulation()
    {
        var op = new DesktopOperator { IsSimulationMode = true };
        var exception = await Record.ExceptionAsync(() => op.KeyPressAsync("Enter"));
        Assert.Null(exception);
    }

    [Fact]
    public async Task GetActiveWindowAsync_ReturnsNonEmptyValues()
    {
        var op = new DesktopOperator();
        var (process, title) = await op.GetActiveWindowAsync();
        
        Assert.NotNull(process);
        Assert.NotNull(title);
    }

    [Fact]
    public void SafetyManager_VerifyCoordinateBounds_ThrowsIfOutOfBounds()
    {
        var manager = new ExecutionSafetyManager();
        Assert.Throws<InvalidOperationException>(() => manager.VerifyCoordinateBounds(-10, 50));
        Assert.Throws<InvalidOperationException>(() => manager.VerifyCoordinateBounds(100, 2000));
        manager.VerifyCoordinateBounds(500, 500); // Should not throw
    }

    [Fact]
    public void SafetyManager_VerifyProcessSafety_ThrowsIfBlacklisted()
    {
        var manager = new ExecutionSafetyManager();
        Assert.Throws<InvalidOperationException>(() => manager.VerifyProcessSafety("powershell.exe", "Windows PowerShell"));
        Assert.Throws<InvalidOperationException>(() => manager.VerifyProcessSafety("cmd", "Administrator: Command Prompt"));
        Assert.Throws<InvalidOperationException>(() => manager.VerifyProcessSafety("explorer", "Administrator: File Explorer")); // privileged
        manager.VerifyProcessSafety("explorer", "File Explorer"); // Should not throw
    }

    [Fact]
    public void SafetyManager_VerifyUrlSafety_ThrowsIfBlacklisted()
    {
        var manager = new ExecutionSafetyManager();
        Assert.Throws<InvalidOperationException>(() => manager.VerifyUrlSafety("http://169.254.169.254/latest/meta-data/"));
        Assert.Throws<InvalidOperationException>(() => manager.VerifyUrlSafety("https://localhost/delete-account"));
        manager.VerifyUrlSafety("https://google.com"); // Should not throw
    }

    [Fact]
    public void SafetyManager_VerifyRateLimit_ThrowsWhenExceeded()
    {
        var manager = new ExecutionSafetyManager(maxActionsPerMinute: 2);
        manager.VerifyRateLimit();
        manager.VerifyRateLimit();
        Assert.Throws<InvalidOperationException>(() => manager.VerifyRateLimit());
    }

    [Fact]
    public void SafetyManager_VerifyMouseFailsafe_ThrowsIfMouseMovedSignificantly()
    {
        var manager = new ExecutionSafetyManager { IsSimulationMode = true };
        
        // Initial setup
        manager.SimulatedMousePosition = new ExecutionSafetyManager.Win32Point { X = 100, Y = 100 };
        manager.InitializeMouseFailsafe();
        
        // No movement
        manager.VerifyMouseFailsafe();

        // Small movement (within threshold 50)
        manager.SimulatedMousePosition = new ExecutionSafetyManager.Win32Point { X = 120, Y = 120 };
        manager.VerifyMouseFailsafe();

        // Large movement
        manager.SimulatedMousePosition = new ExecutionSafetyManager.Win32Point { X = 200, Y = 200 };
        Assert.Throws<InvalidOperationException>(() => manager.VerifyMouseFailsafe());
    }
}
