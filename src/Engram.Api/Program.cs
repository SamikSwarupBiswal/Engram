using Engram.Store;
using Engram.Store.Events;
using Engram.Store.Search;
using Engram.Store.Wiki;
using Engram.Store.Salience;
using Engram.Store.Identity;
using Engram.Store.Inference;
using Engram.Store.Billing;
using Engram.Store.Google;
using Engram.Store.Agent;
using Engram.Store.Automation;
using Engram.Store.Security;
using Engram.Store.Perception;
using System.Text.Json;
using Engram.Store.Governance;



var builder = WebApplication.CreateBuilder(args);

// OpenAPI/Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "Engram API", Version = "1.0.0", Description = "Personal Semantic Operating Layer API" });
});

// Rate limiting (simple in-memory)
builder.Services.AddSingleton<Engram.Store.ApiRateLimiter>();

// CORS for Tauri frontend
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

app.UseCors();

// Swagger UI (development only)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Rate limiting middleware
app.Use(async (context, next) =>
{
    var limiter = context.RequestServices.GetRequiredService<Engram.Store.ApiRateLimiter>();
    if (!limiter.TryAcquire())
    {
        context.Response.StatusCode = 429;
        await context.Response.WriteAsJsonAsync(new { error = "Rate limit exceeded. Try again later." });
        return;
    }
    await next();
});

// Initialize workspace
var workspacePath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".engram");
var paths = new WorkspacePaths(workspacePath);
var initializer = new WorkspaceInitializer();
initializer.Initialize(paths);

// Shared services
var hasher = new ContentHasher();
var writer = new RawEventWriter(paths, hasher);
var enumerator = new ReplayEnumerator(paths);
var nodeStore = new WikiNodeStore(paths);
var searchEngine = new SearchEngine(nodeStore);
var briefGenerator = new BriefGenerator(nodeStore);
var identityStore = new IdentityStore(paths);
var salienceScorer = new SalienceScorer();
var driftAlertStore = new DriftAlertStore(paths);
var governance = new GovernanceCoordinator(nodeStore, paths, driftAlertStore);

var archiveManager = new ArchiveManager(nodeStore, salienceScorer, paths);
var discoverySOP = new DiscoverySOP(identityStore);
var interventionPolicy = new InterventionPolicy(identityStore);
// ── Structured Logger ──
var log = InferenceLogger.Instance;
log.Boot("Engram.Api starting...");
log.Boot($"Workspace: {paths.Root}");
log.Boot($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}");
log.Boot($"Process ID: {Environment.ProcessId}");

// ── Inference Components ──
var gpuDetector = new GpuDetector();
var modelManager = new ModelManager();
var localEngine = new LocalInferenceEngine(modelManager, gpuDetector);
var inferenceRouter = new InferenceRouter(localEngine);

// ── Backend probe + verdict persistence ──
var probe = new BackendProbe();
var verdictStore = new VerdictStore(paths.Root);

// ── Lifecycle Manager (single source of truth) ──
var lifecycle = new InferenceLifecycleManager();
lifecycle.Configure(gpuDetector, modelManager, localEngine, inferenceRouter,
    downloadFunc: null, probe: probe, verdictStore: verdictStore);

var tokenBudget = new TokenBudget(paths.Config);
var gwsManager = new GoogleWorkspaceManager(paths.Config);
var researchAgent = new ResearchAgent(paths.Config);
var permissionGate = new PermissionGate();
var actionExecutor = new ActionExecutor();
var keyManager = new KeyManager(paths.Config);
var dataExport = new DataExport(paths.Root);
var dataDelete = new DataDelete(paths.Root);
var screenCapture = new ScreenCaptureService();
var actionRuntime = new ActionRuntime(actionExecutor, permissionGate);
var taskPlanner = new TaskPlanner(localEngine);
var cognitiveActionLoop = new CognitiveActionLoop(taskPlanner, actionRuntime, localEngine);

var eventBus = new Engram.Store.Events.InMemoryEventBus();

// ── Phase 8 Services ──
var worldModel = new OperationalWorldModel(eventBus);
var workflowStore = new WorkflowPersistenceStore(paths.Root);
var workflowRuntime = new WorkflowRuntime(workflowStore, actionRuntime, worldModel);
var proceduralMemory = new ProceduralMemoryEngine(paths.Root);
await proceduralMemory.InitializeAsync();
var executionReasoning = new ExecutionReasoningEngine(worldModel, localEngine);
var collaborationEngine = new CollaborationEngine(eventBus);
var attentionOrchestrator = new OperationalAttentionOrchestrator();
var telemetryEngine = new ExecutionTelemetryEngine(paths.Root);
var browserAgentForLayer = new BrowserAgentRuntime();
var desktopOpForLayer = new DesktopOperator();
var toolAbstraction = new ToolAbstractionLayer(browserAgentForLayer, desktopOpForLayer);
var resilienceEngine = new EnvironmentalResilienceEngine(eventBus);
var sandboxManager = new SandboxManager();
var agentOrchestrator = new AgentOrchestrator(worldModel, eventBus);
var operationalTimeline = new OperationalTimeline(paths.Root);

// ── Phase 8 Extension Services ──
var intentMonitor = new WorkflowIntentMonitor(worldModel, eventBus);
var confidenceEngine = new WorkflowConfidenceEngine(telemetryEngine, proceduralMemory, eventBus);
var driftEngine = new OperationalDriftEngine(worldModel, telemetryEngine, operationalTimeline, eventBus, paths.Root);
var interruptionClassifier = new InterruptionClassifier(worldModel, eventBus);
var priorityGraph = new OperationalPriorityGraph(worldModel, confidenceEngine, identityStore);
var replayEngine = new ExecutionReplayEngine(operationalTimeline, workflowStore);
var workflowConsolidator = new WorkflowConsolidator(proceduralMemory, telemetryEngine, paths.Root);
var envSyncEngine = new EnvironmentSynchronizationEngine(worldModel, eventBus);
var escalationPolicy = new EscalationPolicyEngine(collaborationEngine, confidenceEngine, eventBus);
var failureArchaeologyStore = new FailureArchaeologyStore(paths.Root);


// ── Memory Pipeline (semantic continuity) ──
var conversationExtractor = new Engram.Store.Memory.ConversationMemoryExtractor();
var cognitiveTelemetry = new Engram.Store.Metabolism.CognitiveTelemetry();
var memoryPipeline = new Engram.Store.Memory.ConversationMemoryPipeline(conversationExtractor, new WikiMetabolizer(nodeStore), eventBus, cognitiveTelemetry);
var promptAssembler = new Engram.Store.Memory.PromptAssembler(identityStore, nodeStore, searchEngine);
var wikiMetabolizer = new WikiMetabolizer(nodeStore);
var timelineSubscriber = new TimelineSubscriber(eventBus, writer, cognitiveTelemetry);
timelineSubscriber.Start(); // Start listening to all events

// ── Task Router (intent orchestration) ──
var intentClassifier = new Engram.Store.Orchestration.IntentClassifier();
var semanticSearchEngine = new Engram.Store.Search.SemanticSearchEngine(nodeStore, salienceScorer);
var driftDetector = new Engram.Store.Salience.DriftDetector(nodeStore);
var taskRouter = new Engram.Store.Orchestration.TaskRouter(
    intentClassifier, semanticSearchEngine, nodeStore, promptAssembler,
    identityStore, salienceScorer, driftDetector, eventBus);

