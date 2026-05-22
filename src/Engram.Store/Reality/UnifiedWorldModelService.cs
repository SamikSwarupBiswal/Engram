using System;
using System.Collections.Generic;
using System.Linq;
using Engram.Store.Events;
using Engram.Store.Wiki;

namespace Engram.Store.Reality;

/// <summary>
/// Unified World Model Service coordinates all reality-fusion, attention-propagation,
/// cross-modal resolution, and claim consistency components.
/// Subscribes to events and drives reality updates.
/// </summary>
public class UnifiedWorldModelService : IDisposable
{
    private readonly WikiNodeStore _nodeStore;
    private readonly IEventBus _eventBus;
    private readonly List<IDisposable> _subscriptions = new();
    private readonly object _lock = new();

    public CrossModalResolver Resolver { get; }
    public TemporalFusionEngine FusionEngine { get; }
    public GlobalAttentionOrchestrator Orchestrator { get; }
    public AttentionStormGuard StormGuard { get; }
    public MemoryPropagationEngine PropagationEngine { get; }
    public SemanticSceneConstructor SceneConstructor { get; }
    public GlobalConsistencyEngine ConsistencyEngine { get; }

    public UnifiedWorldModelService(WikiNodeStore nodeStore, IEventBus eventBus)
    {
        _nodeStore = nodeStore ?? throw new ArgumentNullException(nameof(nodeStore));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));

        // Instantiate subcomponents
        Resolver = new CrossModalResolver(_nodeStore);
        FusionEngine = new TemporalFusionEngine(Resolver, _eventBus);
        Orchestrator = new GlobalAttentionOrchestrator(_nodeStore);
        StormGuard = new AttentionStormGuard { RefractoryCooldown = TimeSpan.FromMilliseconds(500) };
        PropagationEngine = new MemoryPropagationEngine(_nodeStore, Orchestrator, StormGuard);
        SceneConstructor = new SemanticSceneConstructor(_eventBus);
        ConsistencyEngine = new GlobalConsistencyEngine();

        SubscribeToEvents();
    }

    private void SubscribeToEvents()
    {
        // 1. Refresh resolver when wiki nodes are created or updated
        _subscriptions.Add(_eventBus.Subscribe(EventTypes.WikiNodeCreated, _ => Resolver.Refresh()));
        _subscriptions.Add(_eventBus.Subscribe(EventTypes.WikiNodeUpdated, _ => Resolver.Refresh()));

        // 2. Handle reality temporal fusion events
        _subscriptions.Add(_eventBus.Subscribe("reality.temporal_fused", envelope =>
        {
            if (envelope.Payload == null) return;
            try
            {
                var payloadStr = System.Text.Json.JsonSerializer.Serialize(envelope.Payload);
                var entry = System.Text.Json.JsonSerializer.Deserialize<FusedChronologyEntry>(payloadStr);
                if (entry != null && !string.IsNullOrEmpty(entry.ResolvedNodeId))
                {
                    HandleResolvedReality(entry);
                }
            }
            catch { }
        }));
    }

    private void HandleResolvedReality(FusedChronologyEntry entry)
    {
        lock (_lock)
        {
            var node = _nodeStore.Load(entry.ResolvedNodeId!);
            if (node == null) return;

            // Update starting node attention
            Orchestrator.RecordAttention(node.NodeId, 1.0);
            node.Salience = Orchestrator.GetAttention(node.NodeId);
            node.LastTouchedAt = DateTimeOffset.UtcNow;
            _nodeStore.Save(node);

            // Propagate attention along edges
            PropagationEngine.Propagate(node.NodeId, 1.0);

            // Run consistency check on the node
            bool affectsExecution = !string.IsNullOrEmpty(entry.WorkflowId);
            var analysis = ConsistencyEngine.AnalyzeNode(node, affectsExecution);

            if (analysis.Escalations.Any())
            {
                _eventBus.Publish(new EventEnvelope
                {
                    EventType = "reality.tension_escalated",
                    Source = "unified_world_model_service",
                    Payload = analysis
                });
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var sub in _subscriptions)
            {
                sub.Dispose();
            }
            _subscriptions.Clear();

            FusionEngine.Dispose();
            SceneConstructor.Dispose();
        }
    }
}
