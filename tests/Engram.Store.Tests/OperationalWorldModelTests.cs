using System;
using System.Collections.Generic;
using Engram.Store.Automation;
using Engram.Store.Events;
using Xunit;

namespace Engram.Store.Tests;

public class OperationalWorldModelTests
{
    [Fact]
    public void ActiveWorkflow_SetsAndPublishesEvent()
    {
        // Arrange
        var eventBus = new InMemoryEventBus();
        var worldModel = new OperationalWorldModel(eventBus);
        EventEnvelope? receivedEnvelope = null;
        using var sub = eventBus.Subscribe("automation.worldmodel.changed", env => receivedEnvelope = env);

        // Act
        worldModel.ActiveWorkflow = "TestWorkflow";

        // Assert
        Assert.Equal("TestWorkflow", worldModel.ActiveWorkflow);
        Assert.NotNull(receivedEnvelope);
        Assert.Equal("automation.worldmodel.changed", receivedEnvelope.EventType);
        Assert.Equal("operational_world_model", receivedEnvelope.Source);
        
        dynamic payload = receivedEnvelope.Payload;
        Assert.Equal("ActiveWorkflow", (string)payload.Property);
        Assert.Equal("TestWorkflow", (string)payload.Value);
    }

    [Fact]
    public void CurrentPhase_SetsAndPublishesEvent()
    {
        // Arrange
        var eventBus = new InMemoryEventBus();
        var worldModel = new OperationalWorldModel(eventBus);
        EventEnvelope? receivedEnvelope = null;
        using var sub = eventBus.Subscribe("automation.worldmodel.changed", env => receivedEnvelope = env);

        // Act
        worldModel.CurrentPhase = "Execution";

        // Assert
        Assert.Equal("Execution", worldModel.CurrentPhase);
        Assert.NotNull(receivedEnvelope);
        dynamic payload = receivedEnvelope.Payload;
        Assert.Equal("CurrentPhase", (string)payload.Property);
        Assert.Equal("Execution", (string)payload.Value);
    }

    [Fact]
    public void BrowserTabsCount_SetsAndPublishesEvent()
    {
        // Arrange
        var eventBus = new InMemoryEventBus();
        var worldModel = new OperationalWorldModel(eventBus);
        EventEnvelope? receivedEnvelope = null;
        using var sub = eventBus.Subscribe("automation.worldmodel.changed", env => receivedEnvelope = env);

        // Act
        worldModel.BrowserTabsCount = 5;

        // Assert
        Assert.Equal(5, worldModel.BrowserTabsCount);
        Assert.NotNull(receivedEnvelope);
        dynamic payload = receivedEnvelope.Payload;
        Assert.Equal("BrowserTabsCount", (string)payload.Property);
        Assert.Equal(5, (int)payload.Value);
    }

    [Fact]
    public void ActiveDocument_SetsAndPublishesEvent()
    {
        // Arrange
        var eventBus = new InMemoryEventBus();
        var worldModel = new OperationalWorldModel(eventBus);
        EventEnvelope? receivedEnvelope = null;
        using var sub = eventBus.Subscribe("automation.worldmodel.changed", env => receivedEnvelope = env);

        // Act
        worldModel.ActiveDocument = "report.docx";

        // Assert
        Assert.Equal("report.docx", worldModel.ActiveDocument);
        Assert.NotNull(receivedEnvelope);
        dynamic payload = receivedEnvelope.Payload;
        Assert.Equal("ActiveDocument", (string)payload.Property);
        Assert.Equal("report.docx", (string)payload.Value);
    }

