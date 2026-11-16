using System.Text.Json.Serialization;

namespace Engram.Store.Automation.Plugins;

public static class AutomationPluginApi
{
    public const string CurrentVersion = "1.0";
}

public enum AutomationPluginIsolation
{
    TrustedInProcess,
    ExternalProcess
}

public enum AutomationPluginTrust
{
    FirstParty,
    VerifiedPublisher
}

public enum AutomationPluginLifecycleState
{
    Discovered,
    Initializing,
    Ready,
    Degraded,
    Quarantined,
    Disabled,
    Stopped
}

public enum AutomationPluginPermission
{
    BrowserRead,
    BrowserWrite,
    Network,
    LocalFileRead,
    LocalFileWrite,
    DesktopLaunch,
    OfficeRead,
    OfficeWrite,
    GlobalInput,
    Shell,
    Credentials
}

public enum AutomationPluginArgumentType
{
    String,
    Integer,
    HttpUrl,
    Base64
}

public sealed class AutomationPluginManifest
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Version { get; init; }
    public required string Publisher { get; init; }
    public string ApiVersion { get; init; } = AutomationPluginApi.CurrentVersion;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AutomationPluginIsolation Isolation { get; init; } = AutomationPluginIsolation.ExternalProcess;

    public required string EntryPoint { get; init; }
    public string? PackageUrl { get; init; }
    public int TimeoutMilliseconds { get; init; } = 30_000;
    public int MaxConcurrency { get; init; } = 1;
    public List<AutomationPluginCapability> Capabilities { get; init; } = new();
    public PluginIntegrityDefinition? Integrity { get; init; }
}

public sealed class AutomationPluginCapability
{
    public required string Id { get; init; }
    public string Description { get; init; } = string.Empty;
    public int Priority { get; init; } = 100;
    public List<AutomationPluginPermission> RequiredPermissions { get; init; } = new();
    public List<AutomationPluginArgumentDefinition> Arguments { get; init; } = new();
}

public sealed class AutomationPluginArgumentDefinition
{
    public required string Name { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AutomationPluginArgumentType Type { get; init; } = AutomationPluginArgumentType.String;
    public bool Required { get; init; } = true;
    public int MaxLength { get; init; } = 4096;
    public long? Minimum { get; init; }
    public long? Maximum { get; init; }
    public string? Pattern { get; init; }
}

public sealed class PluginIntegrityDefinition
{
    public required string EntryPointSha256 { get; init; }
    public required string PublisherKeyId { get; init; }
    public required string Signature { get; init; }
}

public sealed record AutomationPluginPackage(
    AutomationPluginManifest Manifest,
    string DirectoryPath,
    string? EntryPointPath,
    AutomationPluginTrust Trust,
    string IntegrityHash);

public sealed record AutomationPluginInvocation(
    string InvocationId,
    string CorrelationId,
    AutomationAction Action,
    ExecutionContext ExecutionContext,
    AutomationPluginCapability Capability,
    DateTimeOffset StartedAt);

public sealed record AutomationPluginResult(
    string Output,
    bool Verified,
    IReadOnlyList<AutomationOwnershipHandle> Ownership,
    IReadOnlyDictionary<string, string>? Evidence = null);

public sealed record AutomationPluginDispatchResult(
    bool Handled,
    string? PluginId,
    string? PluginVersion,
    string? CapabilityId,
    string? ResolutionReason,
    AutomationPluginResult? Result)
{
    public static AutomationPluginDispatchResult NotHandled { get; } = new(false, null, null, null, null, null);
}

public sealed record AutomationPluginCleanupItem(
    AutomationOwnershipHandle Ownership,
    bool Cleaned,
    string? Error);

public sealed record AutomationPluginCleanupResult(
    string CorrelationId,
    IReadOnlyList<AutomationPluginCleanupItem> Items,
    bool Complete);

public sealed record AutomationPluginStatus(
    string Id,
    string DisplayName,
    string Version,
    string Publisher,
    AutomationPluginTrust Trust,
    AutomationPluginIsolation Isolation,
    AutomationPluginLifecycleState State,
    bool Enabled,
    IReadOnlyList<string> Capabilities,
    long SuccessfulInvocations,
    long FailedInvocations,
    int ConsecutiveFailures,
    DateTimeOffset? QuarantinedUntil,
    string? LastError,
    double? LastDurationMilliseconds);

public sealed record AutomationPluginAuditRecord(
    DateTimeOffset Timestamp,
    string InvocationId,
    string CorrelationId,
    string PluginId,
    string PluginVersion,
    string CapabilityId,
    string ActionId,
    ActionType ActionType,
    string PermissionDecision,
    string ResolutionReason,
    string State,
    double DurationMilliseconds,
    string RequestSha256,
    bool Verified,
    string? EvidenceSha256,
    IReadOnlyList<string> EvidenceKeys,
    IReadOnlyList<AutomationOwnershipHandle> Ownership,
    string? Error);

public interface IAutomationPlugin
{
    AutomationPluginManifest Manifest { get; }
    Task InitializeAsync(CancellationToken ct = default);
    Task<AutomationPluginResult> ExecuteAsync(AutomationPluginInvocation invocation, CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
}

public interface IAutomationPluginAuditSink
{
    ValueTask WriteAsync(AutomationPluginAuditRecord record, CancellationToken ct = default);
    IReadOnlyList<AutomationPluginAuditRecord> ReadRecent(int maximum = 100);
}

public sealed class AutomationPluginCapabilityUnavailableException : InvalidOperationException
{
    public AutomationPluginCapabilityUnavailableException(string message) : base(message) { }
}

public sealed class AutomationPluginAuthorizationException : UnauthorizedAccessException
{
    public AutomationPluginAuthorizationException(string message) : base(message) { }
}
