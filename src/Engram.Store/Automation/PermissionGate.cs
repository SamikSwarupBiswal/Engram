using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Engram.Store.Automation;

/// <summary>
/// Gates all automation actions behind user approval.
/// Safe actions can be auto-approved. Dangerous actions require explicit approval.
/// Tracks capability warmups with trust regression.
/// </summary>
public class PermissionGate
{
    private readonly ILogger<PermissionGate>? _logger;
    private readonly string _warmupFilePath;
    private readonly Dictionary<string, int> _warmupCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _warmupLock = new();
    private Governance.GovernanceConfig? _config;

    /// <summary>Actions that are auto-approved (safe, read-only).</summary>
    private static readonly HashSet<ActionType> SafeActions = new()
    {
        ActionType.Screenshot,
        ActionType.Wait,
    };

    /// <summary>Actions that are always blocked (dangerous).</summary>
    private static readonly HashSet<ActionType> BlockedActions = new()
    {
        // Currently no permanently blocked actions — all require approval
    };

    public PermissionGate(WorkspacePaths? paths = null, Governance.GovernanceConfig? config = null, ILogger<PermissionGate>? logger = null)
    {
        _logger = logger;
        _config = config;
        if (paths != null)
        {
            var dir = Path.Combine(paths.Config, "governance");
            Directory.CreateDirectory(dir);
            _warmupFilePath = Path.Combine(dir, "capability_warmups.json");
            LoadWarmupCounts();
        }
        else
        {
            _warmupFilePath = string.Empty;
        }
    }

