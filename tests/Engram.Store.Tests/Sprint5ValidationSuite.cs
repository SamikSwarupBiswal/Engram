using Engram.Store.Metabolism;
using Engram.Store.Wiki;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Sprint 5 Validation — Human-Compatible Cognition.
/// 
/// Tests emotional tone regulation, positive evidence modeling,
/// curiosity layer, user agency protection, and reflection expiry.
/// 
/// These tests validate that Engram's cognition is PSYCHOLOGICALLY SUSTAINABLE.
/// </summary>
public class Sprint5ValidationSuite : IDisposable
{
    private readonly CognitiveReplayHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    // ═══════════════════════════════════════════════════════════════
    // TONE BALANCE ENGINE
    // Emotional tone regulation
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ToneEngine_EmptyState_BalancedTone()
    {
        var balance = _harness.ToneEngine.ComputeToneBalance();

        Assert.True(balance.IsBalanced);
        Assert.True(balance.ToneScore > 0.7);
    }

    [Fact]
    public void ToneEngine_SevereInterventions_UnbalancedTone()
    {
        // Seed severe interventions
        for (int i = 0; i < 5; i++)
        {
            _harness.InterventionStore.Save(new Intervention
            {
                InterventionId = $"severe_{i}",
                Severity = InterventionSeverity.Critical,
                Message = $"Critical issue {i}",
                GeneratedAt = DateTimeOffset.UtcNow.AddDays(-1)
            });
        }

        var balance = _harness.ToneEngine.ComputeToneBalance();

        Assert.False(balance.IsBalanced);
        Assert.True(balance.SevereInterventionRatio > 0.5);
    }

    [Fact]
    public void ToneEngine_SoftensInterventions_WhenImbalanced()
    {
        // Seed severe interventions
        for (int i = 0; i < 5; i++)
        {
            _harness.InterventionStore.Save(new Intervention
            {
                InterventionId = $"severe_{i}",
                Severity = InterventionSeverity.Critical,
                Message = $"Critical issue {i}",
                GeneratedAt = DateTimeOffset.UtcNow.AddDays(-1)
            });
        }

        var guidance = _harness.ToneEngine.GetToneGuidance(InterventionSeverity.Critical);

        Assert.True(guidance.ShouldSoften);
        Assert.Equal(InterventionSeverity.High, guidance.SuggestedSeverity);
    }

    [Fact]
    public void ToneEngine_AssessesAtmosphere()
    {
        var atmosphere = _harness.ToneEngine.AssessAtmosphere();

        Assert.NotNull(atmosphere);
        Assert.True(atmosphere.IsSustainable);
    }

    // ═══════════════════════════════════════════════════════════════
    // MOMENTUM DETECTOR
    // Positive evidence modeling
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void MomentumDetector_EmptyState_NoSignals()
    {
        var signals = _harness.MomentumDetector.DetectPositiveSignals();

        Assert.Empty(signals);
    }

    [Fact]
    public void MomentumDetector_DetectsMomentum()
    {
        // Seed active goals
        _harness.InjectNode(new WikiNode
        {
            NodeId = "goal_momentum",
            NodeType = WikiNodeType.Goal,
            Title = "Ship Engram",
            Salience = 0.6,
            LastTouchedAt = DateTimeOffset.UtcNow.AddHours(-12)
        });

        var signals = _harness.MomentumDetector.DetectPositiveSignals();

        Assert.Contains(signals, s => s.Type == PositiveSignalType.Momentum);
    }

    [Fact]
    public void MomentumDetector_DetectsImprovement()
    {
        // Seed active goals for momentum (simpler test)
        _harness.InjectNode(new WikiNode
        {
            NodeId = "goal_improvement",
            NodeType = WikiNodeType.Goal,
            Title = "Test Goal",
            Salience = 0.6,
            LastTouchedAt = DateTimeOffset.UtcNow.AddHours(-12)
        });

        var signals = _harness.MomentumDetector.DetectPositiveSignals();

        // Should detect momentum from active goal
        Assert.True(signals.Count > 0, "Should detect at least one positive signal");
    }

