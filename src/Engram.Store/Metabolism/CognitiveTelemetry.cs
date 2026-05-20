using System.Collections.Concurrent;

namespace Engram.Store.Metabolism;

/// <summary>
/// Central telemetry registry for all cognitive subsystems.
/// 
/// Without this, you're flying blind. You cannot trust intuition
/// when you have this many moving parts. You need evidence.
/// 
/// Every subsystem reports here. The diagnostics endpoint reads from here.
/// This is the single source of truth for cognitive health.
/// 
/// Thread-safe: all counters use Interlocked or ConcurrentDictionary.
/// </summary>
public class CognitiveTelemetry
{
    // ── Memory Pipeline ──
    private long _extractionCount;
    private long _extractionFailureCount;
    private long _candidatesExtracted;
    private long _nodesCreatedByPipeline;
    private long _pipelineInvocationCount;

    // ── Metabolism ──
    private long _metabolismCyclesCompleted;
    private long _metabolismCyclesFailed;
    private long _totalMetabolismDurationMs;
    private DateTimeOffset? _lastMetabolismCycleAt;
    private TimeSpan? _lastMetabolismCycleDuration;
    private long _totalNodesAnalyzed;
    private long _totalSalienceUpdates;
    private long _totalNodesArchived;
    private long _totalTensionsGenerated;

    // ── Deduplication ──
    private long _deduplicationRuns;
    private long _totalMergesPerformed;
    private long _totalNodesAnalyzedByDedup;
    private long _totalDeduplicationDurationMs;

    // ── Contradictions ──
    private long _contradictionDetections;
    private long _totalContradictionsFound;
    private long _totalBehavioralContradictionsFound;
    private long _activeContradictions;
    private DateTimeOffset? _lastContradictionDetectedAt;
    private readonly ConcurrentDictionary<string, ContradictionRecord> _contradictionHistory = new();

    // ── Interventions ──
    private long _interventionsGenerated;
    private long _interventionsAcknowledged;
    private long _interventionsDismissed;
    private long _interventionsActed;
    private DateTimeOffset? _lastInterventionAt;
    private readonly ConcurrentDictionary<string, InterventionRecord> _interventionHistory = new();

    // ── Retrieval ──
    private long _retrievalRequests;
    private long _retrievalHits;
    private long _retrievalMisses;
    private long _totalNodesInjected;
    private long _totalTokensUsed;
    private long _budgetExceededCount;
    private readonly ConcurrentQueue<TopInjectedNode> _recentInjectedNodes = new();

    // ── Timeline ──
    private long _timelineEventsWritten;
    private long _timelineWriteFailures;
    private long _timelineEventBusPublished;
    private readonly ConcurrentDictionary<string, long> _eventTypeCounts = new();
    private DateTimeOffset? _lastTimelineEventAt;

    // ── Automation ──
    private long _automationActionsExecuted;
    private long _automationVerificationsPassed;
    private long _automationVerificationsFailed;
    private long _automationRollbacks;

    // ── Perception ──
    private long _ocrEventsProcessed;
    private long _activeWindowEventsProcessed;
    private long _semanticSummariesGenerated;

    // ── Phase 7: Behavioral Reality Validation ──
    private long _perceptionSnapshotsRecorded;
    private long _perceptionReplaysPerformed;
    private long _interpretationDivergencesFound;
    private long _interpretationsCorrect;
    private long _interpretationsIncorrect;
    private long _interpretationsPartial;
    private long _overinterpretationWarnings;
    private long _humanCorrectionsRecorded;
    private long _restraintDecisionsAllowed;
    private long _restraintDecisionsSuppressed;
    private long _timelineSessionsDetected;
    private long _timelineArcsDetected;
    private long _momentumSignalsDetected;
    private long _regressionSignalsDetected;

    // ── System ──
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    // ═══════════════════════════════════════════
    // MEMORY PIPELINE
    // ═══════════════════════════════════════════

    public void RecordExtraction(int candidatesExtracted, int nodesCreated, bool success)
    {
        Interlocked.Increment(ref _pipelineInvocationCount);
        Interlocked.Add(ref _candidatesExtracted, candidatesExtracted);
        Interlocked.Add(ref _nodesCreatedByPipeline, nodesCreated);

        if (success)
            Interlocked.Increment(ref _extractionCount);
        else
            Interlocked.Increment(ref _extractionFailureCount);
    }

