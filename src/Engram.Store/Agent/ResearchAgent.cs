using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Agent;

/// <summary>
/// Orchestrates multi-step research runs.
/// Takes a query, plans steps, executes them, collects sources,
/// synthesizes a summary with citations, and persists everything.
/// Supports pause/resume: each step is checkpointed.
/// </summary>
public class ResearchAgent : IDisposable
{
    private readonly string _researchDir;
    private readonly ILogger<ResearchAgent>? _logger;
    private readonly BrowserService _browser;
    private readonly CitationEngine _citationEngine;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    public ResearchAgent(string configDir, BrowserService? browser = null, CitationEngine? citationEngine = null, ILogger<ResearchAgent>? logger = null)
    {
        _researchDir = Path.Combine(configDir, "research");
        Directory.CreateDirectory(_researchDir);
        _logger = logger;
        _browser = browser ?? new BrowserService(logger as ILogger<BrowserService>);
        _citationEngine = citationEngine ?? new CitationEngine(logger as ILogger<CitationEngine>);
    }

    public async Task<ResearchRun> StartResearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var run = new ResearchRun
        {
            Query = query,
            Status = ResearchStatus.Running,
            Steps = PlanSteps(query),
        };
        run.TotalSteps = run.Steps.Count;

        SaveRun(run);
        _logger?.LogInformation("Research started: {Query} ({RunId})", query, run.RunId);

        try
        {
            await ExecuteRunAsync(run, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            run.Status = ResearchStatus.Paused;
            run.PausedAt = DateTimeOffset.UtcNow;
            SaveRun(run);
        }
        catch (Exception ex)
        {
            run.Status = ResearchStatus.Failed;
            run.Error = ex.Message;
            run.CompletedAt = DateTimeOffset.UtcNow;
            SaveRun(run);
        }

        return run;
    }