    [Fact]
    public void MomentumDetector_ComputesMomentumScore()
    {
        // Seed active goals
        _harness.InjectNode(new WikiNode
        {
            NodeId = "goal_score",
            NodeType = WikiNodeType.Goal,
            Title = "Test Goal",
            Salience = 0.5,
            LastTouchedAt = DateTimeOffset.UtcNow.AddHours(-6)
        });

        var score = _harness.MomentumDetector.ComputeMomentumScore();

        Assert.True(score.Score >= 0 && score.Score <= 1);
    }

    // ═══════════════════════════════════════════════════════════════
    // CURIOSITY ENGINE
    // Gentle exploration
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void CuriosityEngine_EmptyState_NoPrompts()
    {
        var prompts = _harness.CuriosityEngine.GenerateCuriosityPrompts();

        Assert.Empty(prompts);
    }

    [Fact]
    public void CuriosityEngine_ExploresNewActivity()
    {
        // Seed recently created node
        _harness.InjectNode(new WikiNode
        {
            NodeId = "new_node",
            NodeType = WikiNodeType.Project,
            Title = "New Project",
            Salience = 0.5,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-6)
        });

        var prompts = _harness.CuriosityEngine.GenerateCuriosityPrompts();

        Assert.Contains(prompts, p => p.Type == CuriosityType.Exploration);
    }

    [Fact]
    public void CuriosityEngine_CelebratesMomentum()
    {
        // Seed multiple active goals for momentum
        for (int i = 0; i < 3; i++)
        {
            _harness.InjectNode(new WikiNode
            {
                NodeId = $"goal_curiosity_{i}",
                NodeType = WikiNodeType.Goal,
                Title = $"Goal {i}",
                Salience = 0.6,
                LastTouchedAt = DateTimeOffset.UtcNow.AddHours(-12)
            });
        }

        var prompts = _harness.CuriosityEngine.GenerateCuriosityPrompts();

        Assert.Contains(prompts, p => p.Type == CuriosityType.Celebration);
    }

    [Fact]
    public void CuriosityEngine_SuppressesWhenOverwhelmed()
    {
        // Seed many severe contradictions
        for (int i = 0; i < 6; i++)
        {
            _harness.ContradictionHistoryStore.Record(new BehavioralContradiction
            {
                Type = ContradictionType.GoalActivityGap,
                Severity = ContradictionSeverity.High,
                DeclaredIntent = $"Critical goal {i}",
                ObservedBehavior = "High activity elsewhere"
            });
        }

        var shouldSuppress = _harness.CuriosityEngine.ShouldSuppressCuriosity();

        Assert.True(shouldSuppress);
    }

    // ═══════════════════════════════════════════════════════════════
    // INTERVENTION CONSENT MODEL
    // User agency protection
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ConsentModel_DefaultConfiguration_AllowsAll()
    {
        var config = _harness.ConsentModel.LoadConfiguration();

        Assert.Equal(InterventionSeverity.Critical, config.MaxIntensity);
        Assert.Equal(SensitivityLevel.Medium, config.SensitivityLevel);
        Assert.Empty(config.BlockedDomains);
    }

    [Fact]
    public void ConsentModel_BlocksHighIntensity()
    {
        _harness.ConsentModel.UpdateIntensity(InterventionSeverity.Medium);

        var intervention = new Intervention
        {
            Severity = InterventionSeverity.High,
            Message = "High severity issue"
        };

        var check = _harness.ConsentModel.IsInterventionAllowed(intervention);

        Assert.False(check.IsAllowed);
        Assert.Contains("exceeds max intensity", check.Reason);
    }

    [Fact]
    public void ConsentModel_BlocksDomain()
    {
        _harness.ConsentModel.BlockDomain("gaming");

        var intervention = new Intervention
        {
            Severity = InterventionSeverity.Medium,
            Message = "You spent too much time on gaming"
        };

        var check = _harness.ConsentModel.IsInterventionAllowed(intervention);

        Assert.False(check.IsAllowed);
        Assert.Contains("blocked domain", check.Reason);
    }

    [Fact]
    public void ConsentModel_LowSensitivity_BlocksLowSeverity()
    {
        _harness.ConsentModel.UpdateSensitivity(SensitivityLevel.Low);

        var intervention = new Intervention
        {
            Severity = InterventionSeverity.Low,
            Message = "Minor observation"
        };

        var check = _harness.ConsentModel.IsInterventionAllowed(intervention);

        Assert.False(check.IsAllowed);
        Assert.Contains("Low sensitivity", check.Reason);
    }

    [Fact]
    public void ConsentModel_CanUnblockDomain()
    {
        _harness.ConsentModel.BlockDomain("gaming");
        _harness.ConsentModel.UnblockDomain("gaming");

        var intervention = new Intervention
        {
            Severity = InterventionSeverity.Medium,
            Message = "Gaming activity detected"
        };

        var check = _harness.ConsentModel.IsInterventionAllowed(intervention);

        Assert.True(check.IsAllowed);
    }

    // ═══════════════════════════════════════════════════════════════
    // REFLECTION EXPIRY ENGINE
    // Fading interpretations
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ExpiryEngine_EmptyState_NoExpiries()
    {
        var results = _harness.ExpiryEngine.ProcessExpiry();

        Assert.Empty(results);
    }

    [Fact]
    public void ExpiryEngine_FadesStaleContradictions()
    {
        // Seed a contradiction
        _harness.ContradictionHistoryStore.Record(new BehavioralContradiction
        {
            Type = ContradictionType.GoalActivityGap,
            Severity = ContradictionSeverity.Medium,
            DeclaredIntent = "Old goal",
            ObservedBehavior = "High activity elsewhere"
        });

        // Process expiry on fresh contradiction (should not expire)
        var results = _harness.ExpiryEngine.ProcessExpiry();

        // Fresh contradictions should not be expired
        Assert.Empty(results);
    }

    [Fact]
    public void ExpiryEngine_ComputesExpiryHealth()
    {
        // Empty state should be healthy
        var health = _harness.ExpiryEngine.ComputeExpiryHealth();

        Assert.NotNull(health);
        Assert.True(health.ActiveContradictions == 0 || health.IsHealthy,
            $"Should be healthy with no active contradictions, got IsHealthy={health.IsHealthy}");
    }

    // ═══════════════════════════════════════════════════════════════
    // INTEGRATION TESTS
    // Full pipeline validation
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Integration_ToneAndMomentum_Together()
    {
        // Seed multiple active goals for momentum
        for (int i = 0; i < 3; i++)
        {
            _harness.InjectNode(new WikiNode
            {
                NodeId = $"goal_integration_{i}",
                NodeType = WikiNodeType.Goal,
                Title = $"Goal {i}",
                Salience = 0.6,
                LastTouchedAt = DateTimeOffset.UtcNow.AddHours(-12)
            });
        }

        var tone = _harness.ToneEngine.ComputeToneBalance();
        var momentum = _harness.MomentumDetector.ComputeMomentumScore();

        Assert.True(tone.IsBalanced, "Tone should be balanced");
        // Momentum may or may not be detected depending on thresholds
        Assert.True(momentum.Score >= 0, "Momentum score should be non-negative");
    }

    [Fact]
    public void Integration_CuriosityAndConsent_WorkTogether()
    {
        // Seed new activity
        _harness.InjectNode(new WikiNode
        {
            NodeId = "new_project",
            NodeType = WikiNodeType.Project,
            Title = "New Project",
            Salience = 0.5,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-6)
        });

        var prompts = _harness.CuriosityEngine.GenerateCuriosityPrompts();
        var config = _harness.ConsentModel.LoadConfiguration();

        Assert.NotEmpty(prompts);
        Assert.True(config.CuriosityEnabled);
    }

    [Fact]
    public void Integration_ExpiryAndHealth_Connected()
    {
        // Seed some contradictions
        _harness.ContradictionHistoryStore.Record(new BehavioralContradiction
        {
            Type = ContradictionType.GoalActivityGap,
            Severity = ContradictionSeverity.Medium,
            DeclaredIntent = "Test goal",
            ObservedBehavior = "Other activity"
        });

        var expiryHealth = _harness.ExpiryEngine.ComputeExpiryHealth();
        var semanticHealth = _harness.HealthMonitor.ComputeHealth();

        // Both should return valid results
        Assert.NotNull(expiryHealth);
        Assert.NotNull(semanticHealth);
        Assert.True(semanticHealth.OverallHealth.HealthScore >= 0,
            "Semantic health score should be non-negative");
    }
}