// ── Background Metabolism (the brain) ──
var deduplicator = new Engram.Store.Metabolism.SemanticDeduplicator(nodeStore);
var contradictionDetector = new Engram.Store.Metabolism.ContradictionDetector(nodeStore, identityStore);
var interventionGenerator = new Engram.Store.Metabolism.InterventionGenerator(identityStore, eventBus);
var interventionStore = new Engram.Store.Metabolism.InterventionStore(paths);
var contradictionHistoryStore = new Engram.Store.Metabolism.ContradictionHistoryStore(paths);
var resolutionDetector = new Engram.Store.Metabolism.ContradictionResolutionDetector(contradictionHistoryStore, nodeStore);
var backgroundMetabolism = new Engram.Store.Metabolism.BackgroundMetabolismService(
    nodeStore, wikiMetabolizer, salienceScorer, driftDetector,
    archiveManager, conversationExtractor, deduplicator, contradictionDetector,
    eventBus, interventionGenerator, interventionStore, contradictionHistoryStore,
    resolutionDetector, cognitiveTelemetry);
// Start as a background hosted service
_ = backgroundMetabolism.StartAsync(CancellationToken.None);
var ocrService = new OcrService();
var stateDetector = new UiStateDetector();
var perceptionPipeline = new VisualPerceptionPipeline(paths.Raw, screenCapture, ocrService, stateDetector);
var layoutSnap = new LayoutSnapService();

// ── Health: Single source of truth for all readiness state ──
app.MapGet("/api/health", () => Results.Ok(lifecycle.GetHealth()));

app.MapGet("/", () => Results.Ok(new
{
    service = "Engram API",
    version = "1.0.0",
    status = "running",
    workspace = paths.Root
}));

// --- Search ---
app.MapGet("/api/search", (string? q, int? limit) =>
{
    if (string.IsNullOrWhiteSpace(q))
        return Results.BadRequest(new { error = "Query parameter 'q' is required." });

    var response = searchEngine.Search(q, limit ?? 20);
    return Results.Ok(new
    {
        query = response.Query,
        nodesSearched = response.NodesSearched,
        duration = response.Duration.TotalMilliseconds,
        results = response.Results.Select(r => new
        {
            title = r.Node?.Title ?? "",
            snippet = r.MatchingFacts?.FirstOrDefault()?.Text ?? r.Node?.Summary ?? "",
            score = r.Relevance,
            nodeId = r.Node?.NodeId ?? "",
            nodeType = r.Node?.NodeType.ToString() ?? "",
            matchedFields = r.MatchedFields
        })
    });
});

// --- Wiki ---
app.MapGet("/api/wiki", () =>
{
    var nodes = nodeStore.LoadAll();
    return Results.Ok(new
    {
        count = nodes.Count,
        nodes = nodes.Select(n => new
        {
            nodeId = n.NodeId,
            title = n.Title,
            nodeType = n.NodeType,
            salience = n.Salience,
            lastTouchedAt = n.LastTouchedAt
        })
    });
});

app.MapGet("/api/wiki/{nodeId}", (string nodeId) =>
{
    var node = nodeStore.Load(nodeId);
    if (node == null)
        return Results.NotFound(new { error = "Node not found." });

    return Results.Ok(node);
});

// --- Briefs ---
app.MapGet("/api/brief", (string? time) =>
{
    var isEvening = string.Equals(time, "evening", StringComparison.OrdinalIgnoreCase);
    var brief = isEvening
        ? briefGenerator.GenerateEveningBrief()
        : briefGenerator.GenerateMorningBrief();
    return Results.Ok(new
    {
        type = isEvening ? "evening" : "morning",
        content = brief.Content,
        generatedAt = brief.GeneratedAt
    });
});

// --- Events ---
app.MapGet("/api/events", (string? source, string? from, string? to, int? offset, int? limit) =>
{
    var query = new ReplayQuery
    {
        Source = source,
        FromDate = from != null ? DateOnly.TryParse(from, out var fd) ? fd : null : null,
        ToDate = to != null ? DateOnly.TryParse(to, out var td) ? td : null : null,
        Offset = offset,
        Limit = limit ?? 100
    };

    var events = enumerator.Enumerate(query);
    return Results.Ok(new
    {
        count = events.Count,
        events = events.Select(e => new
        {
            eventId = e.EventId,
            eventType = e.EventType,
            capturedAt = e.CapturedAt,
            source = e.Source,
            activeWindow = e.ActiveWindow,
            textPreview = e.Text?.Length > 200 ? e.Text[..200] + "..." : e.Text
        })
    });
});

// --- Status ---
app.MapGet("/api/status", () =>
{
    var rawCount = Directory.Exists(paths.Raw)
        ? Directory.GetFiles(paths.Raw, "*.json", SearchOption.AllDirectories)
            .Count(f => !f.EndsWith(".meta.json"))
        : 0;
    var wikiCount = nodeStore.LoadAll().Count;
    var config = new EngramConfig();

    return Results.Ok(new
    {
        workspace = paths.Root,
        tier = config.Tier.ToString(),
        cloudEnabled = config.CloudEnabled,
        rawEvents = rawCount,
        wikiNodes = wikiCount,
        isCapturing = true
    });
});

// --- Identity ---
app.MapGet("/api/identity", () =>
{
    var profile = identityStore.LoadProfile();
    if (profile == null)
        return Results.Ok(new { discovered = false });

    return Results.Ok(new
    {
        discovered = true,
        name = profile.DisplayName,
        goals = profile.Goals,
        comfortTriggers = profile.ComfortTriggers,
        recurringAnxieties = profile.RecurringAnxieties,
        preferences = profile.Preferences
    });
});

// --- Drift Alerts ---
app.MapGet("/api/drift", () =>
{
    var alerts = driftAlertStore.LoadAll();
    return Results.Ok(new
    {
        count = alerts.Count,
        alerts = alerts.Select(a => new
        {
            alertId = a.AlertId,
            description = a.Description,
            severity = a.Severity.ToString(),
            status = a.Status.ToString(),
            detectedAt = a.DetectedAt
        })
    });
});

// --- Discovery Status ---
app.MapGet("/api/discovery/status", () =>
{
    var complete = discoverySOP.IsDiscoveryComplete();
    return Results.Ok(new { complete });
});

// --- Run Discovery ---
app.MapPost("/api/discovery", (DiscoveryAnswers answers) =>
{
    var result = discoverySOP.RunDiscovery(answers);
    discoverySOP.SaveDiscoveryResults(result);
    return Results.Ok(new
    {
        complete = true,
        goals = result.Profile.Goals.Count,
        priorities = result.Priorities.Count,
        antiGoals = result.AntiGoals.Count
    });
});

// --- Update Identity ---
app.MapPut("/api/identity", (UserProfile profile) =>
{
    identityStore.SaveProfile(profile);
    interventionPolicy.InvalidateCache();
    return Results.Ok(new { saved = true });
});

// --- Get Anti-Goals ---
app.MapGet("/api/identity/anti-goals", () =>
{
    var antiGoals = identityStore.LoadAntiGoals();
    return Results.Ok(new { count = antiGoals.Count, antiGoals });
});

// --- Get Priorities ---
app.MapGet("/api/identity/priorities", () =>
{
    var priorities = identityStore.LoadPriorities();
    return Results.Ok(new { count = priorities.Count, priorities });
});

// --- Intervention Policy Check ---
app.MapPost("/api/intervention/check", (InterventionRequest request) =>
{
    var result = interventionPolicy.Evaluate(request);
    return Results.Ok(new
    {
        allowed = result.Allowed,
        reason = result.Reason,
        confidence = result.Confidence,
        severity = result.Severity?.ToString()
    });
});

