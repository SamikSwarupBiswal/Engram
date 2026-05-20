using Engram.Store.Memory;
using Xunit;

namespace Engram.Store.Tests;

public class ConversationMemoryExtractorTests
{
    private readonly ConversationMemoryExtractor _extractor = new();

    // ── Person Extraction ──

    [Fact]
    public void Extract_DetectsPersonMention()
    {
        var results = _extractor.Extract("I was talking to my friend Alex about the project", "");

        Assert.Contains(results, r => r.MemoryType == MemoryType.Person && r.Title == "Alex");
    }

    [Fact]
    public void Extract_DetectsColleagueMention()
    {
        var results = _extractor.Extract("My colleague Sarah suggested a different approach", "");

        Assert.Contains(results, r => r.MemoryType == MemoryType.Person && r.Title == "Sarah");
    }

    [Fact]
    public void Extract_IgnoresCommonWords()
    {
        var results = _extractor.Extract("I was talking to my friend the about stuff", "");

        Assert.DoesNotContain(results, r => r.MemoryType == MemoryType.Person && r.Title == "the");
    }

    // ── Project Extraction ──

    [Fact]
    public void Extract_DetectsProjectBuilding()
    {
        var results = _extractor.Extract("I'm building a new app called Engram", "");

        Assert.Contains(results, r => r.MemoryType == MemoryType.Project && r.Fact.Contains("building"));
    }

    [Fact]
    public void Extract_DetectsProjectWorkingOn()
    {
        var results = _extractor.Extract("I'm working on the backend API", "");

        Assert.Contains(results, r => r.MemoryType == MemoryType.Project);
    }

    [Fact]
    public void Extract_DetectsProjectDeveloping()
    {
        var results = _extractor.Extract("I'm developing a semantic memory system", "");

        Assert.Contains(results, r => r.MemoryType == MemoryType.Project);
    }

    // ── Goal Extraction ──

    [Fact]
    public void Extract_DetectsGoalWantTo()
    {
        var results = _extractor.Extract("I want to become a better programmer", "");

        Assert.Contains(results, r => r.MemoryType == MemoryType.Goal && r.Fact.Contains("become a better programmer"));
    }

    [Fact]
    public void Extract_DetectsGoalPlanTo()
    {
        var results = _extractor.Extract("I plan to ship the product next month", "");

        Assert.Contains(results, r => r.MemoryType == MemoryType.Goal);
    }

    [Fact]
    public void Extract_DetectsGoalTryingTo()
    {
        var results = _extractor.Extract("I'm trying to reduce memory usage", "");

        Assert.Contains(results, r => r.MemoryType == MemoryType.Goal);
    }

    // ── Decision Extraction ──

    [Fact]
    public void Extract_DetectsDecisionDecidedTo()
    {
        var results = _extractor.Extract("I decided to switch from React to Vue", "");

        Assert.Contains(results, r => r.MemoryType == MemoryType.Decision && r.Fact.Contains("switch from React to Vue"));
    }

    [Fact]
    public void Extract_DetectsDecisionChoseTo()
    {
        var results = _extractor.Extract("I chose to use .NET for the backend", "");

        Assert.Contains(results, r => r.MemoryType == MemoryType.Decision);
    }

    // ── Preference Extraction ──

    [Fact]
    public void Extract_DetectsPreferenceLike()
    {
        var results = _extractor.Extract("I like dark mode for everything", "");

        Assert.Contains(results, r => r.MemoryType == MemoryType.Preference && r.Fact.Contains("dark mode"));
    }

    [Fact]
    public void Extract_DetectsPreferencePrefer()
    {
        var results = _extractor.Extract("I prefer TypeScript over JavaScript", "");

        Assert.Contains(results, r => r.MemoryType == MemoryType.Preference && r.Fact.Contains("TypeScript over JavaScript"));
    }

    [Fact]
    public void Extract_DetectsPreferenceDislike()
    {
        var results = _extractor.Extract("I don't like waiting for builds", "");

        Assert.Contains(results, r => r.MemoryType == MemoryType.Preference && r.Fact.Contains("waiting for builds"));
    }

    // ── Anxiety Extraction ──

    [Fact]
    public void Extract_DetectsAnxietyWorried()
    {
        var results = _extractor.Extract("I'm worried about the deployment going wrong", "");

        Assert.Contains(results, r => r.MemoryType == MemoryType.Anxiety && r.Fact.Contains("deployment going wrong"));
    }

    [Fact]
    public void Extract_DetectsAnxietyConcerned()
    {
        var results = _extractor.Extract("I'm concerned about the memory leaks", "");

        Assert.Contains(results, r => r.MemoryType == MemoryType.Anxiety);
    }

    // ── Task Extraction ──

    [Fact]
    public void Extract_DetectsTaskNeedTo()
    {
        var results = _extractor.Extract("I need to fix the authentication bug", "");

        Assert.Contains(results, r => r.MemoryType == MemoryType.Task && r.Fact.Contains("fix the authentication bug"));
    }

    [Fact]
    public void Extract_DetectsTaskExplicit()
    {
        var results = _extractor.Extract("Todo: write unit tests for the new module", "");

        Assert.Contains(results, r => r.MemoryType == MemoryType.Task);
    }

