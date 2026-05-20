using Engram.Store.Metabolism;
using Xunit;

namespace Engram.Store.Tests;

public class CognitiveTelemetryTests
{
    private readonly CognitiveTelemetry _telemetry = new();

    // ═══════════════════════════════════════════
    // MEMORY PIPELINE METRICS
    // ═══════════════════════════════════════════

    [Fact]
    public void MemoryPipeline_InitialState_AllZero()
    {
        var metrics = _telemetry.GetMemoryPipelineMetrics();

        Assert.Equal(0, metrics.TotalInvocations);
        Assert.Equal(0, metrics.SuccessfulExtractions);
        Assert.Equal(0, metrics.FailedExtractions);
        Assert.Equal(0, metrics.TotalCandidatesExtracted);
        Assert.Equal(0, metrics.TotalNodesCreatedByPipeline);
        Assert.Equal(0, metrics.FailureRate);
    }

    [Fact]
    public void MemoryPipeline_RecordExtraction_Success_IncrementsCounters()
    {
        _telemetry.RecordExtraction(candidatesExtracted: 5, nodesCreated: 3, success: true);

        var metrics = _telemetry.GetMemoryPipelineMetrics();
        Assert.Equal(1, metrics.TotalInvocations);
        Assert.Equal(1, metrics.SuccessfulExtractions);
        Assert.Equal(0, metrics.FailedExtractions);
        Assert.Equal(5, metrics.TotalCandidatesExtracted);
        Assert.Equal(3, metrics.TotalNodesCreatedByPipeline);
    }

    [Fact]
    public void MemoryPipeline_RecordExtraction_Failure_IncrementsFailureCounter()
    {
        _telemetry.RecordExtraction(candidatesExtracted: 0, nodesCreated: 0, success: false);

        var metrics = _telemetry.GetMemoryPipelineMetrics();
        Assert.Equal(1, metrics.TotalInvocations);
        Assert.Equal(0, metrics.SuccessfulExtractions);
        Assert.Equal(1, metrics.FailedExtractions);
        Assert.Equal(1.0, metrics.FailureRate);
    }

    [Fact]
    public void MemoryPipeline_FailureRate_CalculatesCorrectly()
    {
        _telemetry.RecordExtraction(3, 2, true);
        _telemetry.RecordExtraction(0, 0, false);
        _telemetry.RecordExtraction(4, 3, true);
        _telemetry.RecordExtraction(0, 0, false);

        var metrics = _telemetry.GetMemoryPipelineMetrics();
        Assert.Equal(4, metrics.TotalInvocations);
        Assert.Equal(0.5, metrics.FailureRate);
    }

    // ═══════════════════════════════════════════
    // METABOLISM METRICS
    // ═══════════════════════════════════════════

    [Fact]
    public void Metabolism_InitialState_AllZero()
    {
        var metrics = _telemetry.GetMetabolismMetrics();

        Assert.Equal(0, metrics.CyclesCompleted);
        Assert.Equal(0, metrics.CyclesFailed);
        Assert.Null(metrics.LastCycleAt);
        Assert.Null(metrics.LastCycleDuration);
    }

    [Fact]
    public void Metabolism_RecordCycle_Success_IncrementsCounters()
    {
        var result = new MetabolismCycleResult
        {
            Success = true,
            NodesAnalyzed = 10,
            SalienceUpdated = 5,
            NodesArchived = 2,
            TensionsGenerated = 1,
            Duration = TimeSpan.FromMilliseconds(150)
        };

        _telemetry.RecordMetabolismCycle(result);

        var metrics = _telemetry.GetMetabolismMetrics();
        Assert.Equal(1, metrics.CyclesCompleted);
        Assert.Equal(0, metrics.CyclesFailed);
        Assert.NotNull(metrics.LastCycleAt);
        Assert.Equal(10, metrics.TotalNodesAnalyzed);
        Assert.Equal(5, metrics.TotalSalienceUpdates);
        Assert.Equal(2, metrics.TotalNodesArchived);
        Assert.Equal(1, metrics.TotalTensionsGenerated);
        Assert.Equal(150, metrics.AverageCycleDurationMs);
    }

    [Fact]
    public void Metabolism_RecordCycle_Failure_IncrementsFailureCounter()
    {
        var result = new MetabolismCycleResult { Success = false, Error = "test error" };
        _telemetry.RecordMetabolismCycle(result);

        var metrics = _telemetry.GetMetabolismMetrics();
        Assert.Equal(0, metrics.CyclesCompleted);
        Assert.Equal(1, metrics.CyclesFailed);
    }