// --- Chat Completions (real inference) ---
// This is NOT a generic chatbot — it's the intent interface into the semantic operating system.
app.MapPost("/v1/chat/completions", async (HttpContext context) =>
{
    // ── Lifecycle guard: reject requests when not ready ──
    var health = lifecycle.GetHealth();
    if (!health.CanAcceptRequests)
    {
        var statusMessage = health.State switch
        {
            "Starting" => "Engram is starting up. Please wait a moment.",
            "DetectingBackend" => "Detecting GPU backend...",
            "BackendReady" => "Backend ready, preparing model...",
            "DownloadingModel" => $"Model is downloading ({health.Progress:F0}%). Please wait.",
            "LoadingModel" => "Model is loading into memory. This may take a moment.",
            "Error" => $"Inference unavailable: {health.Error}",
            "Offline" => "Engram is offline.",
            _ => $"Engram is not ready (state: {health.State})"
        };

        log.Router($"Request rejected: state={health.State}");
        return Results.Ok(new
        {
            id = "chatcmpl-" + Guid.NewGuid().ToString("n"),
            @object = "chat.completion",
            choices = new[]
            {
                new
                {
                    index = 0,
                    message = new { role = "assistant", content = statusMessage },
                    finish_reason = "not_ready"
                }
            },
            usage = new { prompt_tokens = 0, completion_tokens = 0, total_tokens = 0 },
            _lifecycle = new { state = health.State, progress = health.Progress }
        });
    }

    var body = await JsonSerializer.DeserializeAsync<ChatRequest>(
        context.Request.Body,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    var userMessage = body?.Messages?.LastOrDefault()?.Content ?? "";

    // ── Step 1: Classify intent ──
    var intent = intentClassifier.Classify(userMessage);
    log.Router($"Intent: {intent.Intent} ({intent.Confidence:F2}) from '{userMessage[..Math.Min(50, userMessage.Length)]}'");

    // ── Step 2: Route through TaskRouter to get contextual system prompt ──
    var taskResult = await taskRouter.RouteAsync(userMessage, context.RequestAborted);

    // ── Step 3: Build message array with contextual system prompt ──
    var messages = body?.Messages?.Select(m => new Engram.Store.Inference.ChatMessage
    {
        Role = m.Role ?? "user",
        Content = m.Content ?? ""
    }).ToList() ?? new List<Engram.Store.Inference.ChatMessage>();

    // Use the task router's contextual system prompt (not generic)
    messages.Insert(0, new Engram.Store.Inference.ChatMessage
    {
        Role = "system",
        Content = taskResult.SystemPrompt
    });

    // ── Step 4: Run inference ──
    var result = await inferenceRouter.ChatCompletionAsync(messages.ToArray(), body?.MaxTokens ?? 1024, context.RequestAborted);

    if (result.Success)
    {
        // ── Step 5: Memory Pipeline: extract memories from this conversation ──
        _ = Task.Run(() =>
        {
            try
            {
                memoryPipeline.ProcessConversation(userMessage, result.Content ?? "");
                searchEngine.InvalidateIndex(); // Rebuild search index with new nodes
            }
            catch { /* Memory extraction is fire-and-forget */ }
        });

        return Results.Ok(new
        {
            id = "chatcmpl-" + Guid.NewGuid().ToString("n"),
            @object = "chat.completion",
            choices = new[]
            {
                new
                {
                    index = 0,
                    message = new { role = "assistant", content = result.Content },
                    finish_reason = "stop"
                }
            },
            usage = new
            {
                prompt_tokens = result.InputTokens,
                completion_tokens = result.OutputTokens,
                total_tokens = result.InputTokens + result.OutputTokens
            },
            model = result.Model,
            provider = result.Provider,
            _intent = new
            {
                type = intent.Intent.ToString(),
                confidence = intent.Confidence,
                routing_duration_ms = taskResult.Duration.TotalMilliseconds,
                retrieved_nodes = taskResult.RetrievedNodes.Count
            },
            _kv = new
            {
                tokensBefore = result.KvTokensBefore,
                tokensAfter = result.KvTokensAfter,
                cellsBefore = result.KvCellsBefore,
                cellsAfter = result.KvCellsAfter,
                tokensAfterCleanup = result.KvTokensAfterCleanup,
                cellsAfterCleanup = result.KvCellsAfterCleanup,
                usedFreshContext = result.UsedFreshContext,
                cleanupResult = result.CleanupResult.ToString(),
                cleanupDurationMs = result.CleanupDurationMs
            }
        });
    }

    return Results.Ok(new
    {
        id = "chatcmpl-" + Guid.NewGuid().ToString("n"),
        @object = "chat.completion",
        choices = new[]
        {
            new
            {
                index = 0,
                message = new { role = "assistant", content = "I could not generate a response. " + result.ErrorMessage },
                finish_reason = "error"
            }
        },
        usage = new { prompt_tokens = 0, completion_tokens = 0, total_tokens = 0 }
    });
});

// --- Model Status (delegates to lifecycle manager) ---
app.MapGet("/api/model/status", () =>
{
    var health = lifecycle.GetHealth();
    var config = ModelManager.Phi4Mini;
    var status = modelManager.GetStatus(config);

    return Results.Ok(new
    {
        model = config.Name,
        description = config.Description,
        state = status.State.ToString(),
        path = status.Path,
        sizeBytes = status.SizeBytes,
        progress = health.Progress,
        gpu = new { backend = health.Backend ?? "unknown", device = health.Metadata.GetValueOrDefault("gpuDevice", "?"), vramMb = health.Metadata.GetValueOrDefault("gpuVramMb", "0"), layers = health.Metadata.GetValueOrDefault("gpuLayers", "0") },
        isReady = health.IsReady,
        isLoading = health.State == "LoadingModel",
        downloadInProgress = health.State == "DownloadingModel",
        downloadError = health.State == "Error" ? health.Error : null
    });
});

// --- Download Model ---
app.MapPost("/api/model/download", () =>
{
    var config = ModelManager.Phi4Mini;
    if (modelManager.IsModelReady(config))
        return Results.Ok(new { status = "already_downloaded", path = ModelManager.GetModelPath(config) });

    // If lifecycle is already handling download (background init), just acknowledge
    if (lifecycle.State == InferenceState.DownloadingModel)
        return Results.Accepted("/api/model/status", new { status = "downloading" });

    // Manual download trigger (user clicked download before background init reached this phase)
    lifecycle.ReportDownloadProgress(0);
    log.Model("Manual download triggered");

    _ = Task.Run(async () =>
    {
        try
        {
            var progress = new Progress<ModelDownloadProgress>(p =>
            {
                lifecycle.ReportDownloadProgress(p.Progress * 100);
            });
            await modelManager.DownloadModelAsync(config, progress, CancellationToken.None);
            lifecycle.ReportDownloadComplete();
            log.Model("Manual download complete, transitioning lifecycle");
            // Lifecycle will pick up from here on next state check
        }
        catch (Exception ex)
        {
            lifecycle.ReportDownloadError(ex.Message);
            log.ModelError("Manual download failed", ex);
        }
    });

    return Results.Accepted("/api/model/status", new { status = "downloading" });
});

// --- Load Model (async via lifecycle manager) ---
app.MapPost("/api/model/load", async () =>
{
    log.Model("Model load requested via API");
    var loaded = await lifecycle.LoadModelAsync();
    var health = lifecycle.GetHealth();
    return Results.Ok(new
    {
        loaded,
        isReady = health.IsReady,
        gpu = health.Backend
    });
});

// --- Unload Model ---
app.MapPost("/api/model/unload", () =>
{
    lifecycle.UnloadModel();
    return Results.Ok(new { unloaded = true });
});

// ══════════════════════════════════════
//  EXPERIMENT CONTROL ENDPOINTS
//  For soak validation — controlled KV cache experiments
// ══════════════════════════════════════

// --- Get inference experiment mode (now shows production lifecycle status) ---
app.MapGet("/api/experiment/mode", () =>
{
    var telemetry = localEngine.GetTelemetry();
    var cleanup = telemetry.Cleanup;
    return Results.Ok(new
    {
        // Production lifecycle: KV clearing is now mandatory
        kvClearingMandatory = true,
        freshContextPerRequest = localEngine.FreshContextPerRequest,
        kvTokensInCache = localEngine.GetKvTokenCount(),
        kvUsedCells = localEngine.GetKvUsedCells(),
        totalInferences = telemetry.TotalInferences,
        totalTokensGenerated = telemetry.TotalTokensGenerated,
        // Cleanup telemetry
        cleanup = new
        {
            totalCleanups = cleanup?.TotalCleanups ?? 0,
            successfulCleanups = cleanup?.SuccessfulCleanups ?? 0,
            failedCleanups = cleanup?.FailedCleanups ?? 0,
            verificationFailures = cleanup?.VerificationFailures ?? 0,
            successRate = cleanup?.SuccessRate ?? 1.0,
            averageDurationMs = cleanup?.AverageDurationMs ?? 0
        },
        // Survivability metrics
        runtimeOperational = telemetry.RuntimeOperational,
        runtimeDegraded = telemetry.RuntimeDegraded
    });
});

// --- Set inference experiment mode (now only allows fresh context toggle) ---
app.MapPost("/api/experiment/mode", (ExperimentModeRequest request) =>
{
    // KV clearing is now mandatory, only fresh context is configurable
    if (request.FreshContext.HasValue)
        localEngine.FreshContextPerRequest = request.FreshContext.Value;

    log.Inference($"Experiment mode set: FreshCtx={localEngine.FreshContextPerRequest} (KV clearing is mandatory)");

    return Results.Ok(new
    {
        kvClearingMandatory = true,
        freshContextPerRequest = localEngine.FreshContextPerRequest
    });
});

// --- Manually clear KV cache (now triggers full cleanup pipeline) ---
app.MapPost("/api/experiment/clear-kv", () =>
{
    var tokensBefore = localEngine.GetKvTokenCount();
    var cellsBefore = localEngine.GetKvUsedCells();
    // Note: ClearKvCache() was removed — cleanup is now mandatory after each inference
    // This endpoint now shows current KV state
    var tokensAfter = localEngine.GetKvTokenCount();
    var cellsAfter = localEngine.GetKvUsedCells();

    return Results.Ok(new
    {
        note = "KV clearing is now mandatory after each inference. Use /api/experiment/mode for cleanup telemetry.",
        kvTokensCurrent = tokensAfter,
        kvCellsCurrent = cellsAfter,
        cleanupTelemetry = localEngine.GetCleanupTelemetry()
    });
});

// --- Get KV cache telemetry (now shows cleanup telemetry) ---
app.MapGet("/api/experiment/kv-status", () =>
{
    var telemetry = localEngine.GetTelemetry();
    var cleanup = telemetry.Cleanup;
    return Results.Ok(new
    {
        kvTokensInCache = telemetry.KvTokensInCache,
        kvUsedCells = telemetry.KvUsedCells,
        totalInferences = telemetry.TotalInferences,
        totalTokensGenerated = telemetry.TotalTokensGenerated,
        // Production lifecycle
        cleanup = new
        {
            totalCleanups = cleanup?.TotalCleanups ?? 0,
            successfulCleanups = cleanup?.SuccessfulCleanups ?? 0,
            failedCleanups = cleanup?.FailedCleanups ?? 0,
            verificationFailures = cleanup?.VerificationFailures ?? 0,
            successRate = cleanup?.SuccessRate ?? 1.0,
            averageDurationMs = cleanup?.AverageDurationMs ?? 0,
            maxDurationMs = cleanup?.MaxDurationMs ?? 0,
            minDurationMs = cleanup?.MinDurationMs ?? 0
        },
        // Survivability
        runtimeOperational = telemetry.RuntimeOperational,
        runtimeDegraded = telemetry.RuntimeDegraded,
        recentSuccessRate = telemetry.RecentSuccessRate
    });
});

// --- Power Mode ---
app.MapGet("/api/power-mode", () =>
{
    return Results.Ok(new
    {
        mode = inferenceRouter.PowerMode.ToString().ToLower(),
        localReady = inferenceRouter.IsLocalReady
    });
});

app.MapPost("/api/power-mode", (PowerModeRequest request) =>
{
    if (Enum.TryParse<PowerMode>(request.Mode, true, out var mode))
    {
        inferenceRouter.PowerMode = mode;
        return Results.Ok(new { mode = inferenceRouter.PowerMode.ToString().ToLower() });
    }
    return Results.BadRequest(new { error = "Invalid mode. Use 'eco' or 'turbo'." });
});

// --- Drift Alert Actions ---
app.MapPost("/api/drift/{alertId}/accept", (string alertId) =>
{
    var result = driftAlertStore.Accept(alertId);
    return result ? Results.Ok(new { status = "accepted" }) : Results.NotFound(new { error = "Alert not found" });
});

app.MapPost("/api/drift/{alertId}/dismiss", (string alertId) =>
{
    var result = driftAlertStore.Dismiss(alertId);
    return result ? Results.Ok(new { status = "dismissed" }) : Results.NotFound(new { error = "Alert not found" });
});

app.MapPost("/api/drift/{alertId}/convert", (string alertId) =>
{
    var result = driftAlertStore.Convert(alertId);
    return result ? Results.Ok(new { status = "converted" }) : Results.NotFound(new { error = "Alert not found" });
});

app.MapGet("/api/drift/stats", () =>
{
    var stats = driftAlertStore.GetStats();
    return Results.Ok(stats);
});

// --- Salience ---
app.MapGet("/api/salience", () =>
{
    var nodes = nodeStore.LoadAll();
    var scored = nodes.Select(n => new
    {
        nodeId = n.NodeId,
        title = n.Title,
        nodeType = n.NodeType.ToString(),
        salience = salienceScorer.Compute(n),
        shouldArchive = salienceScorer.ShouldArchive(n),
        lastTouchedAt = n.LastTouchedAt
    }).OrderByDescending(n => n.salience).ToList();

    return Results.Ok(new { count = scored.Count, nodes = scored });
});

// --- Archive ---
app.MapGet("/api/archive", () =>
{
    var archived = archiveManager.ListArchived();
    return Results.Ok(new
    {
        count = archived.Count,
        nodes = archived.Select(n => new
        {
            nodeId = n.NodeId,
            title = n.Title,
            nodeType = n.NodeType.ToString(),
            salience = salienceScorer.Compute(n),
            lastTouchedAt = n.LastTouchedAt
        })
    });
});

app.MapPost("/api/archive/stale", () =>
{
    var archived = archiveManager.ArchiveStaleNodes();
    return Results.Ok(new { archived = archived.Count, nodeIds = archived });
});

app.MapPost("/api/archive/{nodeId}/restore", (string nodeId) =>
{
    var result = archiveManager.RestoreFromArchive(nodeId);
    return result ? Results.Ok(new { restored = true }) : Results.NotFound(new { error = "Node not found in archive" });
});

app.MapGet("/api/archive/candidates", () =>
{
    var candidates = archiveManager.GetArchiveCandidates();
    return Results.Ok(new
    {
        count = candidates.Count,
        nodes = candidates.Select(n => new
        {
            nodeId = n.NodeId,
            title = n.Title,
            nodeType = n.NodeType.ToString(),
            salience = salienceScorer.Compute(n),
            lastTouchedAt = n.LastTouchedAt
        })
    });
});

// --- Visual Perception ---
app.MapGet("/api/perception/status", () =>
{
    return Results.Ok(new
    {
        isRunning = perceptionPipeline.IsRunning,
        framesProcessed = perceptionPipeline.FramesProcessed,
        eventsGenerated = perceptionPipeline.EventsGenerated,
        ocrAvailable = ocrService.IsAvailable
    });
});

app.MapPost("/api/perception/start", async (CancellationToken ct) =>
{
    await perceptionPipeline.StartAsync();
    return Results.Ok(new { started = true });
});

app.MapPost("/api/perception/stop", async (CancellationToken ct) =>
{
    await perceptionPipeline.StopAsync();
    return Results.Ok(new { stopped = true });
});

app.MapPost("/api/perception/capture", async () =>
{
    var result = await perceptionPipeline.ProcessSingleFrameAsync();
    return Results.Ok(new
    {
        frame = new
        {
            result.Frame.ActiveWindowTitle,
            result.Frame.ActiveWindowProcess,
            result.Frame.Width,
            result.Frame.Height,
            result.Frame.Success,
            result.Frame.ExtractedText,
            stateChanges = result.Frame.StateChanges
        },
        events = result.Events
    });
});

// --- Layout Snap ---
app.MapPost("/api/layout/snap-research", (LayoutSnapRequest request) =>
{
    var result = layoutSnap.SnapResearchLayout(request.BrowserProcess ?? "msedge", request.EditorProcess ?? "code");
    return Results.Ok(new { snapped = result });
});

app.MapPost("/api/layout/snap-left", () =>
{
    var handle = layoutSnap.GetForegroundWindow();
    if (handle == IntPtr.Zero) return Results.BadRequest(new { error = "No foreground window" });
    var result = layoutSnap.SnapLeft(handle);
    return Results.Ok(new { snapped = result });
});

app.MapPost("/api/layout/snap-right", () =>
{
    var handle = layoutSnap.GetForegroundWindow();
    if (handle == IntPtr.Zero) return Results.BadRequest(new { error = "No foreground window" });
    var result = layoutSnap.SnapRight(handle);
    return Results.Ok(new { snapped = result });
});

app.MapPost("/api/layout/maximize", () =>
{
    var handle = layoutSnap.GetForegroundWindow();
    if (handle == IntPtr.Zero) return Results.BadRequest(new { error = "No foreground window" });
    layoutSnap.Maximize(handle);
    return Results.Ok(new { maximized = true });
});

app.MapPost("/api/layout/restore", () =>
{
    var handle = layoutSnap.GetForegroundWindow();
    if (handle == IntPtr.Zero) return Results.BadRequest(new { error = "No foreground window" });
    layoutSnap.Restore(handle);
    return Results.Ok(new { restored = true });
});

// --- Security ---
app.MapGet("/api/security/status", () =>
{
    return Results.Ok(new
    {
        encryptionConfigured = keyManager.IsConfigured()
    });
});

app.MapPost("/api/security/setup", (SecuritySetupRequest request) =>
{
    try
    {
        var result = keyManager.Setup(request.Password);
        return Results.Ok(result);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/security/unlock", (SecurityUnlockRequest request) =>
{
    var encryption = keyManager.Unlock(request.Password);
    if (encryption == null)
        return Results.BadRequest(new { error = "Wrong password" });
    encryption.Dispose();
    return Results.Ok(new { unlocked = true });
});

app.MapPost("/api/security/change-password", (SecurityChangePasswordRequest request) =>
{
    var success = keyManager.ChangePassword(request.OldPassword, request.NewPassword);
    return success
        ? Results.Ok(new { changed = true })
        : Results.BadRequest(new { error = "Wrong old password" });
});

app.MapPost("/api/security/export", async (CancellationToken ct) =>
{
    var outputPath = Path.Combine(Path.GetTempPath(), $"engram-export-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.zip");
    var result = await dataExport.ExportAsync(outputPath, ct);
    return Results.Ok(result);
});

app.MapPost("/api/security/import", async (SecurityImportRequest request, CancellationToken ct) =>
{
    var result = await dataExport.ImportAsync(request.ZipPath, ct);
    return Results.Ok(result);
});

app.MapPost("/api/security/delete", () =>
{
    var result = dataDelete.DeleteAll();
    return Results.Ok(result);
});

// --- Automation ---
app.MapPost("/api/automation/plan", (AutomationPlanRequest request) =>
{
    var plan = new ActionPlan
    {
        Goal = request.Goal,
        Actions = request.Actions.Select(a => new AutomationAction
        {
            Type = Enum.Parse<ActionType>(a.Type, true),
            Description = a.Description,
            Value = a.Value,
            Target = a.Selector != null ? new ActionTarget { Selector = a.Selector } : null
        }).ToList()
    };

    // Check permissions
    foreach (var action in plan.Actions)
    {
        action.Permission = permissionGate.CheckPermission(action);
    }

    var pending = plan.Actions.Count(a => a.Permission == ActionPermission.Pending);
    plan.Status = pending > 0 ? ActionPlanStatus.PendingApproval : ActionPlanStatus.Draft;

    return Results.Ok(plan);
});

app.MapPost("/api/automation/approve", (AutomationApproveRequest request) =>
{
    if (request.PlanId != null && request.ActionId == null)
    {
        return Results.Ok(new { message = "Use plan-level approve with plan data" });
    }
    return Results.Ok(new { approved = true });
});

app.MapPost("/api/automation/approve-all", (ActionPlan plan) =>
{
    var count = permissionGate.ApproveAll(plan);
    return Results.Ok(new { approved = count });
});

app.MapPost("/api/automation/deny-all", (ActionPlan plan) =>
{
    var count = permissionGate.DenyAll(plan);
    return Results.Ok(new { denied = count });
});

app.MapPost("/api/automation/execute", async (ActionPlan plan, CancellationToken ct) =>
{
    // Auto-check permissions
    foreach (var action in plan.Actions)
    {
        if (action.Permission == ActionPermission.Pending)
            action.Permission = permissionGate.CheckPermission(action);
    }

    await actionExecutor.ExecutePlanAsync(plan, ct);
    return Results.Ok(plan);
});

app.MapGet("/api/automation/log", () =>
{
    var log = actionExecutor.GetLog();
    return Results.Ok(new { count = log.Count, log });
});

app.MapPost("/api/automation/rollback", (ActionPlan plan) =>
{
    var count = actionExecutor.Rollback(plan);
    return Results.Ok(new { rolledBack = count });
});

app.MapPost("/api/automation/pause", () =>
{
    actionRuntime.Pause();
    return Results.Ok(new { message = "Execution paused." });
});

app.MapPost("/api/automation/resume", () =>
{
    actionRuntime.Resume();
    return Results.Ok(new { message = "Execution resumed." });
});

app.MapPost("/api/automation/abort", () =>
{
    actionRuntime.Abort();
    return Results.Ok(new { message = "Execution aborted." });
});

app.MapGet("/api/automation/status", () =>
{
    var plan = actionRuntime.ActivePlan;
    var context = actionRuntime.ActiveContext;
    return Results.Ok(new
    {
        state = actionRuntime.State.ToString(),
        plan = plan != null ? new
        {
            planId = plan.PlanId,
            goal = plan.Goal,
            steps = plan.Steps.Values.Select(s => new
            {
                id = s.Id,
                status = s.Status.ToString(),
                error = s.Error,
                action = new
                {
                    type = s.Action.Type.ToString(),
                    description = s.Action.Description,
                    value = s.Action.Value,
                    status = s.Action.Status.ToString(),
                    result = s.Action.Result
                }
            }).ToList()
        } : null,
        variables = context != null ? context.Variables.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString() ?? string.Empty) : null
    });
});

app.MapPost("/api/automation/execute-plan", (ExecutionPlan plan) =>
{
    _ = Task.Run(async () =>
    {
        try
        {
            var context = new Engram.Store.Automation.ExecutionContext();
            var desktopOp = new DesktopOperator();
            context.SetVariable("DesktopOperator", desktopOp);
            
            var browserAgent = new BrowserAgentRuntime();
            context.SetVariable("BrowserAgent", browserAgent);

            await actionRuntime.ExecutePlanAsync(plan, context);
        }
        catch (Exception)
        {
            // Background errors logged or handled
        }
    });
    return Results.Accepted("/api/automation/status");
});

app.MapPost("/api/automation/cognitive/run", (CognitiveRunRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request?.Goal))
    {
        return Results.BadRequest(new { error = "Goal is required." });
    }

    _ = Task.Run(async () =>
    {
        try
        {
            var context = new Engram.Store.Automation.ExecutionContext();
            var desktopOp = new DesktopOperator();
            context.SetVariable("DesktopOperator", desktopOp);
            
            var browserAgent = new BrowserAgentRuntime();
            context.SetVariable("BrowserAgent", browserAgent);

            await cognitiveActionLoop.RunAsync(request.Goal, context);
        }
        catch (Exception)
        {
            // Background errors logged or handled
        }
    });
    return Results.Accepted("/api/automation/status");
});

