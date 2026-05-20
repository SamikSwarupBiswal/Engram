using Engram.Store.Orchestration;
using Xunit;

namespace Engram.Store.Tests;

public class IntentClassifierTests
{
    private readonly IntentClassifier _classifier = new();

    // ── Memory Query ──

    [Fact]
    public void Classify_MemoryQuery_WhatDoYouKnow()
    {
        var result = _classifier.Classify("What do you know about Engram?");
        Assert.Equal(IntentType.MemoryQuery, result.Intent);
        Assert.True(result.Confidence >= 0.4);
    }

    [Fact]
    public void Classify_MemoryQuery_TellMeAbout()
    {
        var result = _classifier.Classify("Tell me about my startup");
        Assert.Equal(IntentType.MemoryQuery, result.Intent);
    }

    [Fact]
    public void Classify_MemoryQuery_RememberWhen()
    {
        var result = _classifier.Classify("Remember when we discussed the architecture?");
        Assert.Equal(IntentType.MemoryQuery, result.Intent);
    }

    [Fact]
    public void Classify_MemoryQuery_WhoIs()
    {
        var result = _classifier.Classify("Who is Alex?");
        Assert.Equal(IntentType.MemoryQuery, result.Intent);
    }

    [Fact]
    public void Classify_MemoryQuery_ExtractsSubject()
    {
        var result = _classifier.Classify("What do you know about Engram?");
        Assert.Equal(IntentType.MemoryQuery, result.Intent);
        Assert.True(result.ExtractedEntities.ContainsKey("query_subject"));
    }

    // ── Timeline Query ──

    [Fact]
    public void Classify_TimelineQuery_WhatWasIDoing()
    {
        var result = _classifier.Classify("What was I doing today?");
        Assert.Equal(IntentType.TimelineQuery, result.Intent);
    }

    [Fact]
    public void Classify_TimelineQuery_ShowMyActivity()
    {
        var result = _classifier.Classify("Show me my activity this week");
        Assert.Equal(IntentType.TimelineQuery, result.Intent);
    }

    [Fact]
    public void Classify_TimelineQuery_WhatHappened()
    {
        var result = _classifier.Classify("What happened yesterday?");
        Assert.Equal(IntentType.TimelineQuery, result.Intent);
    }

    [Fact]
    public void Classify_TimelineQuery_Recent()
    {
        var result = _classifier.Classify("What have I been working on recently?");
        Assert.Equal(IntentType.TimelineQuery, result.Intent);
    }

    // ── Drift Analysis ──

    [Fact]
    public void Classify_DriftAnalysis_AmIMakingProgress()
    {
        var result = _classifier.Classify("Am I making progress on my goals?");
        Assert.Equal(IntentType.DriftAnalysis, result.Intent);
    }

    [Fact]
    public void Classify_DriftAnalysis_WhyAmIStuck()
    {
        var result = _classifier.Classify("Why am I stuck?");
        Assert.Equal(IntentType.DriftAnalysis, result.Intent);
    }

    [Fact]
    public void Classify_DriftAnalysis_WhatsBlocking()
    {
        var result = _classifier.Classify("What's blocking me?");
        Assert.Equal(IntentType.DriftAnalysis, result.Intent);
    }

    // ── Research Task ──

    [Fact]
    public void Classify_ResearchTask_FindTheBest()
    {
        var result = _classifier.Classify("Find the best GPUs under $500");
        Assert.Equal(IntentType.ResearchTask, result.Intent);
    }

    [Fact]
    public void Classify_ResearchTask_Compare()
    {
        var result = _classifier.Classify("Compare React and Vue for my project");
        Assert.Equal(IntentType.ResearchTask, result.Intent);
    }

    [Fact]
    public void Classify_ResearchTask_Recommend()
    {
        var result = _classifier.Classify("What is the best framework for building desktop apps?");
        Assert.Equal(IntentType.ResearchTask, result.Intent);
    }

    // ── Automation Task ──

    [Fact]
    public void Classify_AutomationTask_Open()
    {
        var result = _classifier.Classify("Open VSCode");
        Assert.Equal(IntentType.AutomationTask, result.Intent);
    }

    [Fact]
    public void Classify_AutomationTask_Create()
    {
        var result = _classifier.Classify("Create a GitHub repo for my new project");
        Assert.Equal(IntentType.AutomationTask, result.Intent);
    }

