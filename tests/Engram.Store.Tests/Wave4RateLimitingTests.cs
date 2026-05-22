using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Engram.Store.Automation;
using Xunit;

namespace Engram.Store.Tests;

public class Wave4RateLimitingTests
{
    [Fact]
    public void SovereigntyMonitor_IdleByDefault()
    {
        var monitor = new SovereigntyMonitor(2000);
        // By default, in tests/CI it shouldn't detect activity
        Assert.False(monitor.DetectUserActivity());
        monitor.VerifySovereignty(); // Should not throw
    }

    [Fact]
    public async Task RateLimiter_EnforcesInteractionDelays()
    {
        var limiter = new RateLimiter(100, 200, 30);

        // Keystroke delay test
        var sw = Stopwatch.StartNew();
        await limiter.ThrottleActionAsync(ActionType.Type, CancellationToken.None);
        await limiter.ThrottleActionAsync(ActionType.Type, CancellationToken.None);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds >= 90, $"Elapsed was {sw.ElapsedMilliseconds}ms");

        // Mouse click delay test
        sw = Stopwatch.StartNew();
        await limiter.ThrottleActionAsync(ActionType.Click, CancellationToken.None);
        await limiter.ThrottleActionAsync(ActionType.Click, CancellationToken.None);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds >= 190, $"Elapsed was {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task RateLimiter_EnforcesMaxActionsPerMinute()
    {
        // Max 5 actions per minute
        var limiter = new RateLimiter(0, 0, 5);

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 5; i++)
        {
            await limiter.ThrottleActionAsync(ActionType.Screenshot, CancellationToken.None);
        }
        
        var cts = new CancellationTokenSource(200);
        // The 6th action should hit the throttling delay. Under 200ms token, it should cancel or delay.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => 
        {
            await limiter.ThrottleActionAsync(ActionType.Screenshot, cts.Token);
        });
    }

    [Fact]
    public void RateLimiter_PreventsReplanOscillation()
    {
        var limiter = new RateLimiter();

        limiter.RecordReplan();
        limiter.RecordReplan();
        limiter.RecordReplan();

        // 4th consecutive replan within threshold must throw
        Assert.Throws<InvalidOperationException>(() => limiter.RecordReplan());
    }

    [Fact]
    public void ContainmentGuard_EnforcesDirectoryContainment()
    {
        var tempDir = Path.GetTempPath();
        var guard = new ContainmentGuard(new[] { tempDir });

        // Allowed path
        guard.VerifyPathSafety(Path.Combine(tempDir, "test.txt"));

        // Outside path
        Assert.Throws<InvalidOperationException>(() => 
            guard.VerifyPathSafety("C:/Windows/System32/drivers/etc/hosts"));

        // Blocked keyword
        Assert.Throws<InvalidOperationException>(() => 
            guard.VerifyPathSafety(Path.Combine(tempDir, "system32", "file.txt")));
    }

    [Fact]
    public void ContainmentGuard_UrlSafety()
    {
        var tempDir = Path.GetTempPath();
        var guard = new ContainmentGuard(new[] { tempDir });

        // Safe web URL
        guard.VerifyUrlSafety("https://google.com");

        // Unsafe local file URL
        Assert.Throws<InvalidOperationException>(() => 
            guard.VerifyUrlSafety("file:///C:/Windows/System32/cmd.exe"));
    }

    [Fact]
    public void BoundedPermissionStore_RestrictsApprovalsByCategory()
    {
        var store = new BoundedPermissionStore();
        var workflowId = "wf-123";

        var nav = new AutomationAction { Type = ActionType.Navigate, Value = "https://example.com" };
        var type = new AutomationAction { Type = ActionType.Type, Value = "Secret" };

        Assert.Equal(PermissionCategory.Navigation, BoundedPermissionStore.GetCategory(nav));
        Assert.Equal(PermissionCategory.Interaction, BoundedPermissionStore.GetCategory(type));

        // Record permission for navigation
        store.RecordPermission(workflowId, nav, ActionPermission.AutoApproved);

        // Navigation should now be approved
        Assert.Equal(ActionPermission.AutoApproved, store.CheckPermission(workflowId, nav));

        // Interaction should still be pending
        Assert.Equal(ActionPermission.Pending, store.CheckPermission(workflowId, type));
    }

    [Fact]
    public void BoundedPermissionStore_DoesNotAuthorizeDestructiveActions()
    {
        var store = new BoundedPermissionStore();
        var workflowId = "wf-123";

        var deleteFile = new AutomationAction 
        { 
            Type = ActionType.Click, 
            Description = "Delete reports directory" 
        };

        Assert.Equal(PermissionCategory.Destructive, BoundedPermissionStore.GetCategory(deleteFile));

        // Attempting to store auto-approved for destructive action should downgrade to Pending
        store.RecordPermission(workflowId, deleteFile, ActionPermission.AutoApproved);

        Assert.Equal(ActionPermission.Pending, store.CheckPermission(workflowId, deleteFile));
    }
}