// ── Phase 8 Execution World Model & Autonomous Workflows Endpoints ──
app.MapGet("/api/automation/world-model", () => Results.Ok(worldModel.GetSnapshot()));

app.MapPost("/api/automation/workflow/pause", async () =>
{
    await workflowRuntime.PauseWorkflowAsync();
    return Results.Ok(new { message = "Workflow paused" });
});

app.MapPost("/api/automation/workflow/resume", async (CancellationToken ct) =>
{
    await workflowRuntime.ResumeWorkflowAsync(ct);
    return Results.Ok(new { message = "Workflow resumed" });
});

app.MapGet("/api/automation/workflow/checkpoints", async () =>
{
    var checkpoints = await workflowStore.ListCheckpointsAsync();
    return Results.Ok(checkpoints);
});

app.MapPost("/api/automation/workflow/restore", async (RestoreWorkflowRequest request, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request?.WorkflowId))
    {
        return Results.BadRequest(new { error = "WorkflowId is required." });
    }

    var context = new Engram.Store.Automation.ExecutionContext();
    var desktopOp = new DesktopOperator();
    context.SetVariable("DesktopOperator", desktopOp);
    var browserAgent = new BrowserAgentRuntime();
    context.SetVariable("BrowserAgent", browserAgent);

    _ = Task.Run(async () =>
    {
        try
        {
            await workflowRuntime.RestoreWorkflowAsync(request.WorkflowId, context, ct);
        }
        catch (Exception)
        {
            // Background errors logged or handled
        }
    });

    return Results.Accepted("/api/automation/status");
});

