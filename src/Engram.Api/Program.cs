using Engram.Store;
using Engram.Store.Search;
using Engram.Store.Wiki;
using Engram.Store.Salience;
using Engram.Store.Identity;
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
var discoverySOP = new DiscoverySOP(identityStore);
var interventionPolicy = new InterventionPolicy(identityStore);

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

// --- Chat Completions (mock for now) ---
app.MapPost("/v1/chat/completions", async (HttpContext context) =>
{
    var body = await JsonSerializer.DeserializeAsync<ChatRequest>(
        context.Request.Body,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    var userMessage = body?.Messages?.LastOrDefault()?.Content ?? "";

    // TODO: Route to LLamaSharp (Eco) or cloud pipeline (Turbo)
    var response = "[Engram Mock] Received: " + userMessage + ". The inference engine is not yet connected.";

    return Results.Ok(new
    {
        id = "chatcmpl-" + Guid.NewGuid().ToString("n"),
        @object = "chat.completion",
        choices = new[]
        {
            new
            {
                index = 0,
                message = new { role = "assistant", content = response },
                finish_reason = "stop"
            }
        },
        usage = new { prompt_tokens = 0, completion_tokens = 0, total_tokens = 0 }
    });
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
record ChatRequest(ChatMessage[]? Messages);
record ChatMessage(string Role, string Content);

// Required for WebApplicationFactory<Program> in integration tests
public partial class Program { }