    [Fact]
    public void Metabolism_AverageDuration_CalculatesCorrectly()
    {
        _telemetry.RecordMetabolismCycle(new MetabolismCycleResult { Success = true, Duration = TimeSpan.FromMilliseconds(100) });
        _telemetry.RecordMetabolismCycle(new MetabolismCycleResult { Success = true, Duration = TimeSpan.FromMilliseconds(200) });

        var metrics = _telemetry.GetMetabolismMetrics();
        Assert.Equal(150, metrics.AverageCycleDurationMs);
    }

    // ═══════════════════════════════════════════
    // DEDUPLICATION METRICS
    // ═══════════════════════════════════════════

    [Fact]
    public void Deduplication_InitialState_AllZero()
    {
        var metrics = _telemetry.GetDeduplicationMetrics();

        Assert.Equal(0, metrics.TotalRuns);
        Assert.Equal(0, metrics.TotalMerges);
        Assert.Equal(0, metrics.DuplicateRate);
    }

    [Fact]
    public void Deduplication_Record_IncrementsCounters()
    {
        var result = new DeduplicationResult
        {
            NodesAnalyzed = 20,
            MergesPerformed = 3,
            Duration = TimeSpan.FromMilliseconds(50)
        };

        _telemetry.RecordDeduplication(result);

        var metrics = _telemetry.GetDeduplicationMetrics();
        Assert.Equal(1, metrics.TotalRuns);
        Assert.Equal(3, metrics.TotalMerges);
        Assert.Equal(20, metrics.TotalNodesAnalyzed);
        Assert.Equal(50, metrics.AverageDurationMs);
        Assert.Equal(0.15, metrics.DuplicateRate);
    }

    // ═══════════════════════════════════════════
    // CONTRADICTION METRICS
    // ═══════════════════════════════════════════

    [Fact]
    public void Contradictions_InitialState_AllZero()
    {
        var metrics = _telemetry.GetContradictionMetrics();

        Assert.Equal(0, metrics.TotalDetections);
        Assert.Equal(0, metrics.TotalSemanticContradictions);
        Assert.Equal(0, metrics.TotalBehavioralContradictions);
        Assert.Equal(0, metrics.ActiveContradictions);
        Assert.Null(metrics.LastDetectedAt);
    }

    [Fact]
    public void Contradictions_Record_IncrementsCounters()
    {
        _telemetry.RecordContradictionDetection(semanticContradictions: 2, behavioralContradictions: 3);

        var metrics = _telemetry.GetContradictionMetrics();
        Assert.Equal(1, metrics.TotalDetections);
        Assert.Equal(2, metrics.TotalSemanticContradictions);
        Assert.Equal(3, metrics.TotalBehavioralContradictions);
        Assert.Equal(5, metrics.ActiveContradictions);
        Assert.NotNull(metrics.LastDetectedAt);
    }

    // ═══════════════════════════════════════════
    // INTERVENTION METRICS
    // ═══════════════════════════════════════════

    [Fact]
    public void Interventions_InitialState_AllZero()
    {
        var metrics = _telemetry.GetInterventionMetrics();

        Assert.Equal(0, metrics.TotalGenerated);
        Assert.Equal(0, metrics.TotalAcknowledged);
        Assert.Equal(0, metrics.TotalDismissed);
        Assert.Equal(0, metrics.TotalActed);
        Assert.Equal(0, metrics.PendingCount);
    }

    [Fact]
    public void Interventions_RecordGenerated_IncrementsCounters()
    {
        _telemetry.RecordInterventionGenerated("int_001");

        var metrics = _telemetry.GetInterventionMetrics();
        Assert.Equal(1, metrics.TotalGenerated);
        Assert.Equal(1, metrics.PendingCount);
        Assert.NotNull(metrics.LastGeneratedAt);
        Assert.Single(metrics.RecentInterventions);
    }

    [Fact]
    public void Interventions_RecordResponse_UpdatesStatus()
    {
        _telemetry.RecordInterventionGenerated("int_002");
        _telemetry.RecordInterventionResponse("int_002", "acknowledged");

        var metrics = _telemetry.GetInterventionMetrics();
        Assert.Equal(1, metrics.TotalAcknowledged);
        Assert.Equal(0, metrics.PendingCount);
    }

    [Fact]
    public void Interventions_Dismissed_Tracked()
    {
        _telemetry.RecordInterventionGenerated("int_003");
        _telemetry.RecordInterventionResponse("int_003", "dismissed");

        var metrics = _telemetry.GetInterventionMetrics();
        Assert.Equal(1, metrics.TotalDismissed);
    }