    [Fact]
    public void Extract_DetectsTaskGoingTo()
    {
        var results = _extractor.Extract("I'm going to refactor the database layer", "");

        Assert.Contains(results, r => r.MemoryType == MemoryType.Task);
    }

    // ── Edge Cases ──

    [Fact]
    public void Extract_EmptyMessage_ReturnsEmpty()
    {
        var results = _extractor.Extract("", "");
        Assert.Empty(results);
    }

    [Fact]
    public void Extract_NullMessage_ReturnsEmpty()
    {
        var results = _extractor.Extract(null!, "");
        Assert.Empty(results);
    }

    [Fact]
    public void Extract_WhitespaceOnly_ReturnsEmpty()
    {
        var results = _extractor.Extract("   ", "");
        Assert.Empty(results);
    }

    [Fact]
    public void Extract_ShortText_ReturnsEmpty()
    {
        var results = _extractor.Extract("hi", "");
        Assert.Empty(results);
    }

    [Fact]
    public void Extract_DeduplicatesByTitle()
    {
        var results = _extractor.Extract("I'm building Engram. I'm working on Engram.", "");

        var engramResults = results.Where(r => r.Title.Contains("Engram")).ToList();
        Assert.Single(engramResults);
    }

    [Fact]
    public void Extract_MultipleMemoriesFromOneMessage()
    {
        var message = "I'm building Engram. I want to make it remember everything. I'm worried about memory usage.";
        var results = _extractor.Extract(message, "");

        Assert.True(results.Count >= 3);
        Assert.Contains(results, r => r.MemoryType == MemoryType.Project);
        Assert.Contains(results, r => r.MemoryType == MemoryType.Goal);
        Assert.Contains(results, r => r.MemoryType == MemoryType.Anxiety);
    }

    [Fact]
    public void Extract_SetsConfidence()
    {
        var results = _extractor.Extract("I decided to use LLamaSharp for inference", "");

        var decision = results.FirstOrDefault(r => r.MemoryType == MemoryType.Decision);
        Assert.NotNull(decision);
        Assert.InRange(decision.Confidence, 0.0, 1.0);
    }

    [Fact]
    public void Extract_SetsCapturedAt()
    {
        var before = DateTimeOffset.UtcNow;
        var results = _extractor.Extract("I need to deploy the app", "");
        var after = DateTimeOffset.UtcNow;

        var task = results.FirstOrDefault(r => r.MemoryType == MemoryType.Task);
        Assert.NotNull(task);
        Assert.InRange(task.CapturedAt, before.AddSeconds(-1), after.AddSeconds(1));
    }

    [Fact]
    public void Extract_SetsSourceMessage()
    {
        var message = "I want to learn Rust programming";
        var results = _extractor.Extract(message, "");

        var goal = results.FirstOrDefault(r => r.MemoryType == MemoryType.Goal);
        Assert.NotNull(goal);
        Assert.Contains("Rust", goal.SourceMessage);
    }

    // ── Integration with assistant response ──

    [Fact]
    public void Extract_DetectsProjectInAssistantResponse()
    {
        var userMsg = "What do you think about my approach?";
        var assistantMsg = "Your project Engram sounds promising. You're building something unique.";

        var results = _extractor.Extract(userMsg, assistantMsg);

        Assert.Contains(results, r => r.MemoryType == MemoryType.Project);
    }

    [Fact]
    public void Extract_ExtractsFromBothMessages()
    {
        var userMsg = "I'm building a wiki system";
        var assistantMsg = "So you're building a semantic memory system. That's a great project to work on.";

        var results = _extractor.Extract(userMsg, assistantMsg);

        // User message yields project, assistant may also yield project
        Assert.True(results.Count >= 1);
    }

    // ── Production-grade edge cases ──

    [Fact]
    public void Extract_HandlesUnicodeText()
    {
        var results = _extractor.Extract("I'm building a system called 记忆系统 for memory", "");

        // Should not crash, may or may not extract
        Assert.NotNull(results);
    }

    [Fact]
    public void Extract_HandlesVeryLongMessage()
    {
        var longMessage = string.Join(" ", Enumerable.Repeat("I need to fix the bug in the authentication system.", 100));
        var results = _extractor.Extract(longMessage, "");

        Assert.NotNull(results);
        // Should still extract, not crash
    }

    [Fact]
    public void Extract_HandlesSpecialCharacters()
    {
        var results = _extractor.Extract("I'm building a C# app with .NET 8", "");

        Assert.NotNull(results);
    }

    [Fact]
    public void Extract_TitleLengthCapped()
    {
        var longGoal = "I want to " + string.Join(" ", Enumerable.Repeat("really", 50)) + " become a programmer";
        var results = _extractor.Extract(longGoal, "");

        var goal = results.FirstOrDefault(r => r.MemoryType == MemoryType.Goal);
        if (goal != null)
        {
            Assert.True(goal.Title.Length <= 63); // 60 + "..."
        }
    }

    [Fact]
    public void Extract_FactLengthCapped()
    {
        var longTask = "I need to " + string.Join(" ", Enumerable.Repeat("fix", 100)) + " the system";
        var results = _extractor.Extract(longTask, "");

        var task = results.FirstOrDefault(r => r.MemoryType == MemoryType.Task);
        if (task != null)
        {
            Assert.True(task.Fact.Length <= 210); // reasonable cap
        }
    }
}