app.MapGet("/api/automation/collaboration/pending", () => Results.Ok(collaborationEngine.GetPendingRequests()));

app.MapPost("/api/automation/collaboration/respond", (CollaborationResponseRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request?.RequestId))
    {
        return Results.BadRequest(new { error = "RequestId is required." });
    }

    var success = collaborationEngine.RespondToRequest(request.RequestId, request.Response, request.Approved);
    return success ? Results.Ok(new { success = true }) : Results.NotFound(new { error = "Request not found or not pending." });
});

app.MapGet("/api/automation/telemetry", () => Results.Ok(telemetryEngine.GetSummary()));

// ── Phase 8 Extension Endpoints ──
app.MapGet("/api/automation/intent/{workflowId}", (string workflowId) =>
{
    var activePlan = workflowRuntime.ActivePlan ?? new ExecutionPlan { Goal = "Active Work" };
    var activeContext = workflowRuntime.ActiveContext ?? new Engram.Store.Automation.ExecutionContext();
    var status = intentMonitor.EvaluateIntent(workflowId, activePlan, activeContext);
    return Results.Ok(status);
});

app.MapGet("/api/automation/confidence/{workflowId}", (string workflowId) =>
{
    var activePlan = workflowRuntime.ActivePlan ?? new ExecutionPlan { Goal = "Active Work" };
    var activeContext = workflowRuntime.ActiveContext ?? new Engram.Store.Automation.ExecutionContext();
    var intentStatus = intentMonitor.EvaluateIntent(workflowId, activePlan, activeContext);
    var confidence = confidenceEngine.ComputeConfidence(workflowId, activePlan, activeContext, intentStatus);
    var vitality = confidenceEngine.DetermineMultiFactorVitality(confidence, intentStatus, TimeSpan.Zero, false, 0);
    return Results.Ok(new { Confidence = confidence, VitalityState = vitality.ToString() });
});