    public MemoryPipelineMetrics GetMemoryPipelineMetrics() => new()
    {
        TotalInvocations = Interlocked.Read(ref _pipelineInvocationCount),
        SuccessfulExtractions = Interlocked.Read(ref _extractionCount),
        FailedExtractions = Interlocked.Read(ref _extractionFailureCount),
        TotalCandidatesExtracted = Interlocked.Read(ref _candidatesExtracted),
        TotalNodesCreatedByPipeline = Interlocked.Read(ref _nodesCreatedByPipeline),
        FailureRate = _pipelineInvocationCount > 0
            ? (double)_extractionFailureCount / _pipelineInvocationCount
            : 0
    };

    // ═══════════════════════════════════════════
    // METABOLISM
    // ═══════════════════════════════════════════

    public void RecordMetabolismCycle(MetabolismCycleResult result)
    {
        if (result.Success)
        {
            Interlocked.Increment(ref _metabolismCyclesCompleted);
            Interlocked.Add(ref _totalMetabolismDurationMs, (long)result.Duration.TotalMilliseconds);
            Interlocked.Add(ref _totalNodesAnalyzed, result.NodesAnalyzed);
            Interlocked.Add(ref _totalSalienceUpdates, result.SalienceUpdated);
            Interlocked.Add(ref _totalNodesArchived, result.NodesArchived);
            Interlocked.Add(ref _totalTensionsGenerated, result.TensionsGenerated);
        }
        else
        {
            Interlocked.Increment(ref _metabolismCyclesFailed);
        }

        _lastMetabolismCycleAt = DateTimeOffset.UtcNow;
        _lastMetabolismCycleDuration = result.Duration;
    }

    public MetabolismMetrics GetMetabolismMetrics() => new()
    {
        CyclesCompleted = Interlocked.Read(ref _metabolismCyclesCompleted),
        CyclesFailed = Interlocked.Read(ref _metabolismCyclesFailed),
        LastCycleAt = _lastMetabolismCycleAt,
        LastCycleDuration = _lastMetabolismCycleDuration,
        AverageCycleDurationMs = _metabolismCyclesCompleted > 0
            ? (double)_totalMetabolismDurationMs / _metabolismCyclesCompleted
            : 0,
        TotalNodesAnalyzed = Interlocked.Read(ref _totalNodesAnalyzed),
        TotalSalienceUpdates = Interlocked.Read(ref _totalSalienceUpdates),
        TotalNodesArchived = Interlocked.Read(ref _totalNodesArchived),
        TotalTensionsGenerated = Interlocked.Read(ref _totalTensionsGenerated)
    };

    // ═══════════════════════════════════════════
    // DEDUPLICATION
    // ═══════════════════════════════════════════

    public void RecordDeduplication(DeduplicationResult result)
    {
        Interlocked.Increment(ref _deduplicationRuns);
        Interlocked.Add(ref _totalMergesPerformed, result.MergesPerformed);
        Interlocked.Add(ref _totalNodesAnalyzedByDedup, result.NodesAnalyzed);
        Interlocked.Add(ref _totalDeduplicationDurationMs, (long)result.Duration.TotalMilliseconds);
    }

    public DeduplicationMetrics GetDeduplicationMetrics() => new()
    {
        TotalRuns = Interlocked.Read(ref _deduplicationRuns),
        TotalMerges = Interlocked.Read(ref _totalMergesPerformed),
        TotalNodesAnalyzed = Interlocked.Read(ref _totalNodesAnalyzedByDedup),
        AverageDurationMs = _deduplicationRuns > 0
            ? (double)_totalDeduplicationDurationMs / _deduplicationRuns
            : 0,
        DuplicateRate = _totalNodesAnalyzedByDedup > 0
            ? (double)_totalMergesPerformed / _totalNodesAnalyzedByDedup
            : 0
    };

    // ═══════════════════════════════════════════
    // CONTRADICTIONS
    // ═══════════════════════════════════════════

    public void RecordContradictionDetection(int semanticContradictions, int behavioralContradictions)
    {
        Interlocked.Increment(ref _contradictionDetections);
        Interlocked.Add(ref _totalContradictionsFound, semanticContradictions);
        Interlocked.Add(ref _totalBehavioralContradictionsFound, behavioralContradictions);
        Interlocked.Add(ref _activeContradictions, semanticContradictions + behavioralContradictions);
        _lastContradictionDetectedAt = DateTimeOffset.UtcNow;
    }

