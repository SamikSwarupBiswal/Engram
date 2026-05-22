using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Engram.Store.Automation;
using Engram.Store.Inference;
using Xunit;
using ExecutionContext = Engram.Store.Automation.ExecutionContext;

namespace Engram.Store.Tests;

public class CognitiveActionLoopTests
{
    private class FakeInferenceEngine : LocalInferenceEngine
    {
        public bool IsReadyOverride { get; set; } = true;
        public Queue<string> Responses { get; } = new();

        public FakeInferenceEngine() 
            : base(new ModelManager(), new GpuDetector())
        {
        }

        public override bool IsReady => IsReadyOverride;

        public override Task<InferenceResult> ChatCompletionAsync(
            ChatMessage[] messages,
            int maxTokens = 1024,
            CancellationToken cancellationToken = default)
        {
            if (Responses.Count == 0)
            {
                return Task.FromResult(InferenceResult.Failed("No mocked response configured."));
            }

            var nextResponse = Responses.Dequeue();
            return Task.FromResult(new InferenceResult
            {
                Success = true,
                Content = nextResponse
            });
        }
    }

    [Fact]
    public async Task RunAsync_InitialPlanSucceeds_NoReplanning()
    {
        // Arrange
        var executor = new ActionExecutor();
        var permissionGate = new PermissionGate();
        var runtime = new ActionRuntime(executor, permissionGate);
        
        // We will use heuristics for initial planning to keep it simple, or LLM
        var fakeLlm = new FakeInferenceEngine();
        
        // Return a valid initial plan from LLM
        fakeLlm.Responses.Enqueue(@"[
            {
                ""id"": ""step_1"",
                ""type"": ""Wait"",
                ""description"": ""Wait for 10ms"",
                ""value"": ""10"",
                ""dependsOn"": []
            }
        ]");

        var planner = new TaskPlanner(fakeLlm);
        var loop = new CognitiveActionLoop(planner, runtime, fakeLlm);
        var context = new ExecutionContext();

        // Act
        var result = await loop.RunAsync("wait for 10ms", context);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(0, result.ReplanCount);
        Assert.Equal(1, result.StepsExecuted);
        Assert.Contains("completed successfully", result.History[^1]);
    }

    [Fact]
    public async Task RunAsync_StepFails_TriggerReplanning_Succeeds()
    {
        // Arrange
        var executor = new ActionExecutor();
        var permissionGate = new PermissionGate();
        var runtime = new ActionRuntime(executor, permissionGate);
        var fakeLlm = new FakeInferenceEngine();

        // Initial plan:
        // step_1: Type without target (this will fail)
        fakeLlm.Responses.Enqueue(@"[
            {
                ""id"": ""step_1"",
                ""type"": ""Type"",
                ""description"": ""Type into input"",
                ""value"": ""test"",
                ""dependsOn"": []
            }
        ]");

        // Repaired plan proposed after failure:
        // step_2: Wait for 5ms (this will succeed)
        fakeLlm.Responses.Enqueue(@"[
            {
                ""id"": ""step_2"",
                ""type"": ""Wait"",
                ""description"": ""Wait instead of typing"",
                ""value"": ""5"",
                ""dependsOn"": []
            }
        ]");

        var planner = new TaskPlanner(fakeLlm);
        var loop = new CognitiveActionLoop(planner, runtime, fakeLlm);
        var context = new ExecutionContext();

        // Act
        var result = await loop.RunAsync("perform task", context);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.ReplanCount);
        Assert.Equal(1, result.StepsExecuted); // step_1 failed (0 executed steps), step_2 succeeded (1 executed step)
        Assert.Contains("Plan repaired successfully", result.History[4]);
        Assert.Contains("completed successfully", result.History[^1]);
    }

    [Fact]
    public async Task RunAsync_MaxReplansExceeded_ReturnsFailure()
    {
        // Arrange
        var executor = new ActionExecutor();
        var permissionGate = new PermissionGate();
        var runtime = new ActionRuntime(executor, permissionGate);
        var fakeLlm = new FakeInferenceEngine();

        // Initial plan:
        // step_1: Type without target (will fail)
        fakeLlm.Responses.Enqueue(@"[
            {
                ""id"": ""step_1"",
                ""type"": ""Type"",
                ""description"": ""Type fails"",
                ""value"": ""test"",
                ""dependsOn"": []
            }
        ]");

        // Replan 1 (still fails):
        fakeLlm.Responses.Enqueue(@"[
            {
                ""id"": ""step_r1"",
                ""type"": ""Type"",
                ""description"": ""Type fails 2"",
                ""value"": ""test"",
                ""dependsOn"": []
            }
        ]");

        // Replan 2 (still fails):
        fakeLlm.Responses.Enqueue(@"[
            {
                ""id"": ""step_r2"",
                ""type"": ""Type"",
                ""description"": ""Type fails 3"",
                ""value"": ""test"",
                ""dependsOn"": []
            }
        ]");

        // Replan 3 (still fails):
        fakeLlm.Responses.Enqueue(@"[
            {
                ""id"": ""step_r3"",
                ""type"": ""Type"",
                ""description"": ""Type fails 4"",
                ""value"": ""test"",
                ""dependsOn"": []
            }
        ]");

        var planner = new TaskPlanner(fakeLlm);
        var loop = new CognitiveActionLoop(planner, runtime, fakeLlm);
        var context = new ExecutionContext();

        // Act
        var result = await loop.RunAsync("fail goal", context);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(3, result.ReplanCount); // Replan count should be capped at 3
        Assert.Contains("Maximum replan attempts", result.Message);
    }

    [Fact]
    public async Task RunAsync_NoLlmReady_NoReplanning()
    {
        // Arrange
        var executor = new ActionExecutor();
        var permissionGate = new PermissionGate();
        var runtime = new ActionRuntime(executor, permissionGate);
        var fakeLlm = new FakeInferenceEngine();
        fakeLlm.IsReadyOverride = false; // LLM is not ready

        // Initial plan via fallback heuristics
        var planner = new TaskPlanner(fakeLlm);
        var loop = new CognitiveActionLoop(planner, runtime, fakeLlm);
        var context = new ExecutionContext();

        // Act - running a task that fails due to safety violation
        var result = await loop.RunAsync("go to http://127.0.0.1", context);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(0, result.ReplanCount); // Should not replan
        Assert.Contains("cannot replan", result.Message);
    }
}