app.MapGet("/api/automation/drift/{workflowId}", (string workflowId) =>
{
    var alerts = driftEngine.DetectDrift(workflowId);
    return Results.Ok(alerts);
});

app.MapGet("/api/automation/priorities", () =>
{
    var activeIds = new List<string>();
    if (!string.IsNullOrEmpty(workflowRuntime.ActiveWorkflowId))
    {
        activeIds.Add(workflowRuntime.ActiveWorkflowId);
    }
    else
    {
        activeIds.Add("default_workflow");
    }
    var activeContext = workflowRuntime.ActiveContext ?? new Engram.Store.Automation.ExecutionContext();
    var priorities = priorityGraph.ComputePriorities(activeIds, activeContext);
    return Results.Ok(priorities);
});

app.MapGet("/api/automation/replay/{workflowId}", async (string workflowId) =>
{
    var replay = await replayEngine.LoadReplayAsync(workflowId);
    return Results.Ok(replay);
});

app.MapGet("/api/automation/failures", async () =>
{
    var failures = await failureArchaeologyStore.GetFailuresAsync();
    return Results.Ok(failures);
});

app.MapGet("/api/automation/failures/patterns", async () =>
{
    var patterns = await failureArchaeologyStore.DetectPatternsAsync();
    return Results.Ok(patterns);
});

app.MapGet("/api/automation/environment/sync", () =>
{
    var activeContext = workflowRuntime.ActiveContext ?? new Engram.Store.Automation.ExecutionContext();
    var report = envSyncEngine.CheckSynchronization(activeContext);
    return Results.Ok(report);
});

app.MapPost("/api/automation/drift/{alertId}/dismiss", (string alertId) =>
{
    driftEngine.DismissAlert(alertId);
    return Results.Ok(new { success = true });
});

app.MapPost("/api/automation/drift/{alertId}/accept", (string alertId) =>
{
    driftEngine.AcceptAlert(alertId);
    return Results.Ok(new { success = true });
});


// --- Research Agent ---
app.MapPost("/api/research/start", async (ResearchStartRequest request, CancellationToken ct) =>
{
    var run = await researchAgent.StartResearchAsync(request.Query, ct);
    return Results.Ok(run);
});

