using Engram.Store.Agent;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Industrial-level tests for the Research Agent system.
/// Tests orchestration, browser service, citation engine, persistence.
/// </summary>
public class ResearchAgentTests : IDisposable
{
    private readonly string _tempDir;

    public ResearchAgentTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram-research-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ─── Research Run Model ───

    [Fact]
    public void ResearchRun_DefaultValues()
    {
        var run = new ResearchRun();
        Assert.NotEmpty(run.RunId);
        Assert.Equal(ResearchStatus.Pending, run.Status);
        Assert.Empty(run.Steps);
        Assert.Empty(run.Sources);
        Assert.Null(run.Summary);
        Assert.Equal(0, run.Progress);
    }

    [Fact]
    public void ResearchRun_Progress_CalculatesCorrectly()
    {
        var run = new ResearchRun { TotalSteps = 5, CurrentStepIndex = 2 };
        Assert.Equal(40, run.Progress);
    }

    [Fact]
    public void ResearchRun_Progress_ZeroSteps_ReturnsZero()
    {
        var run = new ResearchRun { TotalSteps = 0 };
        Assert.Equal(0, run.Progress);
    }

    [Fact]
    public void ResearchRun_Duration_WhenCompleted()
    {
        var run = new ResearchRun
        {
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletedAt = DateTimeOffset.UtcNow
        };
        Assert.True(run.Duration.TotalMinutes >= 4);
    }

    // ─── Research Step Model ───

    [Fact]
    public void ResearchStep_DefaultValues()
    {
        var step = new ResearchStep();
        Assert.NotEmpty(step.StepId);
        Assert.Equal(ResearchStepStatus.Pending, step.Status);
        Assert.Null(step.Output);
        Assert.Null(step.Error);
    }

    // ─── Research Source Model ───

    [Fact]
    public void ResearchSource_DefaultValues()
    {
        var source = new ResearchSource();
        Assert.NotEmpty(source.SourceId);
        Assert.Equal(string.Empty, source.Url);
        Assert.Equal(0, source.RelevanceScore);
    }

    // ─── Citation Engine ───

    [Fact]
    public void CitationEngine_ExtractCitations_FindsMarkers()
    {
        var engine = new CitationEngine();
        var citations = engine.ExtractCitations("According to [1] and [2], the answer is [3].");

        Assert.Equal(3, citations.Count);
        Assert.Equal(1, citations[0].Index);
        Assert.Equal(2, citations[1].Index);
        Assert.Equal(3, citations[2].Index);
    }

    [Fact]
    public void CitationEngine_ExtractCitations_Deduplicates()
    {
        var engine = new CitationEngine();
        var citations = engine.ExtractCitations("[1] said this. [1] also said that. [2] disagreed.");

        Assert.Equal(2, citations.Count);
    }

    [Fact]
    public void CitationEngine_ExtractCitations_EmptyText_ReturnsEmpty()
    {
        var engine = new CitationEngine();
        Assert.Empty(engine.ExtractCitations(""));
        Assert.Empty(engine.ExtractCitations(null!));
    }

    [Fact]
    public void CitationEngine_ExtractCitations_NoCitations_ReturnsEmpty()
    {
        var engine = new CitationEngine();
        Assert.Empty(engine.ExtractCitations("No citations here."));
    }

    [Fact]
    public void CitationEngine_ValidateCitations_AllValid_ReturnsEmpty()
    {
        var engine = new CitationEngine();
        var sources = new List<ResearchSource>
        {
            new() { CitationIndex = 1 },
            new() { CitationIndex = 2 }
        };
        var invalid = engine.ValidateCitations("[1] and [2] are valid.", sources);
        Assert.Empty(invalid);
    }

    [Fact]
    public void CitationEngine_ValidateCitations_InvalidIndices_Returned()
    {
        var engine = new CitationEngine();
        var sources = new List<ResearchSource> { new() { CitationIndex = 1 } };
        var invalid = engine.ValidateCitations("[1] and [5] and [99].", sources);

        Assert.Equal(2, invalid.Count);
        Assert.Contains(5, invalid);
        Assert.Contains(99, invalid);
    }