    [Fact]
    public void ExecutionConfidence_SetsAndPublishesEventOnlyWhenDeltaExceeded()
    {
        // Arrange
        var eventBus = new InMemoryEventBus();
        var worldModel = new OperationalWorldModel(eventBus);
        int eventCount = 0;
        using var sub = eventBus.Subscribe("automation.worldmodel.changed", env => eventCount++);

        // Act & Assert
        worldModel.ExecutionConfidence = 0.95; // Initial is 1.0, so this changes
        Assert.Equal(1, eventCount);
        Assert.Equal(0.95, worldModel.ExecutionConfidence);

        worldModel.ExecutionConfidence = 0.95001; // Delta is 0.00001, this change is < 0.0001, should NOT publish
        Assert.Equal(1, eventCount);

        worldModel.ExecutionConfidence = 0.90; // Delta is 0.05, should publish
        Assert.Equal(2, eventCount);
        Assert.Equal(0.90, worldModel.ExecutionConfidence);
    }

    [Fact]
    public void InterruptionCount_SetsAndPublishesEvent()
    {
        // Arrange
        var eventBus = new InMemoryEventBus();
        var worldModel = new OperationalWorldModel(eventBus);
        EventEnvelope? receivedEnvelope = null;
        using var sub = eventBus.Subscribe("automation.worldmodel.changed", env => receivedEnvelope = env);

        // Act
        worldModel.InterruptionCount = 3;

        // Assert
        Assert.Equal(3, worldModel.InterruptionCount);
        Assert.NotNull(receivedEnvelope);
        dynamic payload = receivedEnvelope.Payload;
        Assert.Equal("InterruptionCount", (string)payload.Property);
        Assert.Equal(3, (int)payload.Value);
    }

    [Fact]
    public void EstimatedCompletion_SetsAndPublishesEvent()
    {
        // Arrange
        var eventBus = new InMemoryEventBus();
        var worldModel = new OperationalWorldModel(eventBus);
        EventEnvelope? receivedEnvelope = null;
        using var sub = eventBus.Subscribe("automation.worldmodel.changed", env => receivedEnvelope = env);
        var timespan = TimeSpan.FromMinutes(45);

        // Act
        worldModel.EstimatedCompletion = timespan;

        // Assert
        Assert.Equal(timespan, worldModel.EstimatedCompletion);
        Assert.NotNull(receivedEnvelope);
        dynamic payload = receivedEnvelope.Payload;
        Assert.Equal("EstimatedCompletion", (string)payload.Property);
        Assert.Equal(timespan, (TimeSpan)payload.Value);
    }

    [Fact]
    public void EnvironmentalConstraints_CanSetAndRemoveConstraints()
    {
        // Arrange
        var eventBus = new InMemoryEventBus();
        var worldModel = new OperationalWorldModel(eventBus);
        var receivedEvents = new List<EventEnvelope>();
        using var sub = eventBus.Subscribe("automation.worldmodel.changed", env => receivedEvents.Add(env));

        // Act
        worldModel.SetEnvironmentalConstraint("network", "offline");
        worldModel.SetEnvironmentalConstraint("network", "online");
        worldModel.RemoveEnvironmentalConstraint("network");

        // Assert
        Assert.Empty(worldModel.EnvironmentalConstraints);
        Assert.Equal(3, receivedEvents.Count);
        
        dynamic payload1 = receivedEvents[0].Payload;
        Assert.Equal("Constraint:network", (string)payload1.Property);
        Assert.Equal("offline", (string)payload1.Value);

        dynamic payload2 = receivedEvents[1].Payload;
        Assert.Equal("Constraint:network", (string)payload2.Property);
        Assert.Equal("online", (string)payload2.Value);

        dynamic payload3 = receivedEvents[2].Payload;
        Assert.Equal("ConstraintRemoved:network", (string)payload3.Property);
        Assert.Equal(string.Empty, (string)payload3.Value);
    }

