using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Engram.Store.Events;
using Engram.Store.Governance;
using Engram.Store.Metabolism;
using Engram.Store.Automation;
using Engram.Store.Identity;
using Xunit;

namespace Engram.Store.Tests;

public class HumanCoexistenceValidationTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();
    private readonly IdentityStore _identityStore;
    private readonly InMemoryEventBus _eventBus;

    public HumanCoexistenceValidationTests()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        _identityStore = new IdentityStore(_workspace.Paths);
        _eventBus = new InMemoryEventBus();
    }

    public void Dispose()
    {
        _eventBus.Dispose();
        _identityStore.Dispose();
        _workspace.Dispose();
    }

    // ─── Friction Tracker Tests ───

    [Fact]
    public void FrictionTracker_SilencesSystem_WhenFrictionExceedsThreshold()
    {
        // Arrange
        var config = new GovernanceConfig();
        var trustModel = new LongitudinalTrustModel(_workspace.Paths);
        using var tracker = new FrictionTracker(config, trustModel, _eventBus);

        Assert.False(tracker.IsSilenced);
        double initialConfidence = config.MinConfidenceToEscalate;

        // Act - Publish 4 friction events (threshold is >3 in 6 hours)
        for (int i = 0; i < 4; i++)
        {
            _eventBus.Publish(new EventEnvelope
            {
                EventType = EventTypes.FrictionUserDismissed,
                Source = "ui",
                Metadata = new Dictionary<string, string> { ["intensity"] = "1.0" }
            });
        }

        // Assert
        Assert.True(tracker.IsSilenced);
        Assert.Equal(0.95, config.MinConfidenceToEscalate);
        Assert.True(tracker.SilencedUntil > DateTimeOffset.UtcNow);

        // Act 2 - Record success resets friction
        tracker.RecordSuccess();

        // Assert 2
        Assert.False(tracker.IsSilenced);
        Assert.True(config.MinConfidenceToEscalate < 0.95);
    }

    // ─── Yield-to-Focus (Multitasking & Cognitive Debt) Tests ───

    [Fact]
    public void YieldToFocus_QueuesInterventionsInCognitiveDebt_WhenMultitaskingVelocityIsHigh()
    {
        // Arrange
        var config = new GovernanceConfig();
        var trustModel = new LongitudinalTrustModel(_workspace.Paths);
        using var tracker = new FrictionTracker(config, trustModel, _eventBus);
        var restraint = new CognitiveRestraintEngine(new RestraintPolicy
        {
            MaxMultitaskingVelocity = 5,
            MinSilenceBetweenInterventions = TimeSpan.Zero // Allow immediate interventions for testing
        });

        var generator = new InterventionGenerator(_identityStore, _eventBus, restraint, tracker);

        // Act - Generate high multitasking switch rate (6 window changes in last 2 minutes)
        for (int i = 0; i < 6; i++)
        {
            _eventBus.Publish(new EventEnvelope
            {
                EventType = "perception.active_window_changed",
                Source = "tracker"
            });
        }

        Assert.True(generator.GetCurrentMultitaskingVelocity() >= 6);

        // Try generating an intervention
        var contradictions = new List<BehavioralContradiction>
        {
            new()
            {
                Type = ContradictionType.GoalActivityGap,
                Severity = ContradictionSeverity.Medium,
                Description = "Goal fading description",
                DeclaredIntent = "Work on project",
                ObservedBehavior = "Browsing social media",
                RelatedNodeIds = new List<string> { "project_node" }
            }
        };

        var immediateInterventions = generator.GenerateInterventions(contradictions);

        // Assert - Suppressed and queued to debt, not returned/dispatched immediately
        Assert.Empty(immediateInterventions);
        var debt = generator.GetCognitiveDebt();
        Assert.Single(debt);
        Assert.Equal("Work on project", debt[0].DeclaredIntent);

        // Act 2 - User goes idle, flushes debt
        List<EventEnvelope> generatedEvents = new();
        _eventBus.Subscribe("intervention.generated", e => generatedEvents.Add(e));

        _eventBus.Publish(new EventEnvelope
        {
            EventType = "perception.idle_transition",
            Source = "tracker"
        });

        // Assert 2 - Debt flushed and fired as events
        Assert.Empty(generator.GetCognitiveDebt());
        Assert.Single(generatedEvents);
        var payload = generatedEvents[0].Payload as Intervention;
        Assert.NotNull(payload);
        Assert.Equal("Work on project", payload.DeclaredIntent);
    }

    // ─── Capability Surface Warmups & Trust Regression Tests ───

    [Fact]
    public void PermissionGate_WarmupAndRegression_FlowsCorrectly()
    {
        // Arrange
        var gate = new PermissionGate(_workspace.Paths);
        var action = new AutomationAction
        {
            Type = ActionType.Click,
            Target = new ActionTarget { Selector = ".submit-button" },
            Description = "Click submit button"
        };

        // Act & Assert 1 - Warmup < 10 requires approval
        Assert.Equal(ActionPermission.Pending, gate.CheckPermission(action));

        // Act 2 - Run success 10 times to warm up capability
        for (int i = 0; i < 10; i++)
        {
            gate.RecordSuccess(action);
        }

        // Assert 2 - Graduated to AutoApproved
        Assert.Equal(10, gate.GetWarmupCount(CapabilityFingerprint.Compute(action)));
        Assert.Equal(ActionPermission.AutoApproved, gate.CheckPermission(action));

        // Act 3 - Normal failure triggers regression (decrements count by 2)
        gate.RecordFailure(action, wasCancelled: false);

        // Assert 3 - Count drops to 8, requires approval again
        Assert.Equal(8, gate.GetWarmupCount(CapabilityFingerprint.Compute(action)));
        Assert.Equal(ActionPermission.Pending, gate.CheckPermission(action));

        // Warm back up
        gate.RecordSuccess(action);
        gate.RecordSuccess(action);
        Assert.Equal(ActionPermission.AutoApproved, gate.CheckPermission(action));

        // Act 4 - User cancellation triggers absolute trust regression (resets to 0)
        gate.RecordFailure(action, wasCancelled: true);

        // Assert 4 - Count drops to 0, requires approval
        Assert.Equal(0, gate.GetWarmupCount(CapabilityFingerprint.Compute(action)));
        Assert.Equal(ActionPermission.Pending, gate.CheckPermission(action));
    }
}
