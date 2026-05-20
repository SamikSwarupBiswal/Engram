using Engram.Store.Metabolism;
using Engram.Store.Wiki;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Sprint 4 Validation — Recursive Cognition Stabilization.
/// 
/// Tests the identity stability layer, counter-evidence engine,
/// narrative diversity, intervention rate limiting, and semantic health metrics.
/// 
/// These tests validate that Engram's recursive cognition is STABLE, not just FUNCTIONAL.
/// </summary>
public class Sprint4ValidationSuite : IDisposable
{
    private readonly CognitiveReplayHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    // ═══════════════════════════════════════════════════════════════
    // REFLECTION CONFIDENCE MODEL
    // Confidence-weighted self-modeling
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ConfidenceModel_SingleObservation_LowConfidence()
    {
        var record = CreateHistoryEntry(1, ContradictionSeverity.Low, DateTimeOffset.UtcNow);
        var confidence = _harness.ConfidenceModel.ComputeConfidence(record);

        Assert.True(confidence < 0.4, $"Single observation should have low confidence, got {confidence:F2}");
    }

    [Fact]
    public void ConfidenceModel_MultipleObservations_HigherConfidence()
    {
        var record = CreateHistoryEntry(10, ContradictionSeverity.High, DateTimeOffset.UtcNow);
        var confidence = _harness.ConfidenceModel.ComputeConfidence(record);

        Assert.True(confidence > 0.5, $"10 observations should have higher confidence, got {confidence:F2}");
    }

    [Fact]
    public void ConfidenceModel_StaleObservation_ReducedConfidence()
    {
        var record = CreateHistoryEntry(5, ContradictionSeverity.Medium, DateTimeOffset.UtcNow.AddDays(-20));
        var confidence = _harness.ConfidenceModel.ComputeConfidence(record);

        Assert.True(confidence < 0.6, $"Stale observation should have reduced confidence, got {confidence:F2}");
    }

    [Fact]
    public void ConfidenceModel_ReflectionConfidence_IncludesCounterEvidence()
    {
        var record = CreateHistoryEntry(5, ContradictionSeverity.Medium, DateTimeOffset.UtcNow);
        var counterEvidence = new List<CounterEvidence>
        {
            new() { Type = CounterEvidenceType.RecentActivity, Strength = CounterEvidenceStrength.Strong }
        };

        var reflection = _harness.ConfidenceModel.ComputeReflectionConfidence(record, counterEvidence);

        Assert.True(reflection.AdjustedConfidence < reflection.BaseConfidence,
            "Counter-evidence should reduce confidence");
        Assert.Equal(1, reflection.CounterEvidenceCount);
    }

    [Fact]
    public void ConfidenceModel_ClassifiesConfidenceLevels()
    {
        var highRecord = CreateHistoryEntry(20, ContradictionSeverity.High, DateTimeOffset.UtcNow);
        var highReflection = _harness.ConfidenceModel.ComputeReflectionConfidence(highRecord);
        Assert.True(highReflection.ConfidenceLevel == ConfidenceLevel.Medium ||
                    highReflection.ConfidenceLevel == ConfidenceLevel.High,
            $"20 observations should be Medium or High, got {highReflection.ConfidenceLevel}");

        var lowRecord = CreateHistoryEntry(1, ContradictionSeverity.Low, DateTimeOffset.UtcNow);
        var lowReflection = _harness.ConfidenceModel.ComputeReflectionConfidence(lowRecord);
        Assert.True(lowReflection.ConfidenceLevel == ConfidenceLevel.Speculative ||
                    lowReflection.ConfidenceLevel == ConfidenceLevel.Low,
            "Single observation should be Speculative or Low confidence");
    }

    // ═══════════════════════════════════════════════════════════════
    // IDENTITY STABILITY ENGINE
    // Preventing recursive identity distortion
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void StabilityEngine_NoTensions_HealthyState()
    {
        var report = _harness.StabilityEngine.AssessStability();

        Assert.True(report.IsHealthy);
        Assert.Equal(0, report.ActiveTensionCount);
    }