    public void RecordContradictionResolved(string contradictionId)
    {
        _contradictionHistory.TryRemove(contradictionId, out _);
        Interlocked.Decrement(ref _activeContradictions);
    }

    public ContradictionMetrics GetContradictionMetrics() => new()
    {
        TotalDetections = Interlocked.Read(ref _contradictionDetections),
        TotalSemanticContradictions = Interlocked.Read(ref _totalContradictionsFound),
        TotalBehavioralContradictions = Interlocked.Read(ref _totalBehavioralContradictionsFound),
        ActiveContradictions = Math.Max(0, Interlocked.Read(ref _activeContradictions)),
        LastDetectedAt = _lastContradictionDetectedAt
    };

    // ═══════════════════════════════════════════
    // INTERVENTIONS
    // ═══════════════════════════════════════════

    public void RecordInterventionGenerated(string interventionId)
    {
        Interlocked.Increment(ref _interventionsGenerated);
        _lastInterventionAt = DateTimeOffset.UtcNow;
        _interventionHistory[interventionId] = new InterventionRecord
        {
            InterventionId = interventionId,
            GeneratedAt = DateTimeOffset.UtcNow,
            Status = "pending"
        };
    }

    public void RecordInterventionResponse(string interventionId, string status)
    {
        if (_interventionHistory.TryGetValue(interventionId, out var record))
        {
            record.Status = status;
            record.RespondedAt = DateTimeOffset.UtcNow;
        }

        switch (status)
        {
            case "acknowledged": Interlocked.Increment(ref _interventionsAcknowledged); break;
            case "dismissed": Interlocked.Increment(ref _interventionsDismissed); break;
            case "acted": Interlocked.Increment(ref _interventionsActed); break;
        }
    }

    public InterventionMetrics GetInterventionMetrics() => new()
    {
        TotalGenerated = Interlocked.Read(ref _interventionsGenerated),
        TotalAcknowledged = Interlocked.Read(ref _interventionsAcknowledged),
        TotalDismissed = Interlocked.Read(ref _interventionsDismissed),
        TotalActed = Interlocked.Read(ref _interventionsActed),
        LastGeneratedAt = _lastInterventionAt,
        PendingCount = _interventionHistory.Values.Count(r => r.Status == "pending"),
        RecentInterventions = _interventionHistory.Values
            .OrderByDescending(r => r.GeneratedAt)
            .Take(10)
            .ToList()
    };

    // ═══════════════════════════════════════════
    // RETRIEVAL
    // ═══════════════════════════════════════════

    public void RecordRetrieval(int nodesInjected, int tokensUsed, bool hit, bool budgetExceeded = false)
    {
        Interlocked.Increment(ref _retrievalRequests);
        Interlocked.Add(ref _totalNodesInjected, nodesInjected);
        Interlocked.Add(ref _totalTokensUsed, tokensUsed);

        if (hit)
            Interlocked.Increment(ref _retrievalHits);
        else
            Interlocked.Increment(ref _retrievalMisses);

        if (budgetExceeded)
            Interlocked.Increment(ref _budgetExceededCount);
    }

    public void RecordInjectedNode(string nodeId, string title, double score)
    {
        _recentInjectedNodes.Enqueue(new TopInjectedNode
        {
            NodeId = nodeId,
            Title = title,
            Score = score,
            InjectedAt = DateTimeOffset.UtcNow
        });

        // Keep only last 50
        while (_recentInjectedNodes.Count > 50)
            _recentInjectedNodes.TryDequeue(out _);
    }

    public RetrievalMetrics GetRetrievalMetrics() => new()
    {
        TotalRequests = Interlocked.Read(ref _retrievalRequests),
        Hits = Interlocked.Read(ref _retrievalHits),
        Misses = Interlocked.Read(ref _retrievalMisses),
        HitRate = _retrievalRequests > 0
            ? (double)_retrievalHits / _retrievalRequests
            : 0,
        TotalNodesInjected = Interlocked.Read(ref _totalNodesInjected),
        TotalTokensUsed = Interlocked.Read(ref _totalTokensUsed),
        BudgetExceededCount = Interlocked.Read(ref _budgetExceededCount),
        TopInjectedNodes = _recentInjectedNodes
            .OrderByDescending(n => n.Score)
            .Take(10)
            .ToList()
    };