    [Fact]
    public void Classify_AutomationTask_Run()
    {
        var result = _classifier.Classify("Run the test suite");
        Assert.Equal(IntentType.AutomationTask, result.Intent);
    }

    [Fact]
    public void Classify_AutomationTask_Deploy()
    {
        var result = _classifier.Classify("Deploy the app to production");
        Assert.Equal(IntentType.AutomationTask, result.Intent);
    }

    // ── State Synthesis ──

    [Fact]
    public void Classify_StateSynthesis_WhatMatters()
    {
        var result = _classifier.Classify("What matters most to me right now?");
        Assert.Equal(IntentType.StateSynthesis, result.Intent);
    }

    [Fact]
    public void Classify_StateSynthesis_WhatShouldIFocus()
    {
        var result = _classifier.Classify("What should I focus on?");
        Assert.Equal(IntentType.StateSynthesis, result.Intent);
    }

    [Fact]
    public void Classify_StateSynthesis_Priority()
    {
        var result = _classifier.Classify("What's my priority?");
        Assert.Equal(IntentType.StateSynthesis, result.Intent);
    }

    // ── Conversational (fallback) ──

    [Fact]
    public void Classify_Conversational_Hello()
    {
        var result = _classifier.Classify("Hello");
        Assert.Equal(IntentType.Conversational, result.Intent);
    }

    [Fact]
    public void Classify_Conversational_HowAreYou()
    {
        var result = _classifier.Classify("How are you?");
        Assert.Equal(IntentType.Conversational, result.Intent);
    }

    [Fact]
    public void Classify_Conversational_GenericQuestion()
    {
        var result = _classifier.Classify("What's the meaning of life?");
        Assert.Equal(IntentType.Conversational, result.Intent);
    }

    // ── Edge Cases ──

    [Fact]
    public void Classify_EmptyMessage_ReturnsConversational()
    {
        var result = _classifier.Classify("");
        Assert.Equal(IntentType.Conversational, result.Intent);
    }

    [Fact]
    public void Classify_NullMessage_ReturnsConversational()
    {
        var result = _classifier.Classify(null!);
        Assert.Equal(IntentType.Conversational, result.Intent);
    }

    [Fact]
    public void Classify_WhitespaceOnly_ReturnsConversational()
    {
        var result = _classifier.Classify("   ");
        Assert.Equal(IntentType.Conversational, result.Intent);
    }

    [Fact]
    public void Classify_AlwaysReturnsConfidence()
    {
        var result = _classifier.Classify("hello");
        Assert.InRange(result.Confidence, 0.0, 1.0);
    }

    [Fact]
    public void Classify_AlwaysReturnsOriginalMessage()
    {
        var msg = "What do you know about Engram?";
        var result = _classifier.Classify(msg);
        Assert.Equal(msg, result.OriginalMessage);
    }

    // ── Entity Extraction ──

    [Fact]
    public void Classify_ResearchTask_ExtractsTopic()
    {
        var result = _classifier.Classify("Find the best GPUs under $500");
        Assert.Equal(IntentType.ResearchTask, result.Intent);
        Assert.True(result.ExtractedEntities.ContainsKey("research_topic"));
    }

    [Fact]
    public void Classify_AutomationTask_ExtractsTarget()
    {
        var result = _classifier.Classify("Open VSCode");
        Assert.Equal(IntentType.AutomationTask, result.Intent);
        Assert.True(result.ExtractedEntities.ContainsKey("automation_target"));
    }

    // ── Production-grade ──

    [Fact]
    public void Classify_UnicodeMessage_DoesNotCrash()
    {
        var result = _classifier.Classify("告诉我关于记忆系统的事情");
        Assert.NotNull(result);
    }

    [Fact]
    public void Classify_VeryLongMessage_DoesNotCrash()
    {
        var longMsg = string.Join(" ", Enumerable.Repeat("What do you know about Engram?", 100));
        var result = _classifier.Classify(longMsg);
        Assert.NotNull(result);
    }

    [Fact]
    public void Classify_SpecialCharacters_DoesNotCrash()
    {
        var result = _classifier.Classify("Open C:\\Users\\Samik\\projects");
        Assert.NotNull(result);
    }
}