    public async Task<ResearchRun> ResumeResearchAsync(string runId, CancellationToken cancellationToken = default)
    {
        var run = LoadRun(runId);
        if (run == null) throw new InvalidOperationException($"Run {runId} not found");
        if (run.Status != ResearchStatus.Paused && run.Status != ResearchStatus.Failed)
            throw new InvalidOperationException($"Run {runId} is not paused or failed");

        run.Status = ResearchStatus.Running;
        run.Error = null;
        SaveRun(run);

        try
        {
            await ExecuteRunAsync(run, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            run.Status = ResearchStatus.Paused;
            run.PausedAt = DateTimeOffset.UtcNow;
            SaveRun(run);
        }
        catch (Exception ex)
        {
            run.Status = ResearchStatus.Failed;
            run.Error = ex.Message;
            run.CompletedAt = DateTimeOffset.UtcNow;
            SaveRun(run);
        }

        return run;
    }

    public void CancelResearch(string runId)
    {
        var run = LoadRun(runId);
        if (run == null) return;
        run.Status = ResearchStatus.Cancelled;
        run.CompletedAt = DateTimeOffset.UtcNow;
        SaveRun(run);
    }

    public ResearchRun? GetRun(string runId) => LoadRun(runId);

    public List<ResearchRun> ListRuns()
    {
        if (!Directory.Exists(_researchDir)) return new List<ResearchRun>();
        return Directory.GetFiles(_researchDir, "*.json")
            .Select(f => { try { return JsonSerializer.Deserialize<ResearchRun>(File.ReadAllText(f), JsonOptions); } catch { return null; } })
            .Where(r => r != null)
            .Cast<ResearchRun>()
            .OrderByDescending(r => r.CreatedAt)
            .ToList();
    }

    private List<ResearchStep> PlanSteps(string query)
    {
        return new List<ResearchStep>
        {
            new() { Type = ResearchStepType.Search, Description = $"Search for: {query}", Input = query },
            new() { Type = ResearchStepType.Scrape, Description = "Extract content from top results", Input = "top-3" },
            new() { Type = ResearchStepType.Analyze, Description = "Analyze extracted content", Input = "content" },
            new() { Type = ResearchStepType.Synthesize, Description = "Synthesize findings", Input = "facts" },
            new() { Type = ResearchStepType.CiteLink, Description = "Link citations to sources", Input = "summary" }
        };
    }

    private async Task ExecuteRunAsync(ResearchRun run, CancellationToken ct)
    {
        for (int i = run.CurrentStepIndex; i < run.Steps.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var step = run.Steps[i];
            step.Status = ResearchStepStatus.Running;
            step.StartedAt = DateTimeOffset.UtcNow;
            run.CurrentStepIndex = i;
            SaveRun(run);

            try
            {
                await ExecuteStepAsync(run, step, ct);
                step.Status = ResearchStepStatus.Completed;
                step.CompletedAt = DateTimeOffset.UtcNow;
            }
            catch (OperationCanceledException) { step.Status = ResearchStepStatus.Failed; step.Error = "Cancelled"; throw; }
            catch (Exception ex) { step.Status = ResearchStepStatus.Failed; step.Error = ex.Message; }

            SaveRun(run);
        }

        run.Status = ResearchStatus.Completed;
        run.CompletedAt = DateTimeOffset.UtcNow;
        SaveRun(run);
    }

    private async Task ExecuteStepAsync(ResearchRun run, ResearchStep step, CancellationToken ct)
    {
        switch (step.Type)
        {
            case ResearchStepType.Search: await ExecuteSearchStep(run, step, ct); break;
            case ResearchStepType.Scrape: await ExecuteScrapeStep(run, step, ct); break;
            case ResearchStepType.Analyze: ExecuteAnalyzeStep(run, step); break;
            case ResearchStepType.Synthesize: ExecuteSynthesizeStep(run, step); break;
            case ResearchStepType.CiteLink: ExecuteCiteLinkStep(run, step); break;
        }
    }

    private async Task ExecuteSearchStep(ResearchRun run, ResearchStep step, CancellationToken ct)
    {
        var results = await _browser.SearchAsync(step.Input ?? run.Query, ct);
        step.Output = $"Found {results.Count} results";
        step.SourceUrls = results.Select(r => r.Url).ToList();

        for (int i = 0; i < results.Count; i++)
        {
            run.Sources.Add(new ResearchSource
            {
                Url = results[i].Url,
                Title = results[i].Title,
                Domain = new Uri(results[i].Url).Host,
                CitationIndex = run.Sources.Count + 1,
                RelevanceScore = 1.0 - (i * 0.1)
            });
        }
    }

    private async Task ExecuteScrapeStep(ResearchRun run, ResearchStep step, CancellationToken ct)
    {
        var urls = run.Sources.Where(s => string.IsNullOrEmpty(s.ExtractedText)).Take(3).Select(s => s.Url).ToList();
        foreach (var url in urls)
        {
            ct.ThrowIfCancellationRequested();
            var content = await _browser.ExtractContentAsync(url, ct);
            var idx = run.Sources.FindIndex(s => s.Url == url);
            if (idx >= 0)
            {
                var old = run.Sources[idx];
                run.Sources[idx] = new ResearchSource
                {
                    SourceId = old.SourceId, Url = old.Url, Title = old.Title, Domain = old.Domain,
                    ExtractedText = content.Text, DiscoveredAt = old.DiscoveredAt,
                    RelevanceScore = old.RelevanceScore, CitationIndex = old.CitationIndex
                };
            }
        }
        step.Output = $"Scraped {urls.Count} pages";
    }

    private void ExecuteAnalyzeStep(ResearchRun run, ResearchStep step)
    {
        var facts = run.Sources
            .Where(s => !string.IsNullOrEmpty(s.ExtractedText))
            .SelectMany(s => s.ExtractedText.Split('\n').Where(l => l.Length > 50 && l.Length < 500).Take(5))
            .Count();
        step.Output = $"Extracted {facts} key facts";
    }

    private void ExecuteSynthesizeStep(ResearchRun run, ResearchStep step)
    {
        var sources = run.Sources.Where(s => !string.IsNullOrEmpty(s.ExtractedText)).ToList();
        if (sources.Count == 0) { run.Summary = "No sources found."; step.Output = "No sources"; return; }

        var parts = new List<string> { $"# Research: {run.Query}", $"**Sources:** {sources.Count}", "" };
        foreach (var s in sources)
        {
            var snippet = s.ExtractedText.Length > 200 ? s.ExtractedText[..200] + "..." : s.ExtractedText;
            parts.Add($"[{s.CitationIndex}] **{s.Title}** ({s.Domain})");
            parts.Add($"> {snippet}");
            parts.Add("");
        }
        run.Summary = string.Join("\n", parts);
        step.Output = $"Synthesized from {sources.Count} sources";
    }

    private void ExecuteCiteLinkStep(ResearchRun run, ResearchStep step)
    {
        var citations = _citationEngine.ExtractCitations(run.Summary ?? "");
        var valid = citations.Count(c => run.Sources.Any(s => s.CitationIndex == c.Index));
        step.Output = $"{valid}/{citations.Count} citations linked";
    }

    private void SaveRun(ResearchRun run)
    {
        try
        {
            var path = Path.Combine(_researchDir, $"{run.RunId}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(run, JsonOptions));
        }
        catch (Exception ex) { _logger?.LogError(ex, "Failed to save run {RunId}", run.RunId); }
    }

    private ResearchRun? LoadRun(string runId)
    {
        try
        {
            var path = Path.Combine(_researchDir, $"{runId}.json");
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<ResearchRun>(File.ReadAllText(path), JsonOptions);
        }
        catch { return null; }
    }

    public void Dispose()
    {
        if (!_disposed) { _browser.Dispose(); _disposed = true; }
    }
}
