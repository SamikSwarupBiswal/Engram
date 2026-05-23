using System;
using System.Threading.Tasks;
using Xunit;
using Engram.Store.Governance;

namespace Engram.Store.Tests;

public class LongitudinalTrustTests
{
    [Fact]
    public void PacingController_EnforcesRateLimitClamps()
    {
        // Arrange
        using var pacing = new PacingController(maxDailyInterventions: 3);

        // Act & Assert
        Assert.Equal(3, pacing.GetAvailableTokens());
        
        Assert.True(pacing.TryAcquireIntervention());
        Assert.True(pacing.TryAcquireIntervention());
        Assert.True(pacing.TryAcquireIntervention());

        Assert.Equal(0, pacing.GetAvailableTokens());
        Assert.False(pacing.TryAcquireIntervention()); // Blocked by rate limiter
    }

    [Fact]
    public async Task OverrideExpiryManager_PrunesExpiredOverridesAndDecaysPermissions()
    {
        // Arrange
        using var workspace = new TempWorkspace();
        var trustEngine = new TrustCalibrationEngine(workspace.Paths);
        var auditLog = new ConstitutionalAuditLog(workspace.Paths);
        var stateMachine = new ConstitutionalStateMachine(workspace.Paths, auditLog);
        var expiryManager = new OverrideExpiryManager(trustEngine, stateMachine);

        // 1. Directory whitelist / permission grant check
        trustEngine.GrantPermission("file_deletion", "C:/Users/Samik/Documents", TimeSpan.FromMilliseconds(50));
        Assert.True(trustEngine.CheckPermission("file_deletion", "C:/Users/Samik/Documents"));

        // 2. Temporary safety overrides check
        expiryManager.RegisterOverride("bypass_privacy", TimeSpan.FromMilliseconds(50));
        Assert.True(expiryManager.IsOverrideActive("bypass_privacy"));

        // Act: wait for TTL expiration
        await Task.Delay(100);
        expiryManager.CheckAndExpireOverrides();

        // Assert
        Assert.False(trustEngine.CheckPermission("file_deletion", "C:/Users/Samik/Documents"));
        Assert.False(expiryManager.IsOverrideActive("bypass_privacy"));
    }

    [Fact]
    public void FrictionTracker_AdjustsSilenceThresholdsDynamically()
    {
        // Arrange
        using var workspace = new TempWorkspace();
        var config = new GovernanceConfig { MinConfidenceToEscalate = 0.7 };
        var trustModel = new LongitudinalTrustModel(workspace.Paths);
        var friction = new FrictionTracker(config, trustModel);

        Assert.Equal(0, friction.ConsecutiveFrictionCount);
        Assert.Equal(0.7, config.MinConfidenceToEscalate);

        // Act & Assert 1: record friction
        friction.RecordFriction(1.0);
        Assert.Equal(1, friction.ConsecutiveFrictionCount);
        Assert.Equal(0.75, config.MinConfidenceToEscalate, 5);
        Assert.Equal(1.0, trustModel.AnnoyanceScore);

        friction.RecordFriction(2.0);
        Assert.Equal(2, friction.ConsecutiveFrictionCount);
        Assert.Equal(0.85, config.MinConfidenceToEscalate, 5);

        // Act & Assert 2: record success restores base
        friction.RecordSuccess();
        Assert.Equal(0, friction.ConsecutiveFrictionCount);
        Assert.Equal(0.83, config.MinConfidenceToEscalate, 5);

        for (int i = 0; i < 10; i++)
        {
            friction.RecordSuccess();
        }
        Assert.Equal(0.7, config.MinConfidenceToEscalate, 5); // cannot drop below base limit (0.7)
    }
}
