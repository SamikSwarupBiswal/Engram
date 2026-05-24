using Engram.Store.Events;
using Engram.Store.Inference;
using Engram.Store.Memory;
using Engram.Store.Salience;
using Engram.Store.Wiki;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Metabolism;

/// <summary>
/// THE BRAIN OF ENGRAM.
/// 
/// Background hosted service that continuously metabolizes the semantic organism.
/// This is what makes Engram continuously cognitive instead of reactive.
/// 
/// Every cycle:
/// 1. Pull pending raw events
/// 2. Extract semantic entities
/// 3. Merge duplicates
/// 4. Update wiki graph
/// 5. Recompute salience
/// 6. Detect contradictions
/// 7. Generate unresolved tensions
/// 8. Archive stale nodes
/// 9. Emit intervention events
/// 
/// Without this, Engram is just a chatbot with memory.
/// With this, Engram is a continuously metabolizing semantic organism.
/// </summary>
public class BackgroundMetabolismService : BackgroundService
{
    private readonly WikiNodeStore _nodeStore;
    private readonly WikiMetabolizer _metabolizer;
    private readonly SalienceScorer _salienceScorer;
    private readonly DriftDetector _driftDetector;
    private readonly ArchiveManager _archiveManager;
    private readonly ConversationMemoryExtractor _extractor;
    private readonly SemanticDeduplicator _deduplicator;
    private readonly ContradictionDetector _contradictionDetector;
    private readonly IEventBus? _eventBus;
    private readonly InterventionGenerator? _interventionGenerator;
    private readonly InterventionStore? _interventionStore;
    private readonly ContradictionHistoryStore? _contradictionHistoryStore;
    private readonly ContradictionResolutionDetector? _resolutionDetector;
    private readonly CognitiveTelemetry? _telemetry;
    private readonly ILogger<BackgroundMetabolismService>? _logger;
    private readonly ResourceBudgetGovernor _governor;
    private readonly ThermalProtectionLayer _thermalLayer;



    /// <summary>How often the metabolism cycle runs.</summary>
    public TimeSpan CycleInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Maximum events to process per cycle.</summary>
    public int MaxEventsPerCycle { get; set; } = 100;

    /// <summary>Salience threshold below which nodes are archived.</summary>
    public double ArchiveThreshold { get; set; } = 0.1;

    /// <summary>Node count threshold above which active compaction is triggered.</summary>
    public int CompactionTriggerThreshold { get; set; } = 2000;

    /// <summary>Age threshold after which active unaddressed contradictions are auto-expired.</summary>
    public TimeSpan ContradictionExpiryAgeLimit { get; set; } = TimeSpan.FromDays(14);

    /// <summary>Whether the service is currently running a cycle.</summary>
    public bool IsProcessing { get; private set; }

    /// <summary>Timestamp of the last completed cycle.</summary>
    public DateTimeOffset LastCycleAt { get; private set; }

    /// <summary>Number of cycles completed.</summary>
    public long CyclesCompleted { get; private set; }

    /// <summary>Events from the last cycle.</summary>
    public MetabolismCycleResult? LastCycleResult { get; private set; }