    // ═══════════════════════════════════════════
    // TIMELINE
    // ═══════════════════════════════════════════

    public void RecordTimelineEventWritten(string eventType)
    {
        Interlocked.Increment(ref _timelineEventsWritten);
        _lastTimelineEventAt = DateTimeOffset.UtcNow;

        _eventTypeCounts.AddOrUpdate(eventType, 1, (_, count) => count + 1);
    }

    public void RecordTimelineWriteFailure()
    {
        Interlocked.Increment(ref _timelineWriteFailures);
    }

    public void RecordEventBusPublish()
    {
        Interlocked.Increment(ref _timelineEventBusPublished);
    }

    public TimelineMetrics GetTimelineMetrics() => new()
    {
        EventsWritten = Interlocked.Read(ref _timelineEventsWritten),
        WriteFailures = Interlocked.Read(ref _timelineWriteFailures),
        EventsPublishedToBus = Interlocked.Read(ref _timelineEventBusPublished),
        LastEventAt = _lastTimelineEventAt,
        EventTypeCounts = new Dictionary<string, long>(_eventTypeCounts),
        WriteFailureRate = _timelineEventsWritten + _timelineWriteFailures > 0
            ? (double)_timelineWriteFailures / (_timelineEventsWritten + _timelineWriteFailures)
            : 0
    };

    // ═══════════════════════════════════════════
    // AUTOMATION
    // ═══════════════════════════════════════════

    public void RecordAutomationAction(bool verified, bool rolledBack = false)
    {
        Interlocked.Increment(ref _automationActionsExecuted);

        if (verified)
            Interlocked.Increment(ref _automationVerificationsPassed);
        else
            Interlocked.Increment(ref _automationVerificationsFailed);

        if (rolledBack)
            Interlocked.Increment(ref _automationRollbacks);
    }

    public AutomationMetrics GetAutomationMetrics() => new()
    {
        ActionsExecuted = Interlocked.Read(ref _automationActionsExecuted),
        VerificationsPassed = Interlocked.Read(ref _automationVerificationsPassed),
        VerificationsFailed = Interlocked.Read(ref _automationVerificationsFailed),
        Rollbacks = Interlocked.Read(ref _automationRollbacks),
        VerificationSuccessRate = _automationActionsExecuted > 0
            ? (double)_automationVerificationsPassed / _automationActionsExecuted
            : 0
    };

    // ═══════════════════════════════════════════
    // PERCEPTION
    // ═══════════════════════════════════════════

    public void RecordOcrEvent()
    {
        Interlocked.Increment(ref _ocrEventsProcessed);
    }

    public void RecordActiveWindowEvent()
    {
        Interlocked.Increment(ref _activeWindowEventsProcessed);
    }

    public void RecordSemanticSummary()
    {
        Interlocked.Increment(ref _semanticSummariesGenerated);
    }

    public PerceptionMetrics GetPerceptionMetrics() => new()
    {
        OcrEventsProcessed = Interlocked.Read(ref _ocrEventsProcessed),
        ActiveWindowEventsProcessed = Interlocked.Read(ref _activeWindowEventsProcessed),
        SemanticSummariesGenerated = Interlocked.Read(ref _semanticSummariesGenerated)
    };

    // ═══════════════════════════════════════════
    // PHASE 7: BEHAVIORAL REALITY VALIDATION
    // ═══════════════════════════════════════════

    public void RecordPerceptionSnapshot()
    {
        Interlocked.Increment(ref _perceptionSnapshotsRecorded);
    }

    public void RecordPerceptionReplay(int divergences)
    {
        Interlocked.Increment(ref _perceptionReplaysPerformed);
        Interlocked.Add(ref _interpretationDivergencesFound, divergences);
    }

    public void RecordInterpretationOutcome(bool correct, bool partial = false)
    {
        if (correct)
            Interlocked.Increment(ref _interpretationsCorrect);
        else if (partial)
            Interlocked.Increment(ref _interpretationsPartial);
        else
            Interlocked.Increment(ref _interpretationsIncorrect);
    }

    public void RecordOverinterpretationWarning()
    {
        Interlocked.Increment(ref _overinterpretationWarnings);
    }

    public void RecordHumanCorrection()
    {
        Interlocked.Increment(ref _humanCorrectionsRecorded);
    }

    public void RecordRestraintDecision(bool allowed)
    {
        if (allowed)
            Interlocked.Increment(ref _restraintDecisionsAllowed);
        else
            Interlocked.Increment(ref _restraintDecisionsSuppressed);
    }

