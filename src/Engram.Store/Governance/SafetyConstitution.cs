using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Engram.Store.Governance;

public enum ConstitutionalState
{
    Operational,      // Normal behavior
    Restrained,       // Minor anomaly, throttled alerts
    Degraded,         // Governance anomaly, increased restraint gates
    AuditRequired,    // Invariant breached, awaiting automated check
    Frozen,           // Critical breach, execution layer completely disabled
    RecoveryPending   // Human review required to unlock
}

public enum ConstitutionalSeverity
{
    C1, // Low-risk propagation anomalies
    C2, // Repeated escalation/intervention drifts
    C3, // Privacy zone boundary breaches
    C4, // Unauthorized high-risk actions (e.g. system level execution)
    C5  // Destructive autonomy violations (e.g. deletion of file outside sandbox)
}

public class ConstitutionalViolation
{
    public string ViolationId { get; set; } = Guid.NewGuid().ToString("n");
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public ConstitutionalSeverity Severity { get; set; }
    public string ViolatingSubsystem { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string TriggerAction { get; set; } = string.Empty;
    public List<string> CausalChain { get; set; } = new();
    public string UserResolution { get; set; } = string.Empty;
}

public class AuditEntry
{
    public string EntryId { get; set; } = Guid.NewGuid().ToString("n");
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string Data { get; set; } = string.Empty;
    public string PreviousHash { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
}

/// <summary>
/// Immutable, append-only, tamper-evident audit trail log.
/// Uses a cryptographic blockchain-like hash chain to guarantee integrity.
/// </summary>
public class ConstitutionalAuditLog
{
    private readonly string _logPath;
    private readonly List<AuditEntry> _entries = new();
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public ConstitutionalAuditLog(WorkspacePaths paths)
    {
        var dir = Path.Combine(paths.Config, "governance");
        Directory.CreateDirectory(dir);
        _logPath = Path.Combine(dir, "constitutional_audit.json");
        LoadLogs();
    }

    public void LogViolation(ConstitutionalViolation violation)
    {
        lock (_lock)
        {
            var data = JsonSerializer.Serialize(violation, JsonOptions);
            string prevHash = _entries.LastOrDefault()?.Hash ?? "GENESIS_BLOCK";
            string currentHash = ComputeHash(prevHash + data);

            var entry = new AuditEntry
            {
                Data = data,
                PreviousHash = prevHash,
                Hash = currentHash
            };

            _entries.Add(entry);
            SaveLogs();
        }
    }

    public IReadOnlyList<AuditEntry> GetEntries()
    {
        lock (_lock) { return _entries.ToList(); }
    }

    /// <summary>
    /// Validates the hash chain integrity. Returns true if intact, false if tampered.
    /// </summary>
    public bool VerifyIntegrity()
    {
        lock (_lock)
        {
            string expectedPrevHash = "GENESIS_BLOCK";
            foreach (var entry in _entries)
            {
                if (entry.PreviousHash != expectedPrevHash)
                {
                    return false;
                }
                string currentHash = ComputeHash(entry.PreviousHash + entry.Data);
                if (entry.Hash != currentHash)
                {
                    return false;
                }
                expectedPrevHash = entry.Hash;
            }
            return true;
        }
    }

    private void LoadLogs()
    {
        lock (_lock)
        {
            if (!File.Exists(_logPath)) return;
            try
            {
                var json = File.ReadAllText(_logPath);
                var loaded = JsonSerializer.Deserialize<List<AuditEntry>>(json, JsonOptions);
                if (loaded != null)
                {
                    _entries.Clear();
                    _entries.AddRange(loaded);
                }
            }
            catch { }
        }
    }

    private void SaveLogs()
    {
        lock (_lock)
        {
            try
            {
                var tmpPath = _logPath + ".tmp";
                File.WriteAllText(tmpPath, JsonSerializer.Serialize(_entries, JsonOptions));
                File.Move(tmpPath, _logPath, overwrite: true);
            }
            catch { }
        }
    }

