using Engram.Store;
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


var builder = WebApplication.CreateBuilder(args);

// OpenAPI/Swagger
builder.Services.AddEndpointsApiExplorer();
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

// ── Lifecycle Manager (single source of truth) ──
var lifecycle = new InferenceLifecycleManager();
lifecycle.Configure(gpuDetector, modelManager, localEngine, inferenceRouter);

var tokenBudget = new TokenBudget(paths.Config);
var gwsManager = new GoogleWorkspaceManager(paths.Config);
var researchAgent = new ResearchAgent(paths.Config);
var permissionGate = new PermissionGate();
var actionExecutor = new ActionExecutor();
var keyManager = new KeyManager(paths.Config);
var dataExport = new DataExport(paths.Root);
var dataDelete = new DataDelete(paths.Root);
var screenCapture = new ScreenCaptureService();
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
app.MapPost("/v1/chat/completions", async (HttpContext context) =>
{
    var body = await JsonSerializer.DeserializeAsync<ChatRequest>(
        context.Request.Body,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    var messages = body?.Messages?.Select(m => new Engram.Store.Inference.ChatMessage
    {
        Role = m.Role ?? "user",
        Content = m.Content ?? ""
    }).ToArray() ?? Array.Empty<Engram.Store.Inference.ChatMessage>();

    var result = await inferenceRouter.ChatCompletionAsync(messages, body?.MaxTokens ?? 1024, context.RequestAborted);

    if (result.Success)
    {
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
            provider = result.Provider
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

// Required for WebApplicationFactory<Program> in integration tests
public partial class Program { }