    public void RecordTimelineAnalysis(int sessions, int arcs, int momentum, int regressions)
    {
        Interlocked.Add(ref _timelineSessionsDetected, sessions);
        Interlocked.Add(ref _timelineArcsDetected, arcs);
        Interlocked.Add(ref _momentumSignalsDetected, momentum);
        Interlocked.Add(ref _regressionSignalsDetected, regressions);
    }

    public Phase7Metrics GetPhase7Metrics() => new()
    {
        PerceptionSnapshotsRecorded = Interlocked.Read(ref _perceptionSnapshotsRecorded),
        PerceptionReplaysPerformed = Interlocked.Read(ref _perceptionReplaysPerformed),
        InterpretationDivergencesFound = Interlocked.Read(ref _interpretationDivergencesFound),
        InterpretationsCorrect = Interlocked.Read(ref _interpretationsCorrect),
        InterpretationsIncorrect = Interlocked.Read(ref _interpretationsIncorrect),
        InterpretationsPartial = Interlocked.Read(ref _interpretationsPartial),
        OverinterpretationWarnings = Interlocked.Read(ref _overinterpretationWarnings),
        HumanCorrectionsRecorded = Interlocked.Read(ref _humanCorrectionsRecorded),
        RestraintDecisionsAllowed = Interlocked.Read(ref _restraintDecisionsAllowed),
        RestraintDecisionsSuppressed = Interlocked.Read(ref _restraintDecisionsSuppressed),
        TimelineSessionsDetected = Interlocked.Read(ref _timelineSessionsDetected),
        TimelineArcsDetected = Interlocked.Read(ref _timelineArcsDetected),
        MomentumSignalsDetected = Interlocked.Read(ref _momentumSignalsDetected),
        RegressionSignalsDetected = Interlocked.Read(ref _regressionSignalsDetected)
    };

    // ═══════════════════════════════════════════
    // FULL DIAGNOSTICS SNAPSHOT
    // ═══════════════════════════════════════════

    /// <summary>
    /// Get a complete diagnostics snapshot of all cognitive subsystems.
    /// This is what the /api/cognitive/diagnostics endpoint returns.
    /// </summary>
    public CognitiveDiagnosticsSnapshot GetDiagnosticsSnapshot() => new()
    {
        ExportedAt = DateTimeOffset.UtcNow,
        Uptime = DateTimeOffset.UtcNow - _startedAt,
        MemoryPipeline = GetMemoryPipelineMetrics(),
        Metabolism = GetMetabolismMetrics(),
        Deduplication = GetDeduplicationMetrics(),
        Contradictions = GetContradictionMetrics(),
        Interventions = GetInterventionMetrics(),
        Retrieval = GetRetrievalMetrics(),
        Timeline = GetTimelineMetrics(),
        Automation = GetAutomationMetrics(),
        Perception = GetPerceptionMetrics(),
        Phase7 = GetPhase7Metrics()
    };
}

// ═══════════════════════════════════════════
// METRICS MODELS
// ═══════════════════════════════════════════

public class CognitiveDiagnosticsSnapshot
{
    public DateTimeOffset ExportedAt { get; set; }
    public TimeSpan Uptime { get; set; }
    public MemoryPipelineMetrics MemoryPipeline { get; set; } = new();
    public MetabolismMetrics Metabolism { get; set; } = new();
    public DeduplicationMetrics Deduplication { get; set; } = new();
    public ContradictionMetrics Contradictions { get; set; } = new();
    public InterventionMetrics Interventions { get; set; } = new();
    public RetrievalMetrics Retrieval { get; set; } = new();
    public TimelineMetrics Timeline { get; set; } = new();
    public AutomationMetrics Automation { get; set; } = new();
    public PerceptionMetrics Perception { get; set; } = new();
    public Phase7Metrics Phase7 { get; set; } = new();
}

public class MemoryPipelineMetrics
{
    public long TotalInvocations { get; set; }
    public long SuccessfulExtractions { get; set; }
    public long FailedExtractions { get; set; }
    public long TotalCandidatesExtracted { get; set; }
    public long TotalNodesCreatedByPipeline { get; set; }
    public double FailureRate { get; set; }
}

