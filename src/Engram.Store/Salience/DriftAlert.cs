namespace Engram.Store.Salience;

/// <summary>
/// A drift alert — detected contradiction between new events and stored wiki facts.
/// </summary>
public class DriftAlert
{
    public string AlertId { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string NodeId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DriftSeverity Severity { get; set; } = DriftSeverity.Medium;
    public List<string> SourceEventIds { get; set; } = new();
    public DriftAlertStatus Status { get; set; } = DriftAlertStatus.Pending;
    public string? Resolution { get; set; }
    public DateTimeOffset DetectedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAt { get; set; }
}

public enum DriftSeverity
{
    Low,       // Minor inconsistency
    Medium,    // Notable contradiction
    High,      // Significant conflict
    Critical   // Fundamental contradiction with identity/priorities
}

public enum DriftAlertStatus
{
    Pending,    // Awaiting user action
    Dismissed,  // User says "not a real contradiction"
    Accepted,   // User confirms drift, wiki needs update
    Converted   // Drift resolved, wiki node updated
}
