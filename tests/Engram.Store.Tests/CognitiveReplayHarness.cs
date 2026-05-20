using Engram.Store.Events;
using Engram.Store.Identity;
using Engram.Store.Memory;
using Engram.Store.Metabolism;
using Engram.Store.Salience;
using Engram.Store.Search;
using Engram.Store.Wiki;

namespace Engram.Store.Tests;

/// <summary>
/// Replayable cognitive test harness.
/// 
/// This is the scientific method for Engram.
/// 
/// Inject event streams, replay semantic histories, observe organism evolution.
/// Every test runs in an isolated environment with full cognitive pipeline.
/// 
/// Pipeline under test:
///   Conversation → MemoryPipeline → WikiMetabolizer → BackgroundMetabolism
///   → ContradictionDetector → InterventionGenerator → SemanticSearch
///   → RetrievalBudgetManager → PromptAssembler
/// 
/// All telemetry is captured via CognitiveTelemetry for behavioral assertions.
/// </summary>
public class CognitiveReplayHarness : IDisposable
{
    private readonly string _tempDir;

    // Core stores
    public WorkspacePaths Paths { get; }
    public WikiNodeStore NodeStore { get; }
    public IdentityStore IdentityStore { get; }

    // Metabolism
    public WikiMetabolizer Metabolizer { get; }
    public SalienceScorer SalienceScorer { get; }
    public DriftDetector DriftDetector { get; }
    public ArchiveManager ArchiveManager { get; }
    public SemanticDeduplicator Deduplicator { get; }
    public ContradictionDetector ContradictionDetector { get; }
    public InterventionGenerator InterventionGenerator { get; }
    public BackgroundMetabolismService MetabolismService { get; }
    public InterventionStore InterventionStore { get; }
    public ContradictionHistoryStore ContradictionHistoryStore { get; }
    public ContradictionResolutionDetector ResolutionDetector { get; }
    public TensionEvolutionEngine TensionEngine { get; }

    // Memory pipeline
    public ConversationMemoryExtractor Extractor { get; }
    public ConversationMemoryPipeline MemoryPipeline { get; }

    // Search & retrieval
    public SearchEngine SearchEngine { get; }
    public SemanticSearchEngine SemanticSearchEngine { get; }
    public RetrievalBudgetManager BudgetManager { get; }
    public PromptAssembler PromptAssembler { get; }

    // Events & telemetry
    public InMemoryEventBus EventBus { get; }
    public CognitiveTelemetry Telemetry { get; }

    // Event history for assertions
    public List<EventEnvelope> CapturedEvents { get; } = new();

    public CognitiveReplayHarness()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram_validation_" + Guid.NewGuid().ToString("n")[..8]);
        Paths = new WorkspacePaths(_tempDir);

        // Core stores
        NodeStore = new WikiNodeStore(Paths);
        IdentityStore = new IdentityStore(Paths);

        // Metabolism
        Metabolizer = new WikiMetabolizer(NodeStore);
        SalienceScorer = new SalienceScorer();
        DriftDetector = new DriftDetector(NodeStore);
        ArchiveManager = new ArchiveManager(NodeStore, SalienceScorer, Paths);
        Deduplicator = new SemanticDeduplicator(NodeStore);
        ContradictionDetector = new ContradictionDetector(NodeStore, IdentityStore);

        // Events & telemetry
        EventBus = new InMemoryEventBus();
        Telemetry = new CognitiveTelemetry();

        // Intervention generator
        InterventionGenerator = new InterventionGenerator(IdentityStore, EventBus);

        // Memory pipeline
        Extractor = new ConversationMemoryExtractor();
        MemoryPipeline = new ConversationMemoryPipeline(Extractor, Metabolizer, EventBus, Telemetry);

        // Search & retrieval
        SearchEngine = new SearchEngine(NodeStore);
        SemanticSearchEngine = new SemanticSearchEngine(NodeStore, SalienceScorer);
        BudgetManager = new RetrievalBudgetManager();
        PromptAssembler = new PromptAssembler(IdentityStore, NodeStore, SearchEngine, BudgetManager);

        // Sprint 3: persistent stores
        InterventionStore = new InterventionStore(Paths);
        ContradictionHistoryStore = new ContradictionHistoryStore(Paths);
        ResolutionDetector = new ContradictionResolutionDetector(ContradictionHistoryStore, NodeStore);
        TensionEngine = new TensionEvolutionEngine(ContradictionHistoryStore);

        // Background metabolism (the brain)
        MetabolismService = new BackgroundMetabolismService(
            NodeStore, Metabolizer, SalienceScorer, DriftDetector,
            ArchiveManager, Extractor, Deduplicator, ContradictionDetector,
            EventBus, InterventionGenerator, InterventionStore,
            ContradictionHistoryStore, ResolutionDetector, Telemetry);

