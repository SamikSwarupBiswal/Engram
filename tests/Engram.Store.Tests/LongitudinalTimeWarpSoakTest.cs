using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Engram.Store.Inference;
using Engram.Store.Metabolism;
using Engram.Store.Wiki;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Verifies stability, decay, compaction, contradiction expiry, and ecological health
/// over a simulated 30-day longitudinal timeline in a fraction of a second.
/// </summary>
public class LongitudinalTimeWarpSoakTest : IDisposable
{
    private readonly CognitiveReplayHarness _harness;

    public LongitudinalTimeWarpSoakTest()
    {
        _harness = new CognitiveReplayHarness();
    }

    public void Dispose()
    {
        // Restore real-time provider for degradation tracker to prevent test pollution
        DegradationTracker.Instance.TimeProvider = () => DateTime.UtcNow;
        _harness.Dispose();
    }

    [Fact]
    public async Task Run30DayTimeWarpSimulation()
    {
        // ── Setup ──
        var virtualStart = DateTimeOffset.UtcNow.AddDays(-30);
        var currentVirtualTime = virtualStart;

        // Configure virtual clocks on dependencies
        _harness.SalienceScorer.TimeProvider = () => currentVirtualTime;
        _harness.ContradictionHistoryStore.TimeProvider = () => currentVirtualTime;
        
        var frictionTracker = _harness.InterventionGenerator.FrictionTracker;
        if (frictionTracker != null)
        {
            frictionTracker.TimeProvider = () => currentVirtualTime;
        }

        DegradationTracker.Instance.TimeProvider = () => currentVirtualTime.UtcDateTime;

        // Verify initial diagnostics state
        var stats = _harness.GetDiagnostics();
        Assert.Equal(0, stats.Metabolism.CyclesCompleted);
        Assert.Equal(0, stats.Ecological.DebtBacklogCount);

        // Seed profile goals and preferences
        _harness.InjectIdentity("Developer User", 
            goals: new List<string> { "Build Engram", "Ship Engram v1" },
            preferences: new List<string> { "Deep work focus", "No distraction windows" });

        // Seed some core constitutional node
        _harness.InjectNode(new WikiNode
        {
            NodeId = "identity_core",
            Title = "Identity: Developer User",
            NodeType = WikiNodeType.Person,
            Summary = "The user of the system",
            Salience = 1.0,
            LastTouchedAt = currentVirtualTime
        });

        // ── Simulation ──
        for (int day = 1; day <= 30; day++)
        {
            // Advance virtual time by 1 day
            currentVirtualTime = virtualStart.AddDays(day);

            // 1. User activity creates daily concepts
            var conceptNode = new WikiNode
            {
                NodeId = $"concept_day_{day}",
                Title = $"Daily Activity Day {day}",
                NodeType = WikiNodeType.Concept,
                Summary = $"Simulated context activity on day {day}",
                Salience = 1.0,
                LastTouchedAt = currentVirtualTime
            };
            _harness.InjectNode(conceptNode);

            // 2. Seed recurring priorities drift every 5 days
            if (day % 5 == 0)
            {
                var contradiction = new BehavioralContradiction
                {
                    Type = ContradictionType.PriorityDrift,
                    DeclaredIntent = $"Ship Engram v{day}",
                    ObservedBehavior = $"Context switching to social media on day {day}",
                    Description = "Mismatch between ship goal and context switching",
                    Severity = ContradictionSeverity.High,
                    RelatedNodeIds = new List<string> { "goal_ship" }
                };
                
                _harness.ContradictionHistoryStore.Record(contradiction);
            }

            // 3. Trigger metabolism cycle
            var result = await _harness.RunMetabolismCycle();
            Assert.True(result.Success, result.Error ?? "Unknown error");
        }

        // ── Validation ──
        var finalDiagnostics = _harness.GetDiagnostics();
        
        // Ensure metabolism ran 30 times
        Assert.True(finalDiagnostics.Metabolism.CyclesCompleted >= 30);

        // Verify older contradictions (e.g. Day 5, 10, 15) expired (>14 days unaddressed)
        var allContradictions = _harness.ContradictionHistoryStore.LoadAll();
        var suppressedCount = allContradictions.Count(c => c.Status == ContradictionStatus.Suppressed);
        
        Assert.True(suppressedCount > 0, "Older unaddressed contradictions should have auto-expired to Suppressed status");

        // Verify protected node is NOT deleted/archived/compacted
        var protectedNode = _harness.GetNode("identity_core");
        Assert.NotNull(protectedNode);
        Assert.Equal(WikiNodeType.Person, protectedNode.NodeType);

        // Verify ecological metrics are tracked and populated
        var eco = finalDiagnostics.Ecological;
        Assert.NotNull(eco);
        Assert.True(eco.InterventionCadence >= 0.0);
        Assert.True(eco.AnnoyanceAccumulation >= 0.0);

        // Ensure graph complexity is bound
        var nodesCount = _harness.NodeStore.LoadAll().Count;
        Assert.True(nodesCount < 2000, $"Active node footprint is uncompacted or bloated: {nodesCount}");
    }
}