    [Fact]
    public void ExecutionTrajectory_CanAddAndClearMilestones()
    {
        // Arrange
        var eventBus = new InMemoryEventBus();
        var worldModel = new OperationalWorldModel(eventBus);
        var receivedEvents = new List<EventEnvelope>();
        using var sub = eventBus.Subscribe("automation.worldmodel.changed", env => receivedEvents.Add(env));

        // Act
        worldModel.AddTrajectoryMilestone("Milestone1");
        worldModel.AddTrajectoryMilestone("Milestone2");

        // Assert
        Assert.Equal(2, worldModel.ExecutionTrajectory.Count);
        Assert.Equal("Milestone1", worldModel.ExecutionTrajectory[0]);
        Assert.Equal("Milestone2", worldModel.ExecutionTrajectory[1]);

        worldModel.ClearTrajectory();
        Assert.Empty(worldModel.ExecutionTrajectory);

        Assert.Equal(3, receivedEvents.Count);
        
        dynamic payload1 = receivedEvents[0].Payload;
        Assert.Equal("TrajectoryAdded", (string)payload1.Property);
        Assert.Equal("Milestone1", (string)payload1.Value);

        dynamic payload3 = receivedEvents[2].Payload;
        Assert.Equal("TrajectoryCleared", (string)payload3.Property);
        Assert.Equal(string.Empty, (string)payload3.Value);
    }

    [Fact]
    public void Update_ExecutesBatchActionAndPublishesBatchEvent()
    {
        // Arrange
        var eventBus = new InMemoryEventBus();
        var worldModel = new OperationalWorldModel(eventBus);
        var receivedEvents = new List<EventEnvelope>();
        using var sub = eventBus.Subscribe("automation.worldmodel.changed", env => receivedEvents.Add(env));

        // Act
        worldModel.Update(m =>
        {
            m.ActiveWorkflow = "BatchWorkflow";
            m.CurrentPhase = "BatchPhase";
            m.BrowserTabsCount = 10;
        });

        // Assert
        Assert.Equal("BatchWorkflow", worldModel.ActiveWorkflow);
        Assert.Equal("BatchPhase", worldModel.CurrentPhase);
        Assert.Equal(10, worldModel.BrowserTabsCount);

        // Events should contain individual updates + the final batch update
        Assert.Equal(4, receivedEvents.Count);
        dynamic lastPayload = receivedEvents[3].Payload;
        Assert.Equal("BatchUpdate", (string)lastPayload.Property);
    }

    [Fact]
    public void GetSnapshot_ReturnsAllPropertiesCorrectly()
    {
        // Arrange
        var eventBus = new InMemoryEventBus();
        var worldModel = new OperationalWorldModel(eventBus);
        worldModel.ActiveWorkflow = "W1";
        worldModel.CurrentPhase = "P1";
        worldModel.BrowserTabsCount = 2;
        worldModel.ActiveDocument = "doc.txt";
        worldModel.ExecutionConfidence = 0.8;
        worldModel.InterruptionCount = 1;
        worldModel.EstimatedCompletion = TimeSpan.FromMinutes(5);
        worldModel.SetEnvironmentalConstraint("c1", "v1");
        worldModel.AddTrajectoryMilestone("m1");

        // Act
        var snapshotObj = worldModel.GetSnapshot();

        // Assert
        Assert.NotNull(snapshotObj);
        dynamic snapshot = snapshotObj;
        Assert.Equal("W1", (string)snapshot.ActiveWorkflow);
        Assert.Equal("P1", (string)snapshot.CurrentPhase);
        Assert.Equal(2, (int)snapshot.BrowserTabsCount);
        Assert.Equal("doc.txt", (string)snapshot.ActiveDocument);
        Assert.Equal(0.8, (double)snapshot.ExecutionConfidence);
        Assert.Equal(1, (int)snapshot.InterruptionCount);
        Assert.Equal(TimeSpan.FromMinutes(5).ToString(), (string)snapshot.EstimatedCompletion);
        
        var constraints = (Dictionary<string, string>)snapshot.EnvironmentalConstraints;
        Assert.True(constraints.ContainsKey("c1"));
        Assert.Equal("v1", constraints["c1"]);

        var trajectory = (List<string>)snapshot.ExecutionTrajectory;
        Assert.Single(trajectory);
        Assert.Equal("m1", trajectory[0]);
    }
}