app.MapPost("/api/research/{runId}/resume", async (string runId, CancellationToken ct) =>
{
    try
    {
        var run = await researchAgent.ResumeResearchAsync(runId, ct);
        return Results.Ok(run);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/research/{runId}/cancel", (string runId) =>
{
    researchAgent.CancelResearch(runId);
    return Results.Ok(new { cancelled = true });
});

app.MapGet("/api/research/{runId}", (string runId) =>
{
    var run = researchAgent.GetRun(runId);
    return run != null ? Results.Ok(run) : Results.NotFound(new { error = "Run not found" });
});

app.MapGet("/api/research", () =>
{
    var runs = researchAgent.ListRuns();
    return Results.Ok(new { count = runs.Count, runs });
});

// --- Google Workspace ---
app.MapGet("/api/gws/status", () =>
{
    var status = gwsManager.OAuth.GetStatus();
    return Results.Ok(status);
});

app.MapPost("/api/gws/connect", async (GwsConnectRequest request, CancellationToken ct) =>
{
    var success = await gwsManager.OAuth.ExchangeCodeAsync(
        request.Code, request.ClientId, request.ClientSecret, request.RedirectUri, ct);
    return success
        ? Results.Ok(new { connected = true, email = gwsManager.OAuth.UserEmail })
        : Results.BadRequest(new { error = "Token exchange failed" });
});

app.MapPost("/api/gws/disconnect", async (CancellationToken ct) =>
{
    var success = await gwsManager.OAuth.RevokeAsync(ct);
    return Results.Ok(new { disconnected = success });
});

app.MapGet("/api/gws/url", (string clientId, string redirectUri) =>
{
    var url = GoogleWorkspaceManager.GetAuthorizationUrl(clientId, redirectUri);
    return Results.Ok(new { url });
});

app.MapPost("/api/gws/sync", async (CancellationToken ct) =>
{
    var result = await gwsManager.SyncAllAsync(ct);
    return Results.Ok(result);
});

app.MapGet("/api/gws/emails", async (CancellationToken ct) =>
{
    var emails = await gwsManager.Gmail.GetRecentEmailsAsync(50, ct);
    return Results.Ok(new { count = emails.Count, emails });
});

app.MapGet("/api/gws/events", async (CancellationToken ct) =>
{
    var events = await gwsManager.Calendar.GetUpcomingEventsAsync(7, 50, ct);
    return Results.Ok(new { count = events.Count, events });
});

app.MapGet("/api/gws/files", async (CancellationToken ct) =>
{
    var files = await gwsManager.Drive.GetRecentFilesAsync(50, ct);
    return Results.Ok(new { count = files.Count, files });
});

// --- Token Budget ---
app.MapGet("/api/tokens", () =>
{
    var status = tokenBudget.GetStatus();
    return Results.Ok(status);
});

app.MapPost("/api/tokens/check", (TokenCheckRequest request) =>
{
    var cost = TokenPricing.CalculateCost(request.Provider ?? "gemini-flash", request.InputTokens, request.OutputTokens);
    var result = tokenBudget.CheckBudget(cost);
    return Results.Ok(new
    {
        allowed = result.IsAllowed,
        cost,
        reason = result.DenyReason,
        remainingAfter = result.RemainingAfter
    });
});

app.MapPost("/api/tokens/pack", (TokenPackRequest request) =>
{
    var amount = request.Size?.ToLowerInvariant() switch
    {
        "small" => TokenBudget.TokenPackSmall,
        "large" => TokenBudget.TokenPackLarge,
        _ => request.Amount ?? 0
    };

    if (amount <= 0)
        return Results.BadRequest(new { error = "Invalid pack size. Use 'small' (100K), 'large' (500K), or provide amount." });

    tokenBudget.AddBonusTokens(amount, $"Token pack: {request.Size ?? "custom"}");
    return Results.Ok(new { added = amount, remaining = tokenBudget.GetStatus().TokensRemaining });
});

app.MapPost("/api/tokens/tier", (TierChangeRequest request) =>
{
    tokenBudget.SetTier(request.Tier);
    return Results.Ok(tokenBudget.GetStatus());
});

app.MapGet("/api/tokens/pricing", () =>
{
    return Results.Ok(new
    {
        plans = new[]
        {
            new { name = "free", price = "$0/mo", tokens = TokenBudget.FreeTierWeeklyTokens * 4, period = "month" },
            new { name = "pro", price = "$20-30/mo", tokens = TokenBudget.ProTierMonthlyTokens, period = "month" }
        },
        packs = new[]
        {
            new { name = "small", tokens = TokenBudget.TokenPackSmall, price = "$5" },
            new { name = "large", tokens = TokenBudget.TokenPackLarge, price = "$20" }
        },
        rates = new[]
        {
            new { provider = "gemini-flash", inputCost = "1x", outputCost = "3x", description = "Cheap, fast, routine tasks" },
            new { provider = "claude-sonnet", inputCost = "10x", outputCost = "30x", description = "Expensive, complex reasoning" },
            new { provider = "local", inputCost = "0x", outputCost = "0x", description = "Free — runs on your machine" }
        }
    });
});

// --- Provider Configuration ---
app.MapGet("/api/provider", () =>
{
    var config = new EngramConfigStore(paths).Load();
    return Results.Ok(new
    {
        hasCustomProvider = !string.IsNullOrEmpty(config.CustomProviderApiKey) || !string.IsNullOrEmpty(config.CustomProviderBaseUrl),
        providerName = config.CustomProviderName ?? "none",
        baseUrl = config.CustomProviderBaseUrl ?? "",
        model = config.CustomProviderModel ?? "",
        hasApiKey = !string.IsNullOrEmpty(config.CustomProviderApiKey)
    });
});

app.MapPost("/api/provider", (ProviderConfigRequest request) =>
{
    var store = new EngramConfigStore(paths);
    var config = store.Load();
    config.CustomProviderApiKey = request.ApiKey;
    config.CustomProviderBaseUrl = request.BaseUrl;
    config.CustomProviderModel = request.Model;
    config.CustomProviderName = request.ProviderName;
    store.Save(config);
    return Results.Ok(new { saved = true });
});

// --- CopilotKit Runtime (mock) ---
app.MapPost("/v1/copilotkit", async (HttpContext context) =>
{
    var body = await new StreamReader(context.Request.Body).ReadToEndAsync();

    context.Response.ContentType = "text/event-stream";
    context.Response.Headers.Append("Cache-Control", "no-cache");

    var mockResponse = JsonSerializer.Serialize(new
    {
        type = "text",
        content = "Engram CopilotKit runtime mock. Connect the inference engine for real AI."
    });
    await context.Response.WriteAsync("data: " + mockResponse + "\n\n");
    await context.Response.WriteAsync("data: [DONE]\n\n");
});

// --- Lifecycle Logs (for debugging startup) ---
app.MapGet("/api/health/logs", (int? count) =>
{
    return Results.Ok(new { entries = InferenceLogger.Instance.GetRecent(count ?? 50) });
});

// --- Lifecycle Retry ---
app.MapPost("/api/health/retry", () =>
{
    lifecycle.Retry();
    return Results.Ok(new { state = lifecycle.State.ToString() });
});

// --- Diagnostics Export (for support + validation) ---
app.MapGet("/api/diagnostics/export", () =>
{
    var health = lifecycle.GetHealth();
    var inferenceTelemetry = localEngine.GetTelemetry();
    var cleanupTelemetry = localEngine.GetCleanupTelemetry();
    var verdicts = verdictStore.GetAll();
    var recentLogs = InferenceLogger.Instance.GetRecent(200);

    var diagnostics = new
    {
        exportedAt = DateTime.UtcNow,
        version = "1.0.0",
        appVersion = "1.0.0",

        // System info
        system = new
        {
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            runtime = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
            processorCount = Environment.ProcessorCount,
            is64Bit = Environment.Is64BitOperatingSystem,
            machineName = Environment.MachineName,
            workingSetMb = Math.Round(Environment.WorkingSet / (1024.0 * 1024.0), 1)
        },

        // Lifecycle state (single source of truth)
        lifecycle = new
        {
            state = health.State,
            backend = health.Backend,
            modelLoaded = health.ModelLoaded,
            modelName = health.ModelName,
            progress = health.Progress,
            error = health.Error,
            uptimeSeconds = health.UptimeSeconds,
            retryCount = health.RetryCount,
            isReady = health.IsReady,
            canAcceptRequests = health.CanAcceptRequests,
            stateHistory = health.StateHistory,
            metadata = health.Metadata,
            metrics = health.Metrics
        },

        // Survivability metrics
        survivability = new
        {
            runtimeOperational = health.RuntimeOperational,
            recentSuccessRate = health.RecentSuccessRate,
            consecutiveFailures = health.ConsecutiveFailures,
            generatedTokensSinceReset = health.GeneratedTokensSinceReset,
            lastSuccessfulInferenceAt = health.LastSuccessfulInferenceAt,
            runtimeDegraded = health.RuntimeDegraded
        },

        // Inference engine telemetry
        inference = new
        {
            totalInferences = inferenceTelemetry.TotalInferences,
            totalTokensGenerated = inferenceTelemetry.TotalTokensGenerated,
            totalViolations = inferenceTelemetry.TotalViolations,
            lastInferenceAt = inferenceTelemetry.LastInferenceAt,
            kvTokensInCache = inferenceTelemetry.KvTokensInCache,
            kvUsedCells = inferenceTelemetry.KvUsedCells,
            freshContextPerRequest = inferenceTelemetry.FreshContextPerRequest
        },

        // Cleanup telemetry (survivability-critical)
        cleanup = new
        {
            totalCleanups = cleanupTelemetry.TotalCleanups,
            successfulCleanups = cleanupTelemetry.SuccessfulCleanups,
            failedCleanups = cleanupTelemetry.FailedCleanups,
            verificationFailures = cleanupTelemetry.VerificationFailures,
            successRate = cleanupTelemetry.SuccessRate,
            averageDurationMs = cleanupTelemetry.AverageDurationMs,
            maxDurationMs = cleanupTelemetry.MaxDurationMs,
            minDurationMs = cleanupTelemetry.MinDurationMs,
            recentDurations = cleanupTelemetry.RecentDurations
        },

        // Backend verdicts
        backendVerdicts = verdicts,

        // Recent logs (last 200 entries)
        logs = recentLogs.Select(l => new
        {
            timestamp = l.Timestamp.ToString("HH:mm:ss.fff"),
            tag = l.Tag,
            level = l.Level,
            message = l.Message
        })
    };

    return Results.Ok(diagnostics);
});

// ── Cognitive Diagnostics (the truth layer) ──
app.MapGet("/api/cognitive/diagnostics", () =>
{
    var snapshot = cognitiveTelemetry.GetDiagnosticsSnapshot();
    return Results.Ok(snapshot);
});

// ── Cognitive Diagnostics — subsystem specific ──
app.MapGet("/api/cognitive/diagnostics/memory", () =>
    Results.Ok(cognitiveTelemetry.GetMemoryPipelineMetrics()));

app.MapGet("/api/cognitive/diagnostics/metabolism", () =>
    Results.Ok(cognitiveTelemetry.GetMetabolismMetrics()));

app.MapGet("/api/cognitive/diagnostics/deduplication", () =>
    Results.Ok(cognitiveTelemetry.GetDeduplicationMetrics()));

app.MapGet("/api/cognitive/diagnostics/contradictions", () =>
    Results.Ok(cognitiveTelemetry.GetContradictionMetrics()));

app.MapGet("/api/cognitive/diagnostics/interventions", () =>
    Results.Ok(cognitiveTelemetry.GetInterventionMetrics()));

app.MapGet("/api/cognitive/diagnostics/retrieval", () =>
    Results.Ok(cognitiveTelemetry.GetRetrievalMetrics()));

app.MapGet("/api/cognitive/diagnostics/timeline", () =>
    Results.Ok(cognitiveTelemetry.GetTimelineMetrics()));

app.MapGet("/api/cognitive/diagnostics/automation", () =>
    Results.Ok(cognitiveTelemetry.GetAutomationMetrics()));

app.MapGet("/api/cognitive/diagnostics/perception", () =>
    Results.Ok(cognitiveTelemetry.GetPerceptionMetrics()));


// ── Sprint 3: Behavioral Cognition Endpoints ──
app.MapGet("/api/cognitive/interventions", () =>
    Results.Ok(interventionStore.LoadAll()));

app.MapGet("/api/cognitive/interventions/stats", () =>
    Results.Ok(interventionStore.GetStats()));

app.MapGet("/api/cognitive/contradictions", () =>
    Results.Ok(contradictionHistoryStore.LoadAll()));

app.MapGet("/api/cognitive/contradictions/active", () =>
    Results.Ok(contradictionHistoryStore.LoadActive()));

app.MapGet("/api/cognitive/contradictions/escalating", () =>
    Results.Ok(contradictionHistoryStore.LoadEscalating()));

app.MapGet("/api/cognitive/contradictions/stats", () =>
    Results.Ok(contradictionHistoryStore.GetStats()));

app.MapGet("/api/cognitive/tensions/scores", () =>
{
    var tensionEngine = new Engram.Store.Metabolism.TensionEvolutionEngine(contradictionHistoryStore);
    return Results.Ok(tensionEngine.ScoreActiveTensions());
});

app.MapGet("/api/cognitive/tensions/clusters", () =>
{
    var tensionEngine = new Engram.Store.Metabolism.TensionEvolutionEngine(contradictionHistoryStore);
    return Results.Ok(tensionEngine.ClusterTensions());
});

// ── Phase 11: Trust, Governance & Coexistence Endpoints ──
app.MapGet("/api/governance/activity", () =>
    Results.Ok(governance.Observability.GetActivityFeed()));

app.MapGet("/api/governance/traces", (string? entityId) =>
{
    if (string.IsNullOrEmpty(entityId))
    {
        return Results.Ok(governance.Traces.GetAllTraces());
    }
    return Results.Ok(governance.Traces.GetTracesForEntity(entityId));
});

app.MapGet("/api/governance/trust", () =>
    Results.Ok(new
    {
        scores = governance.Trust.GetAllScores(),
        grants = governance.Trust.GetAllGrants(),
        autonomyCeiling = governance.Trust.AutonomyCeiling,
        interventionFrequencyMultiplier = governance.Trust.InterventionFrequencyMultiplier
    }));

app.MapPost("/api/governance/forget", (ForgetRequest request) =>
{
    governance.ForgetNode(request.NodeId);
    return Results.Ok(new { forgotten = true });
});

app.MapPost("/api/governance/dispute", (DisputeRequest request) =>
{
    governance.DisputeClaim(request.NodeId, request.ClaimId, request.CorrectedValue);
    return Results.Ok(new { disputed = true });
});

app.MapPost("/api/governance/settings", (GovernanceConfig request) =>
{
    governance.UpdateConfig(request);
    return Results.Ok(new { updated = true });
});

app.MapPost("/api/governance/recover", (RecoveryRequest request) =>
{
    governance.SafetyStateMachine.Recover(request.ResolutionDetail);
    return Results.Ok(new { state = governance.SafetyStateMachine.CurrentState.ToString() });
});

app.MapGet("/api/governance/audit", () =>
    Results.Ok(new
    {
        entries = governance.SafetyAudit.GetEntries(),
        integrityValid = governance.SafetyAudit.VerifyIntegrity()
    }));

log.Api("All endpoints registered");
log.Api($"Listening on: {string.Join(", ", app.Urls)}");

// Start non-blocking background initialization
log.Lifecycle("Starting background initialization...");
lifecycle.StartInitialization();

log.Api("=== ENGRAM API READY (accepting HTTP) ===");
log.Api("Model initialization running in background — poll /api/health for state");

app.Run();

// Request models
record ChatRequest(ChatMessage[]? Messages, int MaxTokens = 1024);
record ChatMessage(string Role, string Content);
record ExperimentModeRequest(bool? FreshContext); // KV clearing is now mandatory
record PowerModeRequest(string Mode);
record ProviderConfigRequest(string? ApiKey, string? BaseUrl, string? Model, string? ProviderName);
record TokenCheckRequest(string? Provider, int InputTokens, int OutputTokens);
record TokenPackRequest(string? Size, long? Amount);
record TierChangeRequest(string Tier);
record GwsConnectRequest(string Code, string ClientId, string ClientSecret, string RedirectUri);
record ResearchStartRequest(string Query);
record AutomationPlanRequest(string Goal, List<AutomationActionRequest> Actions);
record AutomationActionRequest(string Type, string Description, string? Value, string? Selector);
record AutomationApproveRequest(string? PlanId, string? ActionId);
record SecuritySetupRequest(string Password);
record SecurityUnlockRequest(string Password);
record SecurityChangePasswordRequest(string OldPassword, string NewPassword);
record SecurityImportRequest(string ZipPath);
record LayoutSnapRequest(string? BrowserProcess, string? EditorProcess);
record CognitiveRunRequest(string Goal);
record RestoreWorkflowRequest(string WorkflowId);
record CollaborationResponseRequest(string RequestId, string Response, bool Approved);

record ForgetRequest(string NodeId);
record DisputeRequest(string NodeId, string ClaimId, string CorrectedValue);
record RecoveryRequest(string ResolutionDetail);

// Required for WebApplicationFactory<Program> in integration tests
public partial class Program { }