    [Fact]
    public void Interventions_Acted_Tracked()
    {
        _telemetry.RecordInterventionGenerated("int_004");
        _telemetry.RecordInterventionResponse("int_004", "acted");

        var metrics = _telemetry.GetInterventionMetrics();
        Assert.Equal(1, metrics.TotalActed);
    }

    // ═══════════════════════════════════════════
    // RETRIEVAL METRICS
    // ═══════════════════════════════════════════

    [Fact]
    public void Retrieval_InitialState_AllZero()
    {
        var metrics = _telemetry.GetRetrievalMetrics();

        Assert.Equal(0, metrics.TotalRequests);
        Assert.Equal(0, metrics.Hits);
        Assert.Equal(0, metrics.Misses);
        Assert.Equal(0, metrics.HitRate);
    }

    [Fact]
    public void Retrieval_RecordHit_IncrementsCounters()
    {
        _telemetry.RecordRetrieval(nodesInjected: 5, tokensUsed: 800, hit: true);

        var metrics = _telemetry.GetRetrievalMetrics();
        Assert.Equal(1, metrics.TotalRequests);
        Assert.Equal(1, metrics.Hits);
        Assert.Equal(0, metrics.Misses);
        Assert.Equal(1.0, metrics.HitRate);
        Assert.Equal(5, metrics.TotalNodesInjected);
        Assert.Equal(800, metrics.TotalTokensUsed);
    }

    [Fact]
    public void Retrieval_RecordMiss_IncrementsMissCounter()
    {
        _telemetry.RecordRetrieval(nodesInjected: 0, tokensUsed: 0, hit: false);

        var metrics = _telemetry.GetRetrievalMetrics();
        Assert.Equal(1, metrics.Misses);
        Assert.Equal(0, metrics.HitRate);
    }

    [Fact]
    public void Retrieval_HitRate_CalculatesCorrectly()
    {
        _telemetry.RecordRetrieval(3, 500, true);
        _telemetry.RecordRetrieval(0, 0, false);
        _telemetry.RecordRetrieval(2, 300, true);
        _telemetry.RecordRetrieval(0, 0, false);

        var metrics = _telemetry.GetRetrievalMetrics();
        Assert.Equal(0.5, metrics.HitRate);
    }

    [Fact]
    public void Retrieval_BudgetExceeded_Tracked()
    {
        _telemetry.RecordRetrieval(10, 2000, true, budgetExceeded: true);

        var metrics = _telemetry.GetRetrievalMetrics();
        Assert.Equal(1, metrics.BudgetExceededCount);
    }

    [Fact]
    public void Retrieval_InjectedNodes_Tracked()
    {
        _telemetry.RecordInjectedNode("node_1", "Engram", 0.95);
        _telemetry.RecordInjectedNode("node_2", "Project X", 0.80);

        var metrics = _telemetry.GetRetrievalMetrics();
        Assert.Equal(2, metrics.TopInjectedNodes.Count);
        Assert.Equal("Engram", metrics.TopInjectedNodes[0].Title);
    }

    // ═══════════════════════════════════════════
    // TIMELINE METRICS
    // ═══════════════════════════════════════════

    [Fact]
    public void Timeline_InitialState_AllZero()
    {
        var metrics = _telemetry.GetTimelineMetrics();

        Assert.Equal(0, metrics.EventsWritten);
        Assert.Equal(0, metrics.WriteFailures);
        Assert.Equal(0, metrics.EventsPublishedToBus);
    }

    [Fact]
    public void Timeline_RecordEventWritten_IncrementsCounters()
    {
        _telemetry.RecordTimelineEventWritten("wiki.node_created");
        _telemetry.RecordTimelineEventWritten("wiki.node_created");
        _telemetry.RecordTimelineEventWritten("metabolism.cycle_completed");

        var metrics = _telemetry.GetTimelineMetrics();
        Assert.Equal(3, metrics.EventsWritten);
        Assert.NotNull(metrics.LastEventAt);
        Assert.Equal(2, metrics.EventTypeCounts["wiki.node_created"]);
        Assert.Equal(1, metrics.EventTypeCounts["metabolism.cycle_completed"]);
    }

    [Fact]
    public void Timeline_RecordWriteFailure_IncrementsCounter()
    {
        _telemetry.RecordTimelineWriteFailure();

        var metrics = _telemetry.GetTimelineMetrics();
        Assert.Equal(1, metrics.WriteFailures);
    }

    [Fact]
    public void Timeline_WriteFailureRate_CalculatesCorrectly()
    {
        _telemetry.RecordTimelineEventWritten("test");
        _telemetry.RecordTimelineEventWritten("test");
        _telemetry.RecordTimelineWriteFailure();

        var metrics = _telemetry.GetTimelineMetrics();
        Assert.Equal(1.0 / 3.0, metrics.WriteFailureRate);
    }