public class MetabolismMetrics
{
    public long CyclesCompleted { get; set; }
    public long CyclesFailed { get; set; }
    public DateTimeOffset? LastCycleAt { get; set; }
    public TimeSpan? LastCycleDuration { get; set; }
    public double AverageCycleDurationMs { get; set; }
    public long TotalNodesAnalyzed { get; set; }
    public long TotalSalienceUpdates { get; set; }
    public long TotalNodesArchived { get; set; }
    public long TotalTensionsGenerated { get; set; }
}

public class DeduplicationMetrics
{
    public long TotalRuns { get; set; }
    public long TotalMerges { get; set; }
    public long TotalNodesAnalyzed { get; set; }
    public double AverageDurationMs { get; set; }
    public double DuplicateRate { get; set; }
}

public class ContradictionMetrics
{
    public long TotalDetections { get; set; }
    public long TotalSemanticContradictions { get; set; }
    public long TotalBehavioralContradictions { get; set; }
    public long ActiveContradictions { get; set; }
    public DateTimeOffset? LastDetectedAt { get; set; }
}

public class InterventionMetrics
{
    public long TotalGenerated { get; set; }
    public long TotalAcknowledged { get; set; }
    public long TotalDismissed { get; set; }
    public long TotalActed { get; set; }
    public DateTimeOffset? LastGeneratedAt { get; set; }
    public int PendingCount { get; set; }
    public List<InterventionRecord> RecentInterventions { get; set; } = new();
}

public class RetrievalMetrics
{
    public long TotalRequests { get; set; }
    public long Hits { get; set; }
    public long Misses { get; set; }
    public double HitRate { get; set; }
    public long TotalNodesInjected { get; set; }
    public long TotalTokensUsed { get; set; }
    public long BudgetExceededCount { get; set; }
    public List<TopInjectedNode> TopInjectedNodes { get; set; } = new();
}

public class TimelineMetrics
{
    public long EventsWritten { get; set; }
    public long WriteFailures { get; set; }
    public long EventsPublishedToBus { get; set; }
    public DateTimeOffset? LastEventAt { get; set; }
    public Dictionary<string, long> EventTypeCounts { get; set; } = new();
    public double WriteFailureRate { get; set; }
}

public class AutomationMetrics
{
    public long ActionsExecuted { get; set; }
    public long VerificationsPassed { get; set; }
    public long VerificationsFailed { get; set; }
    public long Rollbacks { get; set; }
    public double VerificationSuccessRate { get; set; }
}

public class PerceptionMetrics
{
    public long OcrEventsProcessed { get; set; }
    public long ActiveWindowEventsProcessed { get; set; }
    public long SemanticSummariesGenerated { get; set; }
}

public class Phase7Metrics
{
    public long PerceptionSnapshotsRecorded { get; set; }
    public long PerceptionReplaysPerformed { get; set; }
    public long InterpretationDivergencesFound { get; set; }
    public long InterpretationsCorrect { get; set; }
    public long InterpretationsIncorrect { get; set; }
    public long InterpretationsPartial { get; set; }
    public long OverinterpretationWarnings { get; set; }
    public long HumanCorrectionsRecorded { get; set; }
    public long RestraintDecisionsAllowed { get; set; }
    public long RestraintDecisionsSuppressed { get; set; }
    public long TimelineSessionsDetected { get; set; }
    public long TimelineArcsDetected { get; set; }
    public long MomentumSignalsDetected { get; set; }
    public long RegressionSignalsDetected { get; set; }

    public double InterpretationAccuracy => (InterpretationsCorrect + InterpretationsIncorrect + InterpretationsPartial) > 0
        ? (double)InterpretationsCorrect / (InterpretationsCorrect + InterpretationsIncorrect + InterpretationsPartial)
        : 0;

    public double RestraintSuppressionRate => (RestraintDecisionsAllowed + RestraintDecisionsSuppressed) > 0
        ? (double)RestraintDecisionsSuppressed / (RestraintDecisionsAllowed + RestraintDecisionsSuppressed)
        : 0;
}

public class InterventionRecord
{
    public string InterventionId { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; set; }
    public DateTimeOffset? RespondedAt { get; set; }
    public string Status { get; set; } = "pending";
}

public class ContradictionRecord
{
    public string ContradictionId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public DateTimeOffset DetectedAt { get; set; }
    public string Status { get; set; } = "active";
}

public class TopInjectedNode
{
    public string NodeId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public double Score { get; set; }
    public DateTimeOffset InjectedAt { get; set; }
}
