using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Engram.Store.Automation;
using Engram.Store.Events;
using Engram.Store.Inference;
using Xunit;
using ExecutionContext = Engram.Store.Automation.ExecutionContext;

namespace Engram.Store.Tests;

public class ExecutionReasoningTests
{
    [Fact]
    public async Task ReasonAndAdaptAsync_WhenEngineNotReady_ReturnsOriginalPlan()
    {
        // Arrange
        var eventBus = new InMemoryEventBus();
        var worldModel = new OperationalWorldModel(eventBus);
        var reasoningEngine = new ExecutionReasoningEngine(worldModel, inferenceEngine: null);
        var originalPlan = new ExecutionPlan { Goal = "original" };

        // Act
        var result = await reasoningEngine.ReasonAndAdaptAsync(
            "original", originalPlan, new ExecutionContext(), "some observation", CancellationToken.None);

        // Assert
        Assert.Same(originalPlan, result);
    }

    [Fact]
    public async Task ReasonAndAdaptAsync_WhenEngineReturnsValidPlan_RepairsAndLogsMilestone()
    {
        // Arrange
        var eventBus = new InMemoryEventBus();
        var worldModel = new OperationalWorldModel(eventBus);
        var mockLlm = new MockInferenceEngine();
        mockLlm.MockResponse = @"[
          {
            ""id"": ""recovered_step"",
            ""type"": ""Click"",
            ""description"": ""Retry clicking the search button"",
            ""target"": { ""selector"": "".search-btn"" },
            ""dependsOn"": []
          }
        ]";

        var reasoningEngine = new ExecutionReasoningEngine(worldModel, mockLlm);
        var originalPlan = new ExecutionPlan { Goal = "Find stuff" };
        var context = new ExecutionContext();

        // Act
        var result = await reasoningEngine.ReasonAndAdaptAsync(
            "Find stuff", originalPlan, context, "Button was covered by a popup", CancellationToken.None);

        // Assert
        Assert.NotSame(originalPlan, result);
        Assert.Single(result.Steps);
        Assert.True(result.Steps.ContainsKey("recovered_step"));
        var step = result.Steps["recovered_step"];
        Assert.Equal(ActionType.Click, step.Action.Type);
        Assert.Equal(".search-btn", step.Action.Target?.Selector);
        Assert.Equal("Retry clicking the search button", step.Action.Description);
        
        Assert.Single(worldModel.ExecutionTrajectory);
        Assert.Contains("Plan adapted due to: Button was covered by a popup", worldModel.ExecutionTrajectory[0]);
        Assert.Equal(0.92, worldModel.ExecutionConfidence);
    }

    [Fact]
    public async Task ReasonAndAdaptAsync_WhenEngineReturnsCorruptJson_ReturnsOriginalPlan()
    {
        // Arrange
        var eventBus = new InMemoryEventBus();
        var worldModel = new OperationalWorldModel(eventBus);
        var mockLlm = new MockInferenceEngine();
        mockLlm.MockResponse = "this is not json at all";

        var reasoningEngine = new ExecutionReasoningEngine(worldModel, mockLlm);
        var originalPlan = new ExecutionPlan { Goal = "Find stuff" };

        // Act
        var result = await reasoningEngine.ReasonAndAdaptAsync(
            "Find stuff", originalPlan, new ExecutionContext(), "Observation", CancellationToken.None);

        // Assert
        Assert.Same(originalPlan, result);
    }

    private class MockInferenceEngine : LocalInferenceEngine
    {
        public override bool IsReady => true;
        public string MockResponse { get; set; } = "[]";

        public MockInferenceEngine() : base(new ModelManager(), new GpuDetector())
        {
        }

        public override Task<InferenceResult> ChatCompletionAsync(
            ChatMessage[] messages,
            int maxTokens = 1024,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new InferenceResult
            {
                Success = true,
                Content = MockResponse
            });
        }
    }
}