    public BackgroundMetabolismService(
        WikiNodeStore nodeStore,
        WikiMetabolizer metabolizer,
        SalienceScorer salienceScorer,
        DriftDetector driftDetector,
        ArchiveManager archiveManager,
        ConversationMemoryExtractor extractor,
        SemanticDeduplicator deduplicator,
        ContradictionDetector contradictionDetector,
        IEventBus? eventBus = null,
        InterventionGenerator? interventionGenerator = null,
        InterventionStore? interventionStore = null,
        ContradictionHistoryStore? contradictionHistoryStore = null,
        ContradictionResolutionDetector? resolutionDetector = null,
        CognitiveTelemetry? telemetry = null,
        ILogger<BackgroundMetabolismService>? logger = null,
        ResourceBudgetGovernor? governor = null,
        ThermalProtectionLayer? thermalLayer = null)
    {
        _nodeStore = nodeStore;
        _metabolizer = metabolizer;
        _salienceScorer = salienceScorer;
        _driftDetector = driftDetector;
        _archiveManager = archiveManager;
        _extractor = extractor;
        _deduplicator = deduplicator;
        _contradictionDetector = contradictionDetector;
        _eventBus = eventBus;
        _interventionGenerator = interventionGenerator;
        _interventionStore = interventionStore;
        _contradictionHistoryStore = contradictionHistoryStore;
        _resolutionDetector = resolutionDetector;
        _telemetry = telemetry;
        _logger = logger;
        _governor = governor ?? new ResourceBudgetGovernor();
        _thermalLayer = thermalLayer ?? new ThermalProtectionLayer();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger?.LogInformation("BackgroundMetabolismService starting. Base cycle interval: {Interval}", CycleInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Dynamic Resource Courtesy state interval calculation
                var intervalMinutes = _governor.GetMetabolicIntervalMinutes();
                CycleInterval = TimeSpan.FromMinutes(intervalMinutes);

                await Task.Delay(CycleInterval, stoppingToken);

                if (!stoppingToken.IsCancellationRequested)
                {
                    await RunMetabolismCycle(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Metabolism cycle failed. Will retry next cycle.");
                // Don't rethrow — keep the service alive
            }
        }

        _logger?.LogInformation("BackgroundMetabolismService stopping. Completed {Cycles} cycles.", CyclesCompleted);
    }

    /// <summary>
    /// Run a single metabolism cycle.
    /// This is the core cognitive loop.
    /// </summary>
    public async Task<MetabolismCycleResult> RunMetabolismCycle(CancellationToken ct = default)
    {
        await _governor.MeasureSchedulingLatencyAsync();
        var state = _governor.CurrentState;

        if (state == ResourceCourtesyState.GamingFullscreen)
        {
            _logger?.LogInformation("Resource courtesy state: Gaming/Fullscreen active. Yielding metabolism cycle to prevent system disturbance.");
            return new MetabolismCycleResult { Success = true, Error = "Suspended due to GamingFullscreen state" };
        }

        if (state == ResourceCourtesyState.ThermalStress)
        {
            _logger?.LogWarning("Resource courtesy state: ThermalStress active. Reducing cycle execution load.");
        }

        if (DegradationTracker.Instance.IsDegraded("SafeModeActive"))
        {
            _logger?.LogWarning("System is running in read-only Safe Mode. Background metabolism cycle suspended.");
            return LastCycleResult ?? new MetabolismCycleResult();
        }

        if (DegradationTracker.Instance.IsDegraded("WakeStabilizing"))
        {
            _logger?.LogWarning("System is stabilizing after wake. Deferring metabolism cycle.");
            return LastCycleResult ?? new MetabolismCycleResult();
        }

        if (IsProcessing)
        {
            _logger?.LogWarning("Metabolism cycle already in progress. Skipping.");
            return LastCycleResult ?? new MetabolismCycleResult();
        }

        IsProcessing = true;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new MetabolismCycleResult();

        try
        {
            _logger?.LogInformation("Starting metabolism cycle {Cycle} (Courtesy State: {State})", CyclesCompleted + 1, state);

            // Step 1: Load all nodes for analysis
            var nodes = _nodeStore.LoadAll();
            result.NodesAnalyzed = nodes.Count;

            // Step 2: Deduplicate (prevent wiki rot)
            DeduplicationResult? dedupResult = null;
            if (state != ResourceCourtesyState.HeavyWorkload)
            {
                dedupResult = _deduplicator.Deduplicate();
                result.MergesPerformed = dedupResult?.MergesPerformed ?? 0;
            }

            // Step 3: Reload after dedup
            nodes = _nodeStore.LoadAll();

            // Compaction: Check if node count exceeds trigger threshold
            if (nodes.Count > CompactionTriggerThreshold && state != ResourceCourtesyState.HeavyWorkload && state != ResourceCourtesyState.ThermalStress)
            {
                _logger?.LogInformation("Graph size ({Count}) exceeds compaction threshold ({Threshold}). Triggering semantic compactor...", nodes.Count, CompactionTriggerThreshold);
                var compactor = new SemanticCompactor(_nodeStore, null, null);
                int compactionMerges = await compactor.CompactGraphAsync(0.70, ct);
                result.MergesPerformed += compactionMerges;

                if (compactionMerges > 0)
                {
                    nodes = _nodeStore.LoadAll();
                }
            }

            // Step 4: Recompute salience for all nodes
            var salienceUpdated = RecomputeSalience(nodes);
            result.SalienceUpdated = salienceUpdated;

            // Step 5: Detect contradictions across the graph
            var contradictions = new List<DriftAlert>();
            if (state != ResourceCourtesyState.HeavyWorkload)
            {
                contradictions = DetectContradictions(nodes);
                result.ContradictionsDetected = contradictions.Count;
            }

            // Step 6: Detect behavioral contradictions (the moat)
            var behavioralContradictions = new List<BehavioralContradiction>();
            if (state != ResourceCourtesyState.HeavyWorkload)
            {
                behavioralContradictions = _contradictionDetector.DetectAll();
                result.BehavioralContradictionsDetected = behavioralContradictions.Count;
            }

            // Step 6b: Generate interventions from contradictions
            if (_interventionGenerator != null && behavioralContradictions.Count > 0)
            {
                var interventions = _interventionGenerator.GenerateInterventions(behavioralContradictions);
                result.InterventionsGenerated = interventions.Count;

                // Persist interventions (Sprint 3: first-class semantic entities)
                if (_interventionStore != null)
                {
                    foreach (var intervention in interventions)
                        _interventionStore.Save(intervention);
                }
            }

            // Step 6c: Record contradictions in history (Sprint 3: longitudinal tracking)
            if (_contradictionHistoryStore != null)
            {
                foreach (var contradiction in behavioralContradictions)
                    _contradictionHistoryStore.Record(contradiction);
            }

            // Step 6d: Detect resolved contradictions (Sprint 3: resolution detection)
            if (_resolutionDetector != null)
            {
                var resolutions = _resolutionDetector.DetectResolutions();
                result.ContradictionsResolved = resolutions.Count;
            }

            // Step 6e: Prune/expire stale unaddressed contradictions older than configured age limit
            if (_contradictionHistoryStore != null)
            {
                _contradictionHistoryStore.PruneExpiredContradictions(ContradictionExpiryAgeLimit);
            }

            // Step 4: Archive stale nodes
            var archived = ArchiveStaleNodes(nodes);
            result.NodesArchived = archived;

            // Step 5: Generate unresolved tensions
            var tensions = GenerateTensions(nodes, contradictions);
            result.TensionsGenerated = tensions.Count;

            // Step 7: Emit events
            EmitCycleEvents(result, contradictions, tensions);

            result.Success = true;
            result.Duration = sw.Elapsed;

            // Step 8: Report telemetry
            _telemetry?.RecordMetabolismCycle(result);
            if (dedupResult != null)
            {
                _telemetry?.RecordDeduplication(dedupResult);
            }
            _telemetry?.RecordContradictionDetection(contradictions.Count, behavioralContradictions.Count);

            if (_telemetry != null)
            {
                double redundancy = (double)result.MergesPerformed / Math.Max(1, result.NodesAnalyzed);
                var frictionTracker = _interventionGenerator?.FrictionTracker;
                double autonomyDrift = 1.0 - (frictionTracker?.HistoricalTrustIndex ?? 1.0);
                double annoyance = frictionTracker?.AnnoyanceScore ?? 0.0;

                double uptimeDays = Math.Max(1.0 / 86400.0, (DateTimeOffset.UtcNow - _telemetry.StartedAt).TotalDays);
                double totalInterventions = _telemetry.GetInterventionMetrics().TotalGenerated;
                double cadence = totalInterventions / uptimeDays;

                double recurrence = 0.0;
                if (_contradictionHistoryStore != null)
                {
                    var activeContradictions = _contradictionHistoryStore.LoadActive();
                    if (activeContradictions.Count > 0)
                    {
                        var recurringOrWorsening = activeContradictions.Count(c =>
                            c.Trend == ContradictionTrend.Recurring || c.Trend == ContradictionTrend.Worsening);
                        recurrence = (double)recurringOrWorsening / activeContradictions.Count;
                    }
                }

                int debtBacklog = _interventionGenerator?.GetCognitiveDebt().Count ?? 0;
                int freezeCount = DegradationTracker.Instance.FreezeFrequency;
                double pathologySeconds = DegradationTracker.Instance.GetPathologyPersistenceDurationSeconds();

                _telemetry.RecordEcologicalMetrics(
                    redundancy,
                    autonomyDrift,
                    annoyance,
                    cadence,
                    recurrence,
                    debtBacklog,
                    freezeCount,
                    pathologySeconds
                );
            }

            LastCycleResult = result;
            CyclesCompleted++;
            LastCycleAt = DateTimeOffset.UtcNow;

            _logger?.LogInformation(
                "Metabolism cycle {Cycle} complete: {Nodes} nodes, {Salience} salience updates, {Contradictions} contradictions, {Archived} archived, {Tensions} tensions in {Ms}ms",
                CyclesCompleted, result.NodesAnalyzed, result.SalienceUpdated,
                result.ContradictionsDetected, result.NodesArchived, result.TensionsGenerated,
                sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            _logger?.LogError(ex, "Metabolism cycle failed");
        }
        finally
        {
            IsProcessing = false;
        }

        return result;
    }

    /// <summary>
    /// Recompute salience for all nodes based on time decay.
    /// Returns count of updated nodes.
    /// </summary>
    private int RecomputeSalience(IReadOnlyList<WikiNode> nodes)
    {
        int updated = 0;

        foreach (var node in nodes)
        {
            var oldSalience = node.Salience;
            var newSalience = _salienceScorer.Compute(node);

            if (Math.Abs(oldSalience - newSalience) > 0.01)
            {
                node.Salience = newSalience;
                _nodeStore.Save(node);
                updated++;
            }
        }

        return updated;
    }

    /// <summary>
    /// Detect contradictions across the wiki graph.
    /// </summary>
    private List<DriftAlert> DetectContradictions(IReadOnlyList<WikiNode> nodes)
    {
        var contradictions = new List<DriftAlert>();

        // Check for nodes with conflicting facts
        foreach (var node in nodes)
        {
            if (node.Facts.Count < 2) continue;

            // Check for negation patterns in facts
            for (int i = 0; i < node.Facts.Count; i++)
            {
                for (int j = i + 1; j < node.Facts.Count; j++)
                {
                    var conflict = DetectFactConflict(node.Facts[i], node.Facts[j]);
                    if (conflict != null)
                    {
                        contradictions.Add(new DriftAlert
                        {
                            NodeId = node.NodeId,
                            Description = $"Contradictory facts in {node.Title}: '{Truncate(node.Facts[i].Text, 40)}' vs '{Truncate(node.Facts[j].Text, 40)}'",
                            Severity = DriftSeverity.Medium,
                            SourceEventIds = node.Facts[i].Sources.Select(s => s.EventId)
                                .Concat(node.Facts[j].Sources.Select(s => s.EventId))
                                .Distinct()
                                .ToList()
                        });
                    }
                }
            }
        }

        // Check for goal-behavior contradictions
        var goals = nodes.Where(n => n.NodeType == WikiNodeType.Goal).ToList();
        var activities = nodes.Where(n => n.NodeType == WikiNodeType.Concept && n.Salience > 0.5).ToList();

        foreach (var goal in goals)
        {
            // If goal has low salience but recent high-activity concepts exist
            if (goal.Salience < 0.3 && activities.Any(a => a.Salience > 0.7))
            {
                var activeActivity = activities.OrderByDescending(a => a.Salience).First();
                contradictions.Add(new DriftAlert
                {
                    NodeId = goal.NodeId,
                    Description = $"Goal '{goal.Title}' is fading (salience: {goal.Salience:F2}) while '{activeActivity.Title}' is highly active (salience: {activeActivity.Salience:F2})",
                    Severity = DriftSeverity.High,
                    SourceEventIds = new List<string>()
                });
            }
        }

        return contradictions;
    }

    /// <summary>
    /// Detect if two facts contradict each other.
    /// </summary>
    private static DriftAlert? DetectFactConflict(WikiFact fact1, WikiFact fact2)
    {
        var text1 = fact1.Text.ToLowerInvariant();
        var text2 = fact2.Text.ToLowerInvariant();

        // Check for negation patterns
        var negationWords = new[] { "not", "no", "never", "don't", "doesn't", "didn't", "won't", "can't", "cannot" };

        foreach (var neg in negationWords)
        {
            // If one fact has negation and the other doesn't, and they share key words
            bool fact1Negated = text1.Contains(neg + " ") || text1.Contains(" " + neg);
            bool fact2Negated = text2.Contains(neg + " ") || text2.Contains(" " + neg);

            if (fact1Negated != fact2Negated)
            {
                // Extract key words (excluding negation words)
                var words1 = text1.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => w.Length >= 4 && !negationWords.Contains(w))
                    .ToHashSet();
                var words2 = text2.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => w.Length >= 4 && !negationWords.Contains(w))
                    .ToHashSet();

                var overlap = words1.Intersect(words2).Count();
                if (overlap >= 2)
                {
                    return new DriftAlert(); // Signal conflict
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Archive nodes with salience below threshold.
    /// </summary>
    private int ArchiveStaleNodes(IReadOnlyList<WikiNode> nodes)
    {
        int archived = 0;

        foreach (var node in nodes)
        {
            if (SemanticCompactor.IsProtectedNode(node)) continue;

            if (_salienceScorer.ShouldArchive(node, ArchiveThreshold))
            {
                try
                {
                    _archiveManager.ArchiveNode(node);
                    archived++;
                    _logger?.LogDebug("Archived stale node: {NodeId} (salience: {Salience:F3})", node.NodeId, node.Salience);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to archive node: {NodeId}", node.NodeId);
                }
            }
        }

        return archived;
    }

    /// <summary>
    /// Generate unresolved tension reports from contradictions and stale goals.
    /// </summary>
    private List<TensionReport> GenerateTensions(IReadOnlyList<WikiNode> nodes, List<DriftAlert> contradictions)
    {
        var tensions = new List<TensionReport>();

        // Tension from contradictions
        foreach (var contradiction in contradictions.Where(c => c.Severity >= DriftSeverity.Medium))
        {
            tensions.Add(new TensionReport
            {
                Source = "contradiction",
                Description = contradiction.Description,
                Severity = contradiction.Severity,
                RelatedNodeId = contradiction.NodeId
            });
        }

        // Tension from abandoned goals
        var goals = nodes.Where(n => n.NodeType == WikiNodeType.Goal && n.Salience < 0.2).ToList();
        foreach (var goal in goals)
        {
            var daysSinceTouch = (DateTimeOffset.UtcNow - goal.LastTouchedAt).TotalDays;
            if (daysSinceTouch > 7)
            {
                tensions.Add(new TensionReport
                {
                    Source = "abandoned_goal",
                    Description = $"Goal '{goal.Title}' hasn't been touched in {daysSinceTouch:F0} days (salience: {goal.Salience:F2})",
                    Severity = DriftSeverity.Medium,
                    RelatedNodeId = goal.NodeId
                });
            }
        }

        // Tension from high activity with no goal alignment
        var highActivity = nodes.Where(n => n.Salience > 0.8 && n.NodeType == WikiNodeType.Concept).ToList();
        var activeGoals = nodes.Where(n => n.NodeType == WikiNodeType.Goal && n.Salience > 0.5).ToList();

        if (highActivity.Count > 3 && activeGoals.Count == 0)
        {
            tensions.Add(new TensionReport
            {
                Source = "goal_vacuum",
                Description = $"High activity ({highActivity.Count} active concepts) but no active goals. Direction unclear.",
                Severity = DriftSeverity.High,
                RelatedNodeId = null
            });
        }

        return tensions;
    }

    /// <summary>
    /// Emit events from the metabolism cycle.
    /// </summary>
    private void EmitCycleEvents(MetabolismCycleResult result, List<DriftAlert> contradictions, List<TensionReport> tensions)
    {
        if (_eventBus == null) return;

        // Emit cycle completed event
        _eventBus.Publish(new EventEnvelope
        {
            EventType = "metabolism.cycle_completed",
            Source = "background_metabolism",
            Payload = new
            {
                Cycle = CyclesCompleted,
                NodesAnalyzed = result.NodesAnalyzed,
                SalienceUpdated = result.SalienceUpdated,
                ContradictionsDetected = result.ContradictionsDetected,
                NodesArchived = result.NodesArchived,
                TensionsGenerated = result.TensionsGenerated,
                Duration = result.Duration.TotalMilliseconds
            }
        });

        // Emit drift events
        foreach (var contradiction in contradictions)
        {
            _eventBus.Publish(new EventEnvelope
            {
                EventType = EventTypes.DriftDetected,
                Source = "background_metabolism",
                Payload = contradiction
            });
        }

        // Emit tension events
        foreach (var tension in tensions)
        {
            _eventBus.Publish(new EventEnvelope
            {
                EventType = "metabolism.tension_detected",
                Source = "background_metabolism",
                Payload = tension
            });
        }
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}

/// <summary>
/// Result of a metabolism cycle.
/// </summary>
public class MetabolismCycleResult
{
    public bool Success { get; set; }
    public int NodesAnalyzed { get; set; }
    public int MergesPerformed { get; set; }
    public int SalienceUpdated { get; set; }
    public int ContradictionsDetected { get; set; }
    public int BehavioralContradictionsDetected { get; set; }
    public int NodesArchived { get; set; }
    public int TensionsGenerated { get; set; }
    public int InterventionsGenerated { get; set; }
    public int ContradictionsResolved { get; set; }
    public TimeSpan Duration { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// A tension report — unresolved conflict or behavioral drift.
/// </summary>
public class TensionReport
{
    public string Source { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DriftSeverity Severity { get; set; }
    public string? RelatedNodeId { get; set; }
}
