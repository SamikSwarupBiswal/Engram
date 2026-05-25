using System;
using System.Collections.Generic;

namespace Engram.Store.Automation;

public class UncertaintyEvent
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public UncertaintyLevel Level { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class WorkflowIdentityEnvelope
{
    public string WorkflowId { get; set; } = string.Empty;
    public List<string> IntentHistory { get; set; } = new();
    public List<string> MutationLog { get; set; } = new();
    public List<UncertaintyEvent> UncertaintyLog { get; set; } = new();
    public List<TrackedPropagation> PropagationLog { get; set; } = new();
    public List<string> VerificationLog { get; set; } = new();
}