        // Capture all events for assertions
        EventBus.SubscribeAll(e => CapturedEvents.Add(e));
    }

    // ═══════════════════════════════════════════
    // INJECTION METHODS
    // ═══════════════════════════════════════════

    /// <summary>
    /// Inject a conversation exchange through the memory pipeline.
    /// This simulates a user chatting with Engram.
    /// </summary>
    public ConversationMemoryResult InjectConversation(string userMessage, string assistantResponse = "")
    {
        var result = MemoryPipeline.ProcessConversation(userMessage, assistantResponse);
        SemanticSearchEngine.InvalidateIndex();
        return result;
    }

    /// <summary>
    /// Inject a raw wiki node directly into the store.
    /// For seeding test data.
    /// </summary>
    public void InjectNode(WikiNode node)
    {
        NodeStore.Save(node);
        SemanticSearchEngine.InvalidateIndex();
    }

    /// <summary>
    /// Inject identity data (priorities, goals, preferences).
    /// </summary>
    public void InjectIdentity(string displayName, List<string>? goals = null,
        List<string>? preferences = null, List<string>? anxieties = null)
    {
        var profile = new UserProfile
        {
            DisplayName = displayName,
            Goals = goals ?? new List<string>(),
            ComfortTriggers = preferences ?? new List<string>(),
            RecurringAnxieties = anxieties ?? new List<string>()
        };
        IdentityStore.SaveProfile(profile);
    }

    /// <summary>
    /// Inject a priority into the identity store.
    /// </summary>
    public void InjectPriority(string description, double confidence = 0.9)
    {
        var priorities = IdentityStore.LoadPriorities();
        priorities.Add(new Priority { Description = description, Confidence = confidence });
        IdentityStore.SavePriorities(priorities);
    }

    /// <summary>
    /// Run a metabolism cycle and return the result.
    /// This is the core cognitive loop step.
    /// </summary>
    public async Task<MetabolismCycleResult> RunMetabolismCycle()
    {
        return await MetabolismService.RunMetabolismCycle();
    }

    /// <summary>
    /// Run multiple metabolism cycles (simulating time passing).
    /// </summary>
    public async Task<List<MetabolismCycleResult>> RunMetabolismCycles(int count)
    {
        var results = new List<MetabolismCycleResult>();
        for (int i = 0; i < count; i++)
        {
            results.Add(await MetabolismService.RunMetabolismCycle());
        }
        return results;
    }

    /// <summary>
    /// Simulate time passing by adjusting LastTouchedAt on nodes.
    /// </summary>
    public void SimulateTimePassage(TimeSpan duration)
    {
        var nodes = NodeStore.LoadAll();
        foreach (var node in nodes)
        {
            node.LastTouchedAt = node.LastTouchedAt.Subtract(duration);
            NodeStore.Save(node);
        }
    }

    /// <summary>
    /// Search the wiki for a query.
    /// </summary>
    public SearchResponse Search(string query, int maxResults = 20)
    {
        SemanticSearchEngine.RebuildIndex();
        return SemanticSearchEngine.Search(query, maxResults);
    }

    /// <summary>
    /// Assemble a prompt for a user message (tests retrieval → prompt pipeline).
    /// </summary>
    public string AssemblePrompt(string userMessage)
    {
        SemanticSearchEngine.RebuildIndex();
        return PromptAssembler.AssemblePrompt(userMessage);
    }

    /// <summary>
    /// Detect contradictions in the current state.
    /// </summary>
    public List<BehavioralContradiction> DetectContradictions()
    {
        return ContradictionDetector.DetectAll();
    }

    /// <summary>
    /// Generate interventions from contradictions.
    /// </summary>
    public List<Intervention> GenerateInterventions(List<BehavioralContradiction> contradictions)
    {
        return InterventionGenerator.GenerateInterventions(contradictions);
    }

    // ═══════════════════════════════════════════
    // QUERY METHODS (for assertions)
    // ═══════════════════════════════════════════

    /// <summary>
    /// Get all wiki nodes of a specific type.
    /// </summary>
    public List<WikiNode> GetNodesByType(WikiNodeType type)
    {
        return NodeStore.LoadAll().Where(n => n.NodeType == type).ToList();
    }

    /// <summary>
    /// Get a specific node by ID.
    /// </summary>
    public WikiNode? GetNode(string nodeId)
    {
        return NodeStore.LoadAll().FirstOrDefault(n => n.NodeId == nodeId);
    }

    /// <summary>
    /// Get all nodes sorted by salience (descending).
    /// </summary>
    public List<WikiNode> GetNodesBySalience()
    {
        return NodeStore.LoadAll().OrderByDescending(n => n.Salience).ToList();
    }

    /// <summary>
    /// Get events of a specific type from captured events.
    /// </summary>
    public List<EventEnvelope> GetEventsByType(string eventType)
    {
        return CapturedEvents.Where(e => e.EventType == eventType).ToList();
    }

    /// <summary>
    /// Get the full diagnostics snapshot.
    /// </summary>
    public CognitiveDiagnosticsSnapshot GetDiagnostics()
    {
        return Telemetry.GetDiagnosticsSnapshot();
    }

    public void Dispose()
    {
        EventBus.Dispose();
        IdentityStore.Dispose();
        NodeStore.Dispose();
        SemanticSearchEngine.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
