using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using Engram.Store.Events;

namespace Engram.Store.Automation;

public enum EscalationAction
{
    ContinueAutonomously,
    AskClarification,
    RequestApproval,
    PauseAndWait,
    AbortWorkflow
}

public class EscalationDecision
{
    public EscalationAction Action { get; set; } = EscalationAction.ContinueAutonomously;
    public string Reason { get; set; } = string.Empty;
    public double Confidence { get; set; } = 1.0;
    public bool RequiresHumanResponse { get; set; }
    public string TimeoutAction { get; set; } = "PauseAndWait";
}

public class EscalationContext
{
    public string ActionType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double Reversibility { get; set; } = 1.0; // 0.0–1.0 (0.0 = completely irreversible)
    public double ExternalImpact { get; set; } = 0.0; // 0.0–1.0 (1.0 = heavy external footprint)
    public double FinancialLegalImpact { get; set; } = 0.0; // 0.0–1.0 (1.0 = heavy cost/legal consequence)
    public double UserPreferenceHistoryScore { get; set; } = 1.0; // 0.0–1.0 (1.0 = user highly accepts this action type)
}

public class EscalationPolicyEngine
{
    private readonly CollaborationEngine _collaborationEngine;
    private readonly WorkflowConfidenceEngine _confidenceEngine;
    private readonly IEventBus _eventBus;
    private readonly ILogger? _logger;

    public EscalationPolicyEngine(
        CollaborationEngine collaborationEngine,
        WorkflowConfidenceEngine confidenceEngine,
        IEventBus eventBus,
        ILogger? logger = null)
    {
        _collaborationEngine = collaborationEngine ?? throw new ArgumentNullException(nameof(collaborationEngine));
        _confidenceEngine = confidenceEngine ?? throw new ArgumentNullException(nameof(confidenceEngine));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logger = logger;
    }

    public EscalationDecision Evaluate(string workflowId, EscalationContext context, ExecutionPlan? plan = null, ExecutionContext? execContext = null)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));

        var decision = new EscalationDecision
        {
            Action = EscalationAction.ContinueAutonomously,
            Reason = "Action is within safe autonomy thresholds.",
            Confidence = 0.95
        };

        // Determine if this is a safe autonomous action
        string desc = context.Description.ToLower();
        string typeStr = context.ActionType.ToLower();

        bool isSafeAction = desc.Contains("open tab") || desc.Contains("summarize") || 
                           desc.Contains("generate draft") || desc.Contains("organize workspace") ||
                           desc.Contains("read file") || typeStr.Contains("read") || typeStr.Contains("browse");

        bool requiresConfirmation = desc.Contains("delete") || desc.Contains("remove") ||
                                    desc.Contains("send email") || desc.Contains("purchase") ||
                                    desc.Contains("buy") || desc.Contains("pay") ||
                                    desc.Contains("post") || desc.Contains("publish") ||
                                    desc.Contains("modify important document") ||
                                    typeStr.Contains("delete") || typeStr.Contains("write");

        // 1. Financial / Legal Impact Dimension
        if (context.FinancialLegalImpact > 0.2)
        {
            decision.Action = EscalationAction.RequestApproval;
            decision.Reason = "Action has potential financial or legal impacts.";
            decision.RequiresHumanResponse = true;
            decision.TimeoutAction = "AbortWorkflow";
            
            _collaborationEngine.CreateApprovalRequest(workflowId, context.Description);
            PublishEscalation(workflowId, decision);
            return decision;
        }

        // 2. Reversibility Dimension
        if (context.Reversibility < 0.5 || requiresConfirmation)
        {
            decision.Action = EscalationAction.RequestApproval;
            decision.Reason = "Action is irreversible or requires explicit user confirmation.";
            decision.RequiresHumanResponse = true;
            decision.TimeoutAction = "PauseAndWait";
            
            _collaborationEngine.CreateApprovalRequest(workflowId, context.Description);
            PublishEscalation(workflowId, decision);
            return decision;
        }

        // 3. External Impact Dimension
        if (context.ExternalImpact > 0.4)
        {
            decision.Action = EscalationAction.RequestApproval;
            decision.Reason = "Action has high external impact (communication/broadcast).";
            decision.RequiresHumanResponse = true;
            decision.TimeoutAction = "PauseAndWait";
            
            _collaborationEngine.CreateApprovalRequest(workflowId, context.Description);
            PublishEscalation(workflowId, decision);
            return decision;
        }

        // 4. Confidence Dimension
        double confidenceVal = 1.0;
        if (plan != null && execContext != null)
        {
            var conf = _confidenceEngine.ComputeConfidence(workflowId, plan, execContext);
            confidenceVal = conf.OverallConfidence;
        }

        if (confidenceVal < 0.5)
        {
            decision.Action = EscalationAction.AskClarification;
            decision.Reason = $"Workflow confidence is low ({confidenceVal:F2}). Seeking clarification.";
            decision.RequiresHumanResponse = true;
            decision.TimeoutAction = "PauseAndWait";
            
            _collaborationEngine.CreateClarificationRequest(workflowId, $"Workflow confidence has dropped to {confidenceVal:F2}. Do you want to proceed with: {context.Description}?");
            PublishEscalation(workflowId, decision);
            return decision;
        }

        // 5. User Preference History Dimension
        if (context.UserPreferenceHistoryScore < 0.5)
        {
            decision.Action = EscalationAction.RequestApproval;
            decision.Reason = "User preference history shows high aversion or lack of prior approval for this action type.";
            decision.RequiresHumanResponse = true;
            
            _collaborationEngine.CreateApprovalRequest(workflowId, context.Description);
            PublishEscalation(workflowId, decision);
            return decision;
        }

        // Safe autonomous action (e.g. generating drafts)
        if (isSafeAction)
        {
            decision.Action = EscalationAction.ContinueAutonomously;
            decision.Reason = "Action classified as safe autonomous behavior (drafting/reading/organizing).";
        }

        return decision;
    }

    private void PublishEscalation(string workflowId, EscalationDecision decision)
    {
        _eventBus.Publish(new EventEnvelope
        {
            EventType = "automation.escalation.triggered",
            Source = "escalation_policy_engine",
            Payload = new
            {
                WorkflowId = workflowId,
                Action = decision.Action.ToString(),
                Reason = decision.Reason,
                Timestamp = DateTimeOffset.UtcNow
            }
        });
    }
}