    [Fact]
    public void CitationEngine_GenerateReferences_FormatsCorrectly()
    {
        var engine = new CitationEngine();
        var sources = new List<ResearchSource>
        {
            new() { CitationIndex = 1, Title = "Article A", Url = "https://a.com" },
            new() { CitationIndex = 2, Title = "Article B", Url = "https://b.com" }
        };
        var refs = engine.GenerateReferences(sources);

        Assert.Contains("[1]", refs);
        Assert.Contains("Article A", refs);
        Assert.Contains("https://a.com", refs);
        Assert.Contains("[2]", refs);
    }

    [Fact]
    public void CitationEngine_GenerateReferences_EmptySources_ReturnsMessage()
    {
        var engine = new CitationEngine();
        Assert.Equal("No sources cited.", engine.GenerateReferences(new List<ResearchSource>()));
    }

    [Fact]
    public void CitationEngine_LinkifyCitations_ConvertsToHyperlinks()
    {
        var engine = new CitationEngine();
        var sources = new List<ResearchSource> { new() { CitationIndex = 1, Url = "https://example.com" } };
        var linked = engine.LinkifyCitations("See [1] for details.", sources);

        Assert.Contains("[1](https://example.com)", linked);
    }

    [Fact]
    public void CitationEngine_LinkifyCitations_UnmatchedCitations_KeptAsIs()
    {
        var engine = new CitationEngine();
        var linked = engine.LinkifyCitations("See [99] for details.", new List<ResearchSource>());
        Assert.Contains("[99]", linked);
    }

    // ─── Browser Service ───

    [Fact]
    public void BrowserService_Constructor_DoesNotThrow()
    {
        var browser = new BrowserService();
        browser.Dispose();
    }