    // ═══════════════════════════════════════════
    // AUTOMATION METRICS
    // ═══════════════════════════════════════════

    [Fact]
    public void Automation_InitialState_AllZero()
    {
        var metrics = _telemetry.GetAutomationMetrics();

        Assert.Equal(0, metrics.ActionsExecuted);
        Assert.Equal(0, metrics.VerificationsPassed);
        Assert.Equal(0, metrics.VerificationsFailed);
        Assert.Equal(0, metrics.Rollbacks);
    }

    [Fact]
    public void Automation_RecordAction_TracksCorrectly()
    {
        _telemetry.RecordAutomationAction(verified: true);
        _telemetry.RecordAutomationAction(verified: false, rolledBack: true);

        var metrics = _telemetry.GetAutomationMetrics();
        Assert.Equal(2, metrics.ActionsExecuted);
        Assert.Equal(1, metrics.VerificationsPassed);
        Assert.Equal(1, metrics.VerificationsFailed);
        Assert.Equal(1, metrics.Rollbacks);
        Assert.Equal(0.5, metrics.VerificationSuccessRate);
    }

    // ═══════════════════════════════════════════
    // PERCEPTION METRICS
    // ═══════════════════════════════════════════

    [Fact]
    public void Perception_InitialState_AllZero()
    {
        var metrics = _telemetry.GetPerceptionMetrics();

        Assert.Equal(0, metrics.OcrEventsProcessed);
        Assert.Equal(0, metrics.ActiveWindowEventsProcessed);
        Assert.Equal(0, metrics.SemanticSummariesGenerated);
    }

    [Fact]
    public void Perception_RecordEvents_IncrementsCounters()
    {
        _telemetry.RecordOcrEvent();
        _telemetry.RecordOcrEvent();
        _telemetry.RecordActiveWindowEvent();
        _telemetry.RecordSemanticSummary();

        var metrics = _telemetry.GetPerceptionMetrics();
        Assert.Equal(2, metrics.OcrEventsProcessed);
        Assert.Equal(1, metrics.ActiveWindowEventsProcessed);
        Assert.Equal(1, metrics.SemanticSummariesGenerated);
    }

    // ═══════════════════════════════════════════
    // FULL DIAGNOSTICS SNAPSHOT
    // ═══════════════════════════════════════════

    [Fact]
    public void DiagnosticsSnapshot_ContainsAllSections()
    {
        var snapshot = _telemetry.GetDiagnosticsSnapshot();

        Assert.NotNull(snapshot);
        Assert.NotNull(snapshot.MemoryPipeline);
        Assert.NotNull(snapshot.Metabolism);
        Assert.NotNull(snapshot.Deduplication);
        Assert.NotNull(snapshot.Contradictions);
        Assert.NotNull(snapshot.Interventions);
        Assert.NotNull(snapshot.Retrieval);
        Assert.NotNull(snapshot.Timeline);
        Assert.NotNull(snapshot.Automation);
        Assert.NotNull(snapshot.Perception);
        Assert.True(snapshot.Uptime >= TimeSpan.Zero);
    }

    [Fact]
    public void DiagnosticsSnapshot_AfterActivity_ReflectsCounts()
    {
        _telemetry.RecordExtraction(5, 3, true);
        _telemetry.RecordMetabolismCycle(new MetabolismCycleResult { Success = true, Duration = TimeSpan.FromMilliseconds(100) });
        _telemetry.RecordContradictionDetection(1, 2);
        _telemetry.RecordTimelineEventWritten("test");

        var snapshot = _telemetry.GetDiagnosticsSnapshot();

        Assert.Equal(5, snapshot.MemoryPipeline.TotalCandidatesExtracted);
        Assert.Equal(1, snapshot.Metabolism.CyclesCompleted);
        Assert.Equal(3, snapshot.Contradictions.TotalSemanticContradictions + snapshot.Contradictions.TotalBehavioralContradictions);
        Assert.Equal(1, snapshot.Timeline.EventsWritten);
    }

    // ═══════════════════════════════════════════
    // THREAD SAFETY
    // ═══════════════════════════════════════════

    [Fact]
    public void ConcurrentWrites_DoNotCorruptState()
    {
        var tasks = new List<Task>();
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < 100; j++)
                {
                    _telemetry.RecordExtraction(1, 1, true);
                    _telemetry.RecordTimelineEventWritten("test");
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());

        var metrics = _telemetry.GetMemoryPipelineMetrics();
        Assert.Equal(1000, metrics.TotalInvocations);

        var timeline = _telemetry.GetTimelineMetrics();
        Assert.Equal(1000, timeline.EventsWritten);
    }
}
