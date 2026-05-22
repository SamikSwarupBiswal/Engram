using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Engram.Store.Events;

namespace Engram.Store.Automation;

public class AgentOrchestrator
{
    private readonly OperationalWorldModel _worldModel;
    private readonly IEventBus _eventBus;
    private readonly ConcurrentDictionary<string, string> _activeAgentTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public AgentOrchestrator(OperationalWorldModel worldModel, IEventBus eventBus)
    {
        _worldModel = worldModel ?? throw new ArgumentNullException(nameof(worldModel));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    }

    public async Task<string> DispatchTaskAsync(string agentName, string taskDescription, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(agentName)) throw new ArgumentException("Agent name cannot be empty", nameof(agentName));
        if (string.IsNullOrEmpty(taskDescription)) throw new ArgumentException("Task description cannot be empty", nameof(taskDescription));

        // Prevent overlapping tasks on the same agent
        lock (_lock)
        {
            if (_activeAgentTasks.ContainsKey(agentName))
            {
                throw new InvalidOperationException($"Agent '{agentName}' is already busy executing a task.");
            }
            _activeAgentTasks[agentName] = taskDescription;
        }

        // Update the operational world model phase and active workflow metadata
        _worldModel.CurrentPhase = $"Agent:{agentName}";
        _worldModel.AddTrajectoryMilestone($"Dispatched task to {agentName}: {taskDescription}");

        _eventBus.Publish(new EventEnvelope
        {
            EventType = $"automation.agent.dispatched",
            Source = "agent_orchestrator",
            Payload = new { Agent = agentName, Task = taskDescription }
        });

        try
        {
            // Simulate agent work/routing latency
            await Task.Delay(100, ct);

            // Construct result details
            var result = $"Success: {agentName} finished task '{taskDescription}'";

            _eventBus.Publish(new EventEnvelope
            {
                EventType = $"automation.agent.completed",
                Source = "agent_orchestrator",
                Payload = new { Agent = agentName, Task = taskDescription, Result = result }
            });

            return result;
        }
        finally
        {
            lock (_lock)
            {
                _activeAgentTasks.TryRemove(agentName, out _);
            }
        }
    }

    public IReadOnlyDictionary<string, string> GetActiveAgentTasks()
    {
        return new Dictionary<string, string>(_activeAgentTasks);
    }
}