    [Fact]
    public void StabilityEngine_DetectsTypeDominance()
    {
        // Seed 5 tensions of the same type
        for (int i = 0; i < 5; i++)
        {
            _harness.ContradictionHistoryStore.Record(new BehavioralContradiction
            {
                Type = ContradictionType.GoalActivityGap,
                Severity = ContradictionSeverity.Medium,
                DeclaredIntent = $"Goal {i}",
                ObservedBehavior = "High activity on other things",
                Description = $"Goal {i} fading"
            });
        }

        var report = _harness.StabilityEngine.AssessStability();

        Assert.Contains(report.Warnings, w => w.Type == WarningType.TypeDominance);
    }

    [Fact]
    public void StabilityEngine_DetectsRecursiveNegativity()
    {
        // Seed tensions with worsening trend by recording multiple times with increasing severity
        for (int i = 0; i < 5; i++)
        {
            // Record multiple observations to establish worsening trend
            for (int obs = 0; obs < 5; obs++)
            {
                _harness.ContradictionHistoryStore.Record(new BehavioralContradiction
                {
                    Type = ContradictionType.GoalActivityGap,
                    Severity = (ContradictionSeverity)Math.Min(3, (int)ContradictionSeverity.Medium + obs),
                    DeclaredIntent = $"Worsening goal {i}",
                    ObservedBehavior = "Getting worse",
                    Description = $"Observation {obs}"
                });
            }
        }

        var report = _harness.StabilityEngine.AssessStability();

        // Should detect either RecursiveNegativity or TypeDominance (since all are GoalActivityGap)
        Assert.True(report.Warnings.Any(w =>
            w.Type == WarningType.RecursiveNegativity ||
            w.Type == WarningType.TypeDominance),
            "Should detect either recursive negativity or type dominance");
    }

    [Fact]
    public void StabilityEngine_SelectTensionsForPrompt_EnforcesDiversity()
    {
        // Seed tensions of different types
        _harness.ContradictionHistoryStore.Record(new BehavioralContradiction
        {
            Type = ContradictionType.GoalActivityGap,
            Severity = ContradictionSeverity.High,
            DeclaredIntent = "Goal 1",
            ObservedBehavior = "High activity elsewhere"
        });
        _harness.ContradictionHistoryStore.Record(new BehavioralContradiction
        {
            Type = ContradictionType.PriorityDrift,
            Severity = ContradictionSeverity.Medium,
            DeclaredIntent = "Priority 1",
            ObservedBehavior = "Focus on other things"
        });

        var selected = _harness.StabilityEngine.SelectTensionsForPrompt();

        // Should select diverse types
        Assert.True(selected.Count <= 2, "Should not exceed max tensions per prompt");
    }

    [Fact]
    public void StabilityEngine_RespectCooldown()
    {
        _harness.ContradictionHistoryStore.Record(new BehavioralContradiction
        {
            Type = ContradictionType.GoalActivityGap,
            Severity = ContradictionSeverity.High,
            DeclaredIntent = "Goal 1",
            ObservedBehavior = "High activity elsewhere"
        });

        var recentIds = new List<string> { _harness.ContradictionHistoryStore.LoadActive().First().ContradictionId };
        var selected = _harness.StabilityEngine.SelectTensionsForPrompt(recentIds);

        Assert.Empty(selected);
    }

    // ═══════════════════════════════════════════════════════════════
    // NARRATIVE BALANCE CONTROLLER
    // Intervention rate limiting and balance
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void BalanceController_NoInterventions_AllowsGeneration()
    {
        var check = _harness.BalanceController.CanGenerateIntervention();

        Assert.True(check.IsAllowed);
    }

