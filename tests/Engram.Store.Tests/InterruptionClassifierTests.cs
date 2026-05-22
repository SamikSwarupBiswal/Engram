using System;
using System.Collections.Generic;
using Engram.Store.Automation;
using Engram.Store.Events;
using Xunit;

namespace Engram.Store.Tests;

public class InterruptionClassifierTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();
    private readonly InMemoryEventBus _eventBus = new();
    private readonly OperationalWorldModel _worldModel;

    public InterruptionClassifierTests()
    {
        _worldModel = new OperationalWorldModel(_eventBus);
    }

    public void Dispose()
    {
        _workspace.Dispose();
    }

    [Fact]
    public void Classify_WithShortPause_ClassifiesAsTemporaryBreak()
    {
        var classifier = new InterruptionClassifier(_worldModel, _eventBus) { TimeProvider = () => new DateTimeOffset(2026, 5, 22, 12, 0, 0, TimeSpan.Zero) };
        var classification = classifier.Classify("generic-wf", TimeSpan.FromMinutes(3));

        Assert.Equal(InterruptionType.TemporaryBreak, classification.Type);
        Assert.Equal(0.95, classification.Confidence);
        Assert.Equal(0.95, classification.ResumptionLikelihood);
        Assert.Equal("WaitAndResume", classification.SuggestedAction);
    }

    [Fact]
    public void Classify_WithMediumPause_ClassifiesAsContextSwitch()
    {
        var classifier = new InterruptionClassifier(_worldModel, _eventBus) { TimeProvider = () => new DateTimeOffset(2026, 5, 22, 12, 0, 0, TimeSpan.Zero) };
        var classification = classifier.Classify("generic-wf", TimeSpan.FromMinutes(25));

        Assert.Equal(InterruptionType.ContextSwitch, classification.Type);
        Assert.Equal(0.70, classification.Confidence);
        Assert.Equal(0.80, classification.ResumptionLikelihood);
    }

    [Fact]
    public void Classify_WithFourHourPause_ClassifiesAsUrgentInterruption()
    {
        var classifier = new InterruptionClassifier(_worldModel, _eventBus) { TimeProvider = () => new DateTimeOffset(2026, 5, 22, 12, 0, 0, TimeSpan.Zero) };
        var classification = classifier.Classify("generic-wf", TimeSpan.FromHours(3.5));

        Assert.Equal(InterruptionType.UrgentInterruption, classification.Type);
        Assert.Equal(0.50, classification.Confidence);
        Assert.Equal(0.60, classification.ResumptionLikelihood);
    }

    [Fact]
    public void Classify_WithTwoDayPause_ClassifiesAsFatigueBreak()
    {
        var classifier = new InterruptionClassifier(_worldModel, _eventBus) { TimeProvider = () => new DateTimeOffset(2026, 5, 22, 12, 0, 0, TimeSpan.Zero) };
        var classification = classifier.Classify("generic-wf", TimeSpan.FromHours(40));

        Assert.Equal(InterruptionType.FatigueBreak, classification.Type);
        Assert.Equal(0.75, classification.Confidence);
        Assert.Equal(0.40, classification.ResumptionLikelihood);
        Assert.Equal("AskUser", classification.SuggestedAction);
    }

    [Fact]
    public void Classify_WithTwoWeekPause_ClassifiesAsIntentionalAbandonment()
    {
        var classifier = new InterruptionClassifier(_worldModel, _eventBus) { TimeProvider = () => new DateTimeOffset(2026, 5, 22, 12, 0, 0, TimeSpan.Zero) };
        var classification = classifier.Classify("generic-wf", TimeSpan.FromDays(15));

        Assert.Equal(InterruptionType.IntentionalAbandonment, classification.Type);
        Assert.Equal(0.85, classification.Confidence);
        Assert.Equal(0.10, classification.ResumptionLikelihood);
        Assert.Equal("Abandon", classification.SuggestedAction);
    }

    [Fact]
    public void Classify_ForCodingWorkflow_AdjustsLikelihoodUpwardsForOvernightPauses()
    {
        var classifier = new InterruptionClassifier(_worldModel, _eventBus) { TimeProvider = () => new DateTimeOffset(2026, 5, 22, 12, 0, 0, TimeSpan.Zero) };
        var classification = classifier.Classify("coding-workflow", TimeSpan.FromHours(10));

        // Normal base duration for 10 hours would be FatigueBreak or UrgentInterruption,
        // but coding workflow override boosts resumption likelihood and returns TemporaryBreak
        Assert.Equal(InterruptionType.TemporaryBreak, classification.Type);
        Assert.True(classification.ResumptionLikelihood > 0.5);
        Assert.Equal("WaitAndResume", classification.SuggestedAction);
    }

    [Fact]
    public void Classify_ForTaxWorkflow_AdjustsLikelihoodDownwardsForLongPauses()
    {
        var classifier = new InterruptionClassifier(_worldModel, _eventBus) { TimeProvider = () => new DateTimeOffset(2026, 5, 22, 12, 0, 0, TimeSpan.Zero) };
        
        // Base category for 3 days is FatigueBreak/IntentionalAbandonment (depending on threshold)
        // With tax workflow, it gets downgraded aggressively
        var classification = classifier.Classify("tax-filing-wf", TimeSpan.FromDays(3));

        Assert.Equal(InterruptionType.IntentionalAbandonment, classification.Type);
        Assert.True(classification.ResumptionLikelihood < 0.2);
        Assert.Equal("AskUser", classification.SuggestedAction);
    }

    [Fact]
    public void Classify_DuringNightHours_TreatsAsTemporaryBreak()
    {
        var classifier = new InterruptionClassifier(_worldModel, _eventBus) { TimeProvider = () => new DateTimeOffset(2026, 5, 22, 23, 0, 0, TimeSpan.Zero) };

        _worldModel.UpdateState("Running", "wf", "", 0, new Dictionary<string, string>
        {
            ["high_priority_deadline"] = "True"
        });

        var classification = classifier.Classify("generic-wf", TimeSpan.FromHours(3));
        Assert.Equal(InterruptionType.TemporaryBreak, classification.Type);
        Assert.Equal("WaitAndResume", classification.SuggestedAction);
        Assert.True(classification.ResumptionLikelihood > 0.5);
    }
}