    public void SetGovernanceConfig(Governance.GovernanceConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Check permission for an action, enforcing capability warmup gates.
    /// </summary>
    public ActionPermission CheckPermission(AutomationAction action)
    {
        if (BlockedActions.Contains(action.Type))
        {
            _logger?.LogWarning("Action blocked: {Type} - {Description}", action.Type, action.Description);
            return ActionPermission.Denied;
        }

        string fp = CapabilityFingerprint.Compute(action);
        int warmupCount = GetWarmupCount(fp);

        // Resolve capability-scoped autonomy ceiling
        var activeAutonomy = _config?.ActiveAutonomy ?? Governance.AutonomyLevel.Medium;
        var domainCeiling = GetDomainCeiling(action);
        var effectiveAutonomy = (Governance.AutonomyLevel)Math.Min((int)activeAutonomy, (int)domainCeiling);

        int requiredWarmup = effectiveAutonomy switch
        {
            Governance.AutonomyLevel.Low => 30,
            Governance.AutonomyLevel.Aggressive => 5,
            _ => 10
        };

        double minConfidence = effectiveAutonomy switch
        {
            Governance.AutonomyLevel.Low => 0.95,
            Governance.AutonomyLevel.Aggressive => 0.65,
            _ => 0.8
        };

        if (SafeActions.Contains(action.Type))
        {
            var confidence = Engram.Store.Inference.DegradationTracker.Instance.GetEnvironmentalConfidence();
            if (confidence < minConfidence)
            {
                _logger?.LogInformation("Action '{Type}' requires approval due to low environmental confidence ({Confidence:F2} < {MinConfidence:F2}): {Description}", action.Type, confidence, minConfidence, action.Description);
                return ActionPermission.Pending;
            }

            _logger?.LogDebug("Action auto-approved: {Type}", action.Type);
            return ActionPermission.AutoApproved;
        }

        // Low autonomy forces approval for all non-safe actions
        if (effectiveAutonomy == Governance.AutonomyLevel.Low)
        {
            _logger?.LogInformation("Action requires manual approval under Low Autonomy: {Description}", action.Description);
            return ActionPermission.Pending;
        }

        // If capability has not warmed up (less than required successful manual approvals), force approval
        if (warmupCount < requiredWarmup)
        {
            _logger?.LogInformation("Action requires warmup approval ({WarmupCount}/{RequiredWarmup} runs for fingerprint '{Fingerprint}'): {Description}", warmupCount, requiredWarmup, fp, action.Description);
            return ActionPermission.Pending;
        }

        _logger?.LogInformation("Action auto-approved after warmup ({WarmupCount}/{RequiredWarmup} runs for fingerprint '{Fingerprint}'): {Description}", warmupCount, requiredWarmup, fp, action.Description);
        return ActionPermission.AutoApproved;
    }

    private Governance.AutonomyLevel GetDomainCeiling(AutomationAction action)
    {
        var desc = action.Description.ToLowerInvariant();
        
        // Destructive
        if (desc.Contains("delete") || desc.Contains("remove") || desc.Contains("wipe") || desc.Contains("format") || desc.Contains("purge"))
            return Governance.AutonomyLevel.Low;
        
        // Financial
        if (desc.Contains("bank") || desc.Contains("pay") || desc.Contains("checkout") || desc.Contains("buy") || desc.Contains("card") || desc.Contains("purchase") || desc.Contains("billing"))
            return Governance.AutonomyLevel.Low;
        
        // Filesystem Write
        if (desc.Contains("write") || desc.Contains("save") || desc.Contains("create") || desc.Contains("export") || desc.Contains("file"))
            return Governance.AutonomyLevel.Medium;
        
        // Email
        if (desc.Contains("email") || desc.Contains("mail") || desc.Contains("send"))
            return Governance.AutonomyLevel.Medium;

        return Governance.AutonomyLevel.Aggressive;
    }

    public int GetWarmupCount(string fingerprint)
    {
        lock (_warmupLock)
        {
            return _warmupCounts.TryGetValue(fingerprint, out var count) ? count : 0;
        }
    }

    /// <summary>
    /// Record a successful run of a fingerprinted capability.
    /// </summary>
    public void RecordSuccess(AutomationAction action)
    {
        string fp = CapabilityFingerprint.Compute(action);
        lock (_warmupLock)
        {
            _warmupCounts[fp] = GetWarmupCount(fp) + 1;
            SaveWarmupCounts();
            _logger?.LogInformation("Capability fingerprint '{Fingerprint}' success recorded. New warmup count: {Count}", fp, _warmupCounts[fp]);
        }
    }

    /// <summary>
    /// Record a failed/cancelled run, executing Trust Regression.
    /// </summary>
    public void RecordFailure(AutomationAction action, bool wasCancelled = false)
    {
        string fp = CapabilityFingerprint.Compute(action);
        lock (_warmupLock)
        {
            int current = GetWarmupCount(fp);
            if (wasCancelled)
            {
                // Absolute regression to 0 for cancellations
                _warmupCounts[fp] = 0;
            }
            else
            {
                // Decrement by 2
                _warmupCounts[fp] = Math.Max(0, current - 2);
            }
            SaveWarmupCounts();
            _logger?.LogWarning("Capability fingerprint '{Fingerprint}' failure/cancel recorded. Trust regressed from {Before} to {After}", fp, current, _warmupCounts[fp]);
        }
    }

    /// <summary>
    /// Approve a specific action.
    /// </summary>
    public bool Approve(AutomationAction action)
    {
        if (action.Permission != ActionPermission.Pending)
            return false;

        action.Permission = ActionPermission.Approved;
        _logger?.LogInformation("Action approved: {ActionId} - {Description}", action.ActionId, action.Description);
        return true;
    }

    /// <summary>
    /// Deny a specific action.
    /// </summary>
    public bool Deny(AutomationAction action)
    {
        if (action.Permission != ActionPermission.Pending)
            return false;

        action.Permission = ActionPermission.Denied;
        action.Status = ActionStatus.Denied;
        _logger?.LogInformation("Action denied: {ActionId} - {Description}", action.ActionId, action.Description);
        return true;
    }

    /// <summary>
    /// Approve all pending actions in a plan.
    /// </summary>
    public int ApproveAll(ActionPlan plan)
    {
        int count = 0;
        foreach (var action in plan.Actions)
        {
            if (action.Permission == ActionPermission.Pending)
            {
                action.Permission = ActionPermission.Approved;
                count++;
            }
        }
        _logger?.LogInformation("Batch approved {Count} actions in plan {PlanId}", count, plan.PlanId);
        return count;
    }

    /// <summary>
    /// Deny all pending actions in a plan.
    /// </summary>
    public int DenyAll(ActionPlan plan)
    {
        int count = 0;
        foreach (var action in plan.Actions)
        {
            if (action.Permission == ActionPermission.Pending)
            {
                action.Permission = ActionPermission.Denied;
                action.Status = ActionStatus.Denied;
                count++;
            }
        }
        return count;
    }

    private void LoadWarmupCounts()
    {
        if (string.IsNullOrEmpty(_warmupFilePath) || !File.Exists(_warmupFilePath)) return;
        try
        {
            var json = File.ReadAllText(_warmupFilePath);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
            if (loaded != null)
            {
                lock (_warmupLock)
                {
                    _warmupCounts.Clear();
                    foreach (var kvp in loaded)
                    {
                        _warmupCounts[kvp.Key] = kvp.Value;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load capability warmups from {Path}", _warmupFilePath);
        }
    }

    private void SaveWarmupCounts()
    {
        if (string.IsNullOrEmpty(_warmupFilePath)) return;
        try
        {
            var json = JsonSerializer.Serialize(_warmupCounts, new JsonSerializerOptions { WriteIndented = true });
            var tmpPath = _warmupFilePath + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, _warmupFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to save capability warmups to {Path}", _warmupFilePath);
        }
    }
}