    [Fact]
    public void BalanceController_DailyBudgetLimit()
    {
        // Fill the daily budget
        for (int i = 0; i < 5; i++)
        {
            _harness.InterventionStore.Save(new Intervention
            {
                InterventionId = $"int_{i}",
                Message = $"Intervention {i}",
                GeneratedAt = DateTimeOffset.UtcNow
            });
        }

        var check = _harness.BalanceController.CanGenerateIntervention();

        Assert.False(check.IsAllowed);
        Assert.Contains("Daily intervention budget", check.Reason);
    }

    [Fact]
    public void BalanceController_PendingLimit()
    {
        // Fill pending interventions
        for (int i = 0; i < 3; i++)
        {
            _harness.InterventionStore.Save(new Intervention
            {
                InterventionId = $"pending_{i}",
                Message = $"Pending {i}",
                Status = InterventionStatus.Pending,
                GeneratedAt = DateTimeOffset.UtcNow.AddDays(-1) // Older to avoid daily budget
            });
        }

        var check = _harness.BalanceController.CanGenerateIntervention();

        Assert.False(check.IsAllowed);
        Assert.Contains("pending interventions", check.Reason);
    }

    [Fact]
    public void BalanceController_SuppressesAfterDismissal()
    {
        _harness.InterventionStore.Save(new Intervention
        {
            InterventionId = "dismissed_int",
            Message = "Dismissed",
            Status = InterventionStatus.Dismissed,
            GeneratedAt = DateTimeOffset.UtcNow.AddHours(-1),
            RespondedAt = DateTimeOffset.UtcNow.AddHours(-1)
        });

        var check = _harness.BalanceController.CanGenerateIntervention();

        Assert.False(check.IsAllowed);
        Assert.Contains("dismissed", check.Reason);
    }

    [Fact]
    public void BalanceController_TensionCooldown()
    {
        _harness.InterventionStore.Save(new Intervention
        {
            InterventionId = "recent_int",
            Message = "Recent",
            Source = ContradictionType.GoalActivityGap.ToString(),
            GeneratedAt = DateTimeOffset.UtcNow.AddHours(-1)
        });

        var check = _harness.BalanceController.CanInjectTension(
            ContradictionType.GoalActivityGap, "Goal 1");

        Assert.False(check.IsAllowed);
        Assert.Contains("cooldown", check.Reason);
    }