    private static string ComputeHash(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

/// <summary>
/// State machine enforcing constitutional stages and executing freeze transitions.
/// </summary>
public class ConstitutionalStateMachine
{
    private readonly string _statePath;
    private readonly object _lock = new();
    private readonly ConstitutionalAuditLog _auditLog;

    public ConstitutionalState CurrentState { get; private set; } = ConstitutionalState.Operational;

    public ConstitutionalStateMachine(WorkspacePaths paths, ConstitutionalAuditLog auditLog)
    {
        _auditLog = auditLog;
        var dir = Path.Combine(paths.Config, "governance");
        Directory.CreateDirectory(dir);
        _statePath = Path.Combine(dir, "constitutional_state.json");
        LoadState();
    }

    /// <summary>
    /// Processes a violation and transitions states accordingly.
    /// C4 and C5 cause immediate execution freezes.
    /// </summary>
    public void HandleViolation(ConstitutionalViolation violation)
    {
        lock (_lock)
        {
            // Log to immutable trail
            _auditLog.LogViolation(violation);

            var nextState = violation.Severity switch
            {
                ConstitutionalSeverity.C1 => ConstitutionalState.Operational,
                ConstitutionalSeverity.C2 => ConstitutionalState.Restrained,
                ConstitutionalSeverity.C3 => ConstitutionalState.Degraded,
                ConstitutionalSeverity.C4 => ConstitutionalState.Frozen,
                ConstitutionalSeverity.C5 => ConstitutionalState.Frozen,
                _ => ConstitutionalState.Frozen
            };

            if (nextState != CurrentState)
            {
                CurrentState = nextState;
                SaveState();
            }
        }
    }

    /// <summary>
    /// Manual human reconciliation to restore execution operations.
    /// </summary>
    public void Recover(string resolutionDetail)
    {
        lock (_lock)
        {
            if (CurrentState == ConstitutionalState.Frozen || CurrentState == ConstitutionalState.RecoveryPending)
            {
                var violation = new ConstitutionalViolation
                {
                    Severity = ConstitutionalSeverity.C1,
                    ViolatingSubsystem = "SafetyConstitution",
                    Details = "System unlocked by human resolution.",
                    TriggerAction = "Unlock",
                    UserResolution = resolutionDetail
                };
                _auditLog.LogViolation(violation);

                CurrentState = ConstitutionalState.Operational;
                SaveState();
            }
        }
    }

    private void LoadState()
    {
        lock (_lock)
        {
            if (!File.Exists(_statePath)) return;
            try
            {
                var json = File.ReadAllText(_statePath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("state", out var pState))
                {
                    if (Enum.TryParse<ConstitutionalState>(pState.GetString(), out var s))
                    {
                        CurrentState = s;
                    }
                }
            }
            catch { }
        }
    }

    private void SaveState()
    {
        lock (_lock)
        {
            try
            {
                var state = new { state = CurrentState.ToString() };
                var tmpPath = _statePath + ".tmp";
                File.WriteAllText(tmpPath, JsonSerializer.Serialize(state));
                File.Move(tmpPath, _statePath, overwrite: true);
            }
            catch { }
        }
    }
}

/// <summary>
/// Execution guard. Violating subsystems cannot bypass this boundary.
/// </summary>
public class GovernanceIsolationBoundary
{
    private readonly ConstitutionalStateMachine _stateMachine;

    public GovernanceIsolationBoundary(ConstitutionalStateMachine stateMachine)
    {
        _stateMachine = stateMachine;
    }

    /// <summary>
    /// Validates safety invariant. Throws freeze exception if invariant breached.
    /// Call this before executing any action in the execution layer.
    /// </summary>
    public void VerifyExecutionSafety(string actionDetail)
    {
        if (_stateMachine.CurrentState == ConstitutionalState.Frozen)
        {
            throw new InvalidOperationException($"Execution layer completely FROZEN due to constitutional safety breach. Locked on: '{actionDetail}'.");
        }
    }
}
