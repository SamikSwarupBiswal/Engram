using Engram.Store;
using Engram.Store.Search;
using Engram.Store.Wiki;
using Engram.Store.Salience;
using Engram.Store.Identity;
using Engram.Store.Inference;
using Engram.Store.Billing;
using Engram.Store.Google;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

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
var gpuDetector = new GpuDetector();
var modelManager = new ModelManager();
var localEngine = new LocalInferenceEngine(modelManager, gpuDetector);
var inferenceRouter = new InferenceRouter(localEngine);
var tokenBudget = new TokenBudget(paths.Config);
var gwsManager = new GoogleWorkspaceManager(paths.Config);
var modelDownloadLock = new object();
Task? modelDownloadTask = null;
ModelDownloadProgress? modelDownloadProgress = null;
string? modelDownloadError = null;

// --- Health ---
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

// --- Model Status ---
app.MapGet("/api/model/status", () =>
{
    var config = ModelManager.Phi4Mini;
    var status = modelManager.GetStatus(config);
    var gpu = gpuDetector.Detect();
    bool downloadInProgress;
    ModelDownloadProgress? latestProgress;
    string? latestError;

    lock (modelDownloadLock)
    {
        downloadInProgress = modelDownloadTask is { IsCompleted: false };
        latestProgress = modelDownloadProgress;
        latestError = modelDownloadError;
    }

    return Results.Ok(new
    {
        model = config.Name,
        description = config.Description,
        state = status.State.ToString(),
        path = status.Path,
        sizeBytes = latestProgress?.BytesDownloaded ?? status.SizeBytes,
        progress = latestProgress?.Progress ?? status.Progress,
        gpu = new { backend = gpu.Backend.ToString(), device = gpu.DeviceName, vramMb = gpu.VramMb, layers = gpu.LayerCount },
        isReady = localEngine.IsReady,
        isLoading = localEngine.IsLoading,
        downloadInProgress,
        downloadError = latestError
    });
});

// --- Download Model ---
app.MapPost("/api/model/download", () =>
{
    var config = ModelManager.Phi4Mini;

    if (modelManager.IsModelReady(config))
        return Results.Ok(new { status = "already_downloaded", path = ModelManager.GetModelPath(config) });

    lock (modelDownloadLock)
    {
        if (modelDownloadTask is { IsCompleted: false })
            return Results.Accepted("/api/model/status", new { status = "downloading" });

        modelDownloadError = null;
        modelDownloadProgress = null;

        var progress = new Progress<ModelDownloadProgress>(p =>
        {
            lock (modelDownloadLock)
            {
                modelDownloadProgress = p;
            }
        });

        modelDownloadTask = Task.Run(async () =>
        {
            try
            {
                await modelManager.DownloadModelAsync(config, progress, CancellationToken.None);
            }
            catch (Exception ex)
            {
                lock (modelDownloadLock)
                {
                    modelDownloadError = ex.Message;
                }
            }
        });
    }

    return Results.Accepted("/api/model/status", new { status = "downloading" });
});

// --- Load Model ---
app.MapPost("/api/model/load", () =>
{
    var loaded = localEngine.LoadModel();
    return Results.Ok(new
    {
        loaded,
        isReady = localEngine.IsReady,
        gpu = localEngine.GpuInfo?.Description
    });
});

// --- Unload Model ---
app.MapPost("/api/model/unload", () =>
{
    localEngine.UnloadModel();
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

Console.WriteLine("Engram API starting...");
Console.WriteLine("Workspace: " + paths.Root);

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

// Required for WebApplicationFactory<Program> in integration tests
public partial class Program { }