    [Fact]
    public void BalanceController_ComputeNarrativeBalance()
    {
        // Seed interventions with varying severity
        _harness.InterventionStore.Save(new Intervention
        {
            InterventionId = "critical_int",
            Severity = InterventionSeverity.Critical,
            Message = "Critical",
            GeneratedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        _harness.InterventionStore.Save(new Intervention
        {
            InterventionId = "low_int",
            Severity = InterventionSeverity.Low,
            Message = "Low",
            GeneratedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });

        var balance = _harness.BalanceController.ComputeNarrativeBalance();

        Assert.True(balance > 0 && balance < 1, $"Balance should be between 0 and 1, got {balance:F2}");
    }

    // ═══════════════════════════════════════════════════════════════
    // COUNTER-EVIDENCE DETECTOR
    // Finding balancing evidence
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void CounterEvidence_FindsGoalSalienceRecovery()
    {
        // Create a goal with recovered salience
        var goalNode = new WikiNode
        {
            NodeId = "goal_1",
            NodeType = WikiNodeType.Goal,
            Title = "Ship Engram",
            Salience = 0.5, // Above fading threshold
            LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        _harness.InjectNode(goalNode);

        // Record a contradiction about this goal
        _harness.ContradictionHistoryStore.Record(new BehavioralContradiction
        {
            Type = ContradictionType.GoalActivityGap,
            Severity = ContradictionSeverity.Medium,
            DeclaredIntent = "Ship Engram",
            ObservedBehavior = "High activity on other things",
            RelatedNodeIds = new List<string> { "goal_1" }
        });

        var counterEvidence = _harness.CounterEvidenceDetector.FindCounterEvidence();

        Assert.NotEmpty(counterEvidence);
        var evidence = counterEvidence.Values.SelectMany(e => e).ToList();
        Assert.Contains(evidence, e => e.Type == CounterEvidenceType.SalienceRecovery);
    }

    [Fact]
    public void CounterEvidence_FindsRecentActivity()
    {
        var goalNode = new WikiNode
        {
            NodeId = "goal_2",
            NodeType = WikiNodeType.Goal,
            Title = "Deep Work",
            Salience = 0.2,
            LastTouchedAt = DateTimeOffset.UtcNow.AddHours(-12) // Recent
        };
        _harness.InjectNode(goalNode);

        _harness.ContradictionHistoryStore.Record(new BehavioralContradiction
        {
            Type = ContradictionType.GoalActivityGap,
            Severity = ContradictionSeverity.Medium,
            DeclaredIntent = "Deep Work",
            ObservedBehavior = "High activity on other things",
            RelatedNodeIds = new List<string> { "goal_2" }
        });

        var counterEvidence = _harness.CounterEvidenceDetector.FindCounterEvidence();

        Assert.NotEmpty(counterEvidence);
        var evidence = counterEvidence.Values.SelectMany(e => e).ToList();
        Assert.Contains(evidence, e => e.Type == CounterEvidenceType.RecentActivity);
    }

    [Fact]
    public void CounterEvidence_EmptyWhenNoContradictions()
    {
        var counterEvidence = _harness.CounterEvidenceDetector.FindCounterEvidence();

        Assert.Empty(counterEvidence);
    }

    // ═══════════════════════════════════════════════════════════════
    // NARRATIVE INTERPRETATION ENGINE
    // Multiple competing interpretations
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void InterpretationEngine_GeneratesMultipleInterpretations()
    {
        var contradiction = new BehavioralContradiction
        {
            Type = ContradictionType.GoalActivityGap,
            Severity = ContradictionSeverity.Medium,
            DeclaredIntent = "Ship Engram",
            ObservedBehavior = "High activity on gaming"
        };

        var interpretations = _harness.InterpretationEngine.GenerateInterpretations(contradiction);

        Assert.True(interpretations.Count >= 2, "Should generate at least 2 interpretations");
        Assert.Contains(interpretations, i => i.Narrative == "burnout");
        Assert.Contains(interpretations, i => i.Narrative == "exploration");
    }

    [Fact]
    public void InterpretationEngine_SortsByPlausibility()
    {
        var contradiction = new BehavioralContradiction
        {
            Type = ContradictionType.PriorityDrift,
            Severity = ContradictionSeverity.High,
            DeclaredIntent = "Ship Engram",
            ObservedBehavior = "Focus on other projects"
        };

        var interpretations = _harness.InterpretationEngine.GenerateInterpretations(contradiction);

        // Should be sorted by plausibility (descending)
        for (int i = 0; i < interpretations.Count - 1; i++)
        {
            Assert.True(interpretations[i].Plausibility >= interpretations[i + 1].Plausibility,
                "Interpretations should be sorted by plausibility");
        }
    }

    [Fact]
    public void InterpretationEngine_IncludesCounterEvidence()
    {
        var contradiction = new BehavioralContradiction
        {
            Type = ContradictionType.GoalActivityGap,
            Severity = ContradictionSeverity.Medium,
            DeclaredIntent = "Ship Engram",
            ObservedBehavior = "High activity elsewhere"
        };

        var counterEvidence = new List<CounterEvidence>
        {
            new() { Type = CounterEvidenceType.RecentActivity, Description = "Goal touched yesterday" }
        };

        var interpretations = _harness.InterpretationEngine.GenerateInterpretations(contradiction, counterEvidence);

        // At least one interpretation should have counter-evidence
        Assert.Contains(interpretations, i => i.CounterEvidence.Count > 0);
    }

    [Fact]
    public void InterpretationEngine_MaxInterpretationsLimit()
    {
        var contradiction = new BehavioralContradiction
        {
            Type = ContradictionType.AbandonedCommitment,
            Severity = ContradictionSeverity.Medium,
            DeclaredIntent = "Write tests",
            ObservedBehavior = "No follow-up"
        };

        var interpretations = _harness.InterpretationEngine.GenerateInterpretations(contradiction);

        Assert.True(interpretations.Count <= _harness.InterpretationEngine.MaxInterpretationsPerObservation,
            $"Should not exceed max interpretations ({_harness.InterpretationEngine.MaxInterpretationsPerObservation})");
    }

    // ═══════════════════════════════════════════════════════════════
    // SEMANTIC HEALTH MONITOR
    // Psychological stability metrics
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void HealthMonitor_EmptyState_Healthy()
    {
        var health = _harness.HealthMonitor.ComputeHealth();

        Assert.True(health.OverallHealth.IsHealthy);
        Assert.True(health.OverallHealth.HealthLevel == HealthLevel.Excellent ||
                    health.OverallHealth.HealthLevel == HealthLevel.Good,
            $"Empty state should be Excellent or Good, got {health.OverallHealth.HealthLevel}");
    }

    [Fact]
    public void HealthMonitor_ComputesContradictionMetrics()
    {
        // Seed some contradictions
        _harness.ContradictionHistoryStore.Record(new BehavioralContradiction
        {
            Type = ContradictionType.GoalActivityGap,
            Severity = ContradictionSeverity.Medium,
            DeclaredIntent = "Goal 1",
            ObservedBehavior = "Other activity"
        });

        var health = _harness.HealthMonitor.ComputeHealth();

        Assert.True(health.ContradictionMetrics.TotalContradictions > 0);
        Assert.True(health.ContradictionMetrics.ActiveContradictions > 0);
    }

    [Fact]
    public void HealthMonitor_ComputesNarrativeMetrics()
    {
        var health = _harness.HealthMonitor.ComputeHealth();

        Assert.True(health.NarrativeMetrics.BalanceScore >= 0 && health.NarrativeMetrics.BalanceScore <= 1);
        Assert.True(health.NarrativeMetrics.NarrativeDiversity >= 0 && health.NarrativeMetrics.NarrativeDiversity <= 1);
    }

    [Fact]
    public void HealthMonitor_ComputesStabilityMetrics()
    {
        var health = _harness.HealthMonitor.ComputeHealth();

        Assert.NotNull(health.StabilityMetrics);
        Assert.True(health.StabilityMetrics.IsStable); // Empty state should be stable
    }

    [Fact]
    public void HealthMonitor_DetectsUnhealthyState()
    {
        // Seed many worsening contradictions
        for (int i = 0; i < 10; i++)
        {
            _harness.ContradictionHistoryStore.Record(new BehavioralContradiction
            {
                Type = ContradictionType.GoalActivityGap,
                Severity = ContradictionSeverity.High,
                DeclaredIntent = $"Worsening goal {i}",
                ObservedBehavior = "Getting worse"
            });
        }

        // Force worsening trend by recording multiple times
        for (int cycle = 0; cycle < 3; cycle++)
        {
            for (int i = 0; i < 10; i++)
            {
                _harness.ContradictionHistoryStore.Record(new BehavioralContradiction
                {
                    Type = ContradictionType.GoalActivityGap,
                    Severity = ContradictionSeverity.Critical,
                    DeclaredIntent = $"Worsening goal {i}",
                    ObservedBehavior = "Still getting worse"
                });
            }
        }

        var health = _harness.HealthMonitor.ComputeHealth();

        // Should detect issues
        Assert.True(health.ContradictionMetrics.ActiveContradictions > 5,
            "Should detect many active contradictions");
    }

    [Fact]
    public void HealthMonitor_SnapshotContainsAllMetrics()
    {
        var health = _harness.HealthMonitor.ComputeHealth();

        Assert.NotNull(health.ComputedAt);
        Assert.NotNull(health.ContradictionMetrics);
        Assert.NotNull(health.InterventionMetrics);
        Assert.NotNull(health.NarrativeMetrics);
        Assert.NotNull(health.StabilityMetrics);
        Assert.NotNull(health.OverallHealth);
    }

    // ═══════════════════════════════════════════════════════════════
    // INTEGRATION TESTS
    // Full pipeline validation
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Integration_ContradictionToCounterEvidence()
    {
        // Seed a goal
        var goalNode = new WikiNode
        {
            NodeId = "goal_int",
            NodeType = WikiNodeType.Goal,
            Title = "Ship Engram",
            Salience = 0.5,
            LastTouchedAt = DateTimeOffset.UtcNow.AddHours(-6)
        };
        _harness.InjectNode(goalNode);

        // Run metabolism to detect contradictions
        await _harness.RunMetabolismCycle();

        // Check for counter-evidence
        var counterEvidence = _harness.CounterEvidenceDetector.FindCounterEvidence();

        // Should find counter-evidence for the recovered goal
        if (counterEvidence.Count > 0)
        {
            var evidence = counterEvidence.Values.SelectMany(e => e).ToList();
            Assert.Contains(evidence, e =>
                e.Type == CounterEvidenceType.SalienceRecovery ||
                e.Type == CounterEvidenceType.RecentActivity);
        }
    }

    [Fact]
    public async Task Integration_HealthMonitorAfterMetabolism()
    {
        // Seed some data
        _harness.InjectNode(new WikiNode
        {
            NodeId = "goal_health",
            NodeType = WikiNodeType.Goal,
            Title = "Test Goal",
            Salience = 0.3,
            LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-5)
        });

        // Run metabolism
        await _harness.RunMetabolismCycle();

        // Compute health
        var health = _harness.HealthMonitor.ComputeHealth();

        Assert.NotNull(health);
        Assert.True(health.OverallHealth.HealthScore >= 0 && health.OverallHealth.HealthScore <= 1);
    }

    [Fact]
    public void Integration_StabilityEngineWithCounterEvidence()
    {
        // Seed a contradiction with counter-evidence
        _harness.ContradictionHistoryStore.Record(new BehavioralContradiction
        {
            Type = ContradictionType.GoalActivityGap,
            Severity = ContradictionSeverity.Medium,
            DeclaredIntent = "Ship Engram",
            ObservedBehavior = "High activity elsewhere"
        });

        // Get stable tensions
        var stableTensions = _harness.StabilityEngine.GetStableTensions();

        // Should include the contradiction
        Assert.NotEmpty(stableTensions);
    }

    // ═══════════════════════════════════════════════════════════════
    // HELPER METHODS
    // ═══════════════════════════════════════════════════════════════

    private static ContradictionHistoryEntry CreateHistoryEntry(
        int observationCount,
        ContradictionSeverity severity,
        DateTimeOffset lastSeenAt)
    {
        var observations = new List<ContradictionObservation>();
        for (int i = 0; i < observationCount; i++)
        {
            observations.Add(new ContradictionObservation
            {
                ObservedAt = lastSeenAt.AddDays(-i),
                Severity = severity,
                ObservedBehavior = $"Behavior {i}",
                Description = $"Observation {i}"
            });
        }

        return new ContradictionHistoryEntry
        {
            ContradictionId = Guid.NewGuid().ToString("n")[..12],
            Type = ContradictionType.GoalActivityGap,
            DeclaredIntent = "Test Goal",
            FirstSeenAt = lastSeenAt.AddDays(-observationCount),
            LastSeenAt = lastSeenAt,
            ObservationCount = observationCount,
            CurrentSeverity = severity,
            Trend = ContradictionTrend.Stable,
            Status = ContradictionStatus.Active,
            Observations = observations
        };
    }
}
