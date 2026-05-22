using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Engram.Store.Events;

namespace Engram.Store.Automation;

public enum CollaborationRequestType
{
    Clarification,
    Approval
}

public enum CollaborationRequestStatus
{
    Pending,
    Responded,
    Denied
}

public class CollaborationRequest
{
    public string RequestId { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public string WorkflowId { get; init; } = string.Empty;
    public CollaborationRequestType Type { get; init; }
    public string Prompt { get; init; } = string.Empty;
    public CollaborationRequestStatus Status { get; set; } = CollaborationRequestStatus.Pending;
    public string? Response { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RespondedAt { get; set; }
}

public class CollaborationEngine
{
    private readonly IEventBus _eventBus;
    private readonly ConcurrentDictionary<string, CollaborationRequest> _requests = new();

    public CollaborationEngine(IEventBus eventBus)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    }

    public CollaborationRequest CreateClarificationRequest(string workflowId, string question)
    {
        var request = new CollaborationRequest
        {
            WorkflowId = workflowId,
            Type = CollaborationRequestType.Clarification,
            Prompt = question
        };

        _requests[request.RequestId] = request;

        _eventBus.Publish(new EventEnvelope
        {
            EventType = "automation.collaboration.requested",
            Source = "collaboration_engine",
            Payload = request
        });

        return request;
    }

    public CollaborationRequest CreateApprovalRequest(string workflowId, string operationDescription)
    {
        var request = new CollaborationRequest
        {
            WorkflowId = workflowId,
            Type = CollaborationRequestType.Approval,
            Prompt = operationDescription
        };

        _requests[request.RequestId] = request;

        _eventBus.Publish(new EventEnvelope
        {
            EventType = "automation.collaboration.requested",
            Source = "collaboration_engine",
            Payload = request
        });

        return request;
    }

    public bool RespondToRequest(string requestId, string response, bool approved = true)
    {
        if (!_requests.TryGetValue(requestId, out var request))
        {
            return false;
        }

        if (request.Status != CollaborationRequestStatus.Pending)
        {
            return false;
        }

        request.Status = approved ? CollaborationRequestStatus.Responded : CollaborationRequestStatus.Denied;
        request.Response = response;
        request.RespondedAt = DateTimeOffset.UtcNow;

        _eventBus.Publish(new EventEnvelope
        {
            EventType = "automation.collaboration.responded",
            Source = "collaboration_engine",
            Payload = request
        });

        return true;
    }

    public IReadOnlyList<CollaborationRequest> GetPendingRequests()
    {
        return _requests.Values.Where(r => r.Status == CollaborationRequestStatus.Pending).ToList();
    }

    public CollaborationRequest? GetRequest(string requestId)
    {
        _requests.TryGetValue(requestId, out var request);
        return request;
    }
}
