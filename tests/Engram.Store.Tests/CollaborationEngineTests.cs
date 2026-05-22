using System;
using System.Collections.Generic;
using System.Linq;
using Engram.Store.Automation;
using Engram.Store.Events;
using Xunit;

namespace Engram.Store.Tests;

public class CollaborationEngineTests
{
    [Fact]
    public void CreateClarificationRequest_CreatesPendingRequestAndPublishesEvent()
    {
        // Arrange
        var eventBus = new InMemoryEventBus();
        var engine = new CollaborationEngine(eventBus);
        EventEnvelope? receivedEnvelope = null;
        using var sub = eventBus.Subscribe("automation.collaboration.requested", env => receivedEnvelope = env);

        // Act
        var request = engine.CreateClarificationRequest("workflow-123", "What is your username?");

        // Assert
        Assert.NotNull(request);
        Assert.Equal("workflow-123", request.WorkflowId);
        Assert.Equal(CollaborationRequestType.Clarification, request.Type);
        Assert.Equal("What is your username?", request.Prompt);
        Assert.Equal(CollaborationRequestStatus.Pending, request.Status);
        
        var pending = engine.GetPendingRequests();
        Assert.Single(pending);
        Assert.Equal(request.RequestId, pending[0].RequestId);

        Assert.NotNull(receivedEnvelope);
        Assert.Equal("automation.collaboration.requested", receivedEnvelope.EventType);
        var payload = (CollaborationRequest)receivedEnvelope.Payload;
        Assert.Equal(request.RequestId, payload.RequestId);
    }

    [Fact]
    public void CreateApprovalRequest_CreatesPendingRequestAndPublishesEvent()
    {
        // Arrange
        var eventBus = new InMemoryEventBus();
        var engine = new CollaborationEngine(eventBus);
        EventEnvelope? receivedEnvelope = null;
        using var sub = eventBus.Subscribe("automation.collaboration.requested", env => receivedEnvelope = env);

        // Act
        var request = engine.CreateApprovalRequest("workflow-123", "Run sudo rm -rf?");

        // Assert
        Assert.NotNull(request);
        Assert.Equal("workflow-123", request.WorkflowId);
        Assert.Equal(CollaborationRequestType.Approval, request.Type);
        Assert.Equal("Run sudo rm -rf?", request.Prompt);
        Assert.Equal(CollaborationRequestStatus.Pending, request.Status);

        Assert.NotNull(receivedEnvelope);
        Assert.Equal("automation.collaboration.requested", receivedEnvelope.EventType);
    }

    [Fact]
    public void RespondToRequest_WhenApproved_UpdatesStatusAndPublishesEvent()
    {
        // Arrange
        var eventBus = new InMemoryEventBus();
        var engine = new CollaborationEngine(eventBus);
        var request = engine.CreateClarificationRequest("workflow-123", "Question?");
        EventEnvelope? receivedEnvelope = null;
        using var sub = eventBus.Subscribe("automation.collaboration.responded", env => receivedEnvelope = env);

        // Act
        var success = engine.RespondToRequest(request.RequestId, "my-response", approved: true);

        // Assert
        Assert.True(success);
        var updated = engine.GetRequest(request.RequestId);
        Assert.NotNull(updated);
        Assert.Equal(CollaborationRequestStatus.Responded, updated.Status);
        Assert.Equal("my-response", updated.Response);
        Assert.NotNull(updated.RespondedAt);

        Assert.Empty(engine.GetPendingRequests());

        Assert.NotNull(receivedEnvelope);
        Assert.Equal("automation.collaboration.responded", receivedEnvelope.EventType);
        var payload = (CollaborationRequest)receivedEnvelope.Payload;
        Assert.Equal(request.RequestId, payload.RequestId);
        Assert.Equal(CollaborationRequestStatus.Responded, payload.Status);
    }

    [Fact]
    public void RespondToRequest_WhenDenied_UpdatesStatus()
    {
        // Arrange
        var eventBus = new InMemoryEventBus();
        var engine = new CollaborationEngine(eventBus);
        var request = engine.CreateApprovalRequest("workflow-123", "Sensitive action");

        // Act
        var success = engine.RespondToRequest(request.RequestId, "Not allowed", approved: false);

        // Assert
        Assert.True(success);
        var updated = engine.GetRequest(request.RequestId);
        Assert.NotNull(updated);
        Assert.Equal(CollaborationRequestStatus.Denied, updated.Status);
        Assert.Equal("Not allowed", updated.Response);
    }

    [Fact]
    public void RespondToRequest_WithInvalidId_ReturnsFalse()
    {
        // Arrange
        var eventBus = new InMemoryEventBus();
        var engine = new CollaborationEngine(eventBus);

        // Act
        var success = engine.RespondToRequest("invalid-id", "response");

        // Assert
        Assert.False(success);
    }

    [Fact]
    public void RespondToRequest_WhenAlreadyResponded_ReturnsFalse()
    {
        // Arrange
        var eventBus = new InMemoryEventBus();
        var engine = new CollaborationEngine(eventBus);
        var request = engine.CreateClarificationRequest("workflow-123", "Question?");
        engine.RespondToRequest(request.RequestId, "First response");

        // Act
        var success = engine.RespondToRequest(request.RequestId, "Second response");

        // Assert
        Assert.False(success);
        var updated = engine.GetRequest(request.RequestId);
        Assert.Equal("First response", updated!.Response);
    }
}
