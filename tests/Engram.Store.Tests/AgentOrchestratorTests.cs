using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Engram.Store.Automation;
using Engram.Store.Events;
using Xunit;

namespace Engram.Store.Tests;

public class AgentOrchestratorTests
{
    [Fact]
    public async Task DispatchTaskAsync_WithNullOrEmptyArguments_ThrowsArgumentException()
    {
        // Arrange
        var eventBus = new InMemoryEventBus();
        var worldModel = new OperationalWorldModel(eventBus);
        var orchestrator = new AgentOrchestrator(worldModel, eventBus);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => orchestrator.DispatchTaskAsync("", "some task"));
        await Assert.ThrowsAsync<ArgumentException>(() => orchestrator.DispatchTaskAsync("Research", ""));
    }

    [Fact]
    public async Task DispatchTaskAsync_Success_UpdatesWorldModelAndPublishesEvents()
    {
        // Arrange
        var eventBus = new InMemoryEventBus();
        var worldModel = new OperationalWorldModel(eventBus);
        var orchestrator = new AgentOrchestrator(worldModel, eventBus);

        var events = new List<EventEnvelope>();
        using var sub1 = eventBus.Subscribe("automation.agent.dispatched", env => events.Add(env));
        using var sub2 = eventBus.Subscribe("automation.agent.completed", env => events.Add(env));

        // Act
        var dispatchTask = orchestrator.DispatchTaskAsync("Research", "Search for laptops");

        // Verify active task is tracked during execution
        await Task.Delay(20); // Allow dispatch execution to start
        var activeTasks = orchestrator.GetActiveAgentTasks();
        Assert.True(activeTasks.ContainsKey("Research"));
        Assert.Equal("Search for laptops", activeTasks["Research"]);

        // Wait for execution to finish
        var result = await dispatchTask;

        // Assert
        Assert.Equal("Success: Research finished task 'Search for laptops'", result);
        Assert.Empty(orchestrator.GetActiveAgentTasks());

        // World Model checks
        Assert.Equal("Agent:Research", worldModel.CurrentPhase);
        Assert.Contains(worldModel.ExecutionTrajectory, m => m.Contains("Dispatched task to Research: Search for laptops"));

        // Events check
        Assert.Equal(2, events.Count);
        
        var dispatchedEvent = events.First(e => e.EventType == "automation.agent.dispatched");
        dynamic dispatchPayload = dispatchedEvent.Payload;
        Assert.Equal("Research", (string)dispatchPayload.Agent);
        Assert.Equal("Search for laptops", (string)dispatchPayload.Task);

        var completedEvent = events.First(e => e.EventType == "automation.agent.completed");
        dynamic completedPayload = completedEvent.Payload;
        Assert.Equal("Research", (string)completedPayload.Agent);
        Assert.Equal("Search for laptops", (string)completedPayload.Task);
        Assert.Equal(result, (string)completedPayload.Result);
    }

    [Fact]
    public async Task DispatchTaskAsync_OverlappingTaskOnSameAgent_ThrowsInvalidOperationException()
    {
        // Arrange
        var eventBus = new InMemoryEventBus();
        var worldModel = new OperationalWorldModel(eventBus);
        var orchestrator = new AgentOrchestrator(worldModel, eventBus);

        // Act
        var firstTask = orchestrator.DispatchTaskAsync("Research", "First task");

        // Try to dispatch a second task on the same agent before the first one completes
        await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.DispatchTaskAsync("Research", "Second task"));

        // Wait for first task to finish so test completes cleanly
        await firstTask;
    }

    [Fact]
    public async Task DispatchTaskAsync_TasksOnDifferentAgents_RunConcurrently()
    {
        // Arrange
        var eventBus = new InMemoryEventBus();
        var worldModel = new OperationalWorldModel(eventBus);
        var orchestrator = new AgentOrchestrator(worldModel, eventBus);

        // Act
        var firstTask = orchestrator.DispatchTaskAsync("Research", "Task A");
        var secondTask = orchestrator.DispatchTaskAsync("Browser", "Task B");

        // Assert no exception thrown on dispatch, verify both are tracked active
        await Task.Delay(20);
        var activeTasks = orchestrator.GetActiveAgentTasks();
        Assert.Equal(2, activeTasks.Count);
        Assert.Equal("Task A", activeTasks["Research"]);
        Assert.Equal("Task B", activeTasks["Browser"]);

        // Await completion
        await Task.WhenAll(firstTask, secondTask);

        Assert.Empty(orchestrator.GetActiveAgentTasks());
    }
}