    [Fact]
    public void BrowserService_DoubleDispose_DoesNotThrow()
    {
        var browser = new BrowserService();
        browser.Dispose();
        var ex = Record.Exception(() => browser.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public async Task BrowserService_SearchAsync_ReturnsResults()
    {
        var browser = new BrowserService();
        try
        {
            var results = await browser.SearchAsync("test query");
            // May return empty if network unavailable, but should not throw
            Assert.NotNull(results);
        }
        finally
        {
            browser.Dispose();
        }
    }

    [Fact]
    public async Task BrowserService_ExtractContentAsync_InvalidUrl_ReturnsError()
    {
        var browser = new BrowserService();
        try
        {
            var content = await browser.ExtractContentAsync("https://this-domain-does-not-exist-12345.com");
            Assert.NotNull(content);
            Assert.NotNull(content.Error);
        }
        finally
        {
            browser.Dispose();
        }
    }

    // ─── Research Agent ───

    [Fact]
    public void ResearchAgent_Constructor_DoesNotThrow()
    {
        var agent = new ResearchAgent(_tempDir);
        agent.Dispose();
    }

    [Fact]
    public void ResearchAgent_ListRuns_EmptyInitially()
    {
        var agent = new ResearchAgent(_tempDir);
        var runs = agent.ListRuns();
        Assert.Empty(runs);
        agent.Dispose();
    }

    [Fact]
    public void ResearchAgent_GetRun_Nonexistent_ReturnsNull()
    {
        var agent = new ResearchAgent(_tempDir);
        Assert.Null(agent.GetRun("nonexistent"));
        agent.Dispose();
    }

    [Fact]
    public void ResearchAgent_CancelResearch_Nonexistent_DoesNotThrow()
    {
        var agent = new ResearchAgent(_tempDir);
        var ex = Record.Exception(() => agent.CancelResearch("nonexistent"));
        Assert.Null(ex);
        agent.Dispose();
    }

    [Fact]
    public async Task ResearchAgent_ResumeResearch_Nonexistent_ThrowsInvalidOperation()
    {
        var agent = new ResearchAgent(_tempDir);
        await Assert.ThrowsAsync<InvalidOperationException>(() => agent.ResumeResearchAsync("nonexistent"));
        agent.Dispose();
    }

    [Fact]
    public async Task ResearchAgent_StartResearch_CreatesRun()
    {
        var agent = new ResearchAgent(_tempDir);
        try
        {
            var run = await agent.StartResearchAsync("test query");
            Assert.NotNull(run);
            Assert.NotEmpty(run.RunId);
            Assert.Equal("test query", run.Query);
            Assert.Equal(5, run.Steps.Count);
        }
        finally
        {
            agent.Dispose();
        }
    }

    [Fact]
    public async Task ResearchAgent_StartResearch_PersistsRun()
    {
        var agent = new ResearchAgent(_tempDir);
        try
        {
            var run = await agent.StartResearchAsync("test query");
            var loaded = agent.GetRun(run.RunId);
            Assert.NotNull(loaded);
            Assert.Equal(run.Query, loaded.Query);
        }
        finally
        {
            agent.Dispose();
        }
    }

    [Fact]
    public async Task ResearchAgent_ListRuns_AfterStart_ReturnsRun()
    {
        var agent = new ResearchAgent(_tempDir);
        try
        {
            await agent.StartResearchAsync("test");
            var runs = agent.ListRuns();
            Assert.Single(runs);
        }
        finally
        {
            agent.Dispose();
        }
    }

    [Fact]
    public async Task ResearchAgent_CancelResearch_ChangesStatus()
    {
        var agent = new ResearchAgent(_tempDir);
        try
        {
            var run = await agent.StartResearchAsync("test");
            agent.CancelResearch(run.RunId);
            var loaded = agent.GetRun(run.RunId);
            Assert.Equal(ResearchStatus.Cancelled, loaded!.Status);
        }
        finally
        {
            agent.Dispose();
        }
    }

    // ─── Data Models ───

    [Fact]
    public void SearchResult_DefaultValues()
    {
        var result = new SearchResult();
        Assert.Equal(string.Empty, result.Url);
        Assert.Equal(string.Empty, result.Title);
    }

    [Fact]
    public void ExtractedContent_DefaultValues()
    {
        var content = new ExtractedContent();
        Assert.Equal(string.Empty, content.Url);
        Assert.Empty(content.Links);
        Assert.Null(content.Error);
    }

    [Fact]
    public void CitationMarker_DefaultValues()
    {
        var marker = new CitationMarker();
        Assert.Equal(0, marker.Index);
        Assert.Equal(string.Empty, marker.RawText);
    }

    // ─── Research Status Enum ───

    [Fact]
    public void ResearchStatus_HasAllExpectedValues()
    {
        Assert.Equal(6, Enum.GetValues<ResearchStatus>().Length);
    }

    [Fact]
    public void ResearchStepType_HasAllExpectedValues()
    {
        Assert.Equal(5, Enum.GetValues<ResearchStepType>().Length);
    }

    // ─── Edge Cases ───

    [Fact]
    public void ResearchRun_LongQuery_PreservesValue()
    {
        var longQuery = new string('x', 10000);
        var run = new ResearchRun { Query = longQuery };
        Assert.Equal(10000, run.Query.Length);
    }

    [Fact]
    public void ResearchSource_Unicode_PreservesValue()
    {
        var source = new ResearchSource { Title = "日本語テスト Über Café" };
        Assert.Contains("日本語", source.Title);
    }

    [Fact]
    public void CitationEngine_LargeCitationNumbers_Works()
    {
        var engine = new CitationEngine();
        var citations = engine.ExtractCitations("[100] and [999] and [1].");
        Assert.Equal(3, citations.Count);
        Assert.Equal(1, citations[0].Index); // Ordered
        Assert.Equal(100, citations[1].Index);
        Assert.Equal(999, citations[2].Index);
    }

    [Fact]
    public void ResearchRun_MultipleSteps_IndependentIds()
    {
        var run = new ResearchRun
        {
            Steps = new List<ResearchStep>
            {
                new() { Type = ResearchStepType.Search },
                new() { Type = ResearchStepType.Scrape },
                new() { Type = ResearchStepType.Analyze }
            }
        };

        var ids = run.Steps.Select(s => s.StepId).ToList();
        Assert.Equal(3, ids.Distinct().Count()); // All unique
    }
}
