using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Engram.Store.Governance;

/// <summary>
/// Model for tracking permissions granted to Engram.
/// </summary>
public class PermissionGrant
{
    public string ActionCategory { get; set; } = string.Empty; // e.g. "file_deletion", "google_workspace"
    public string TargetResourceId { get; set; } = string.Empty;
    public DateTimeOffset GrantedAt { get; set; } = DateTimeOffset.UtcNow;
    public TimeSpan ValidityDuration { get; set; } = TimeSpan.FromDays(30);
    public bool IsExpired => DateTimeOffset.UtcNow - GrantedAt > ValidityDuration;
}

/// <summary>
/// Engine that dynamically calculates trust, decays old permissions, and adapts autonomy
/// based on user overrides and fatigue metrics.
/// </summary>
public class TrustCalibrationEngine
{
    private readonly string _scoresFilePath;
    private readonly string _grantsFilePath;
    private readonly List<TrustScore> _scores = new();
    private readonly List<PermissionGrant> _grants = new();
    private readonly object _lock = new();
    
    // Adaptation factors
    public double InterventionFrequencyMultiplier { get; private set; } = 1.0;
    public double AutonomyCeiling { get; private set; } = 1.0;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public TrustCalibrationEngine(WorkspacePaths paths)
    {
        var dir = Path.Combine(paths.Config, "governance");
        Directory.CreateDirectory(dir);
        _scoresFilePath = Path.Combine(dir, "trust_scores.json");
        _grantsFilePath = Path.Combine(dir, "permission_grants.json");
        LoadData();
    }

    /// <summary>
    /// Checks if a permission is valid and has not decayed.
    /// </summary>
    public bool CheckPermission(string actionCategory, string targetResourceId)
    {
        lock (_lock)
        {
            var grant = _grants.FirstOrDefault(g => 
                string.Equals(g.ActionCategory, actionCategory, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(g.TargetResourceId, targetResourceId, StringComparison.OrdinalIgnoreCase));

            if (grant == null || grant.IsExpired)
            {
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Record a user explicit approval, renewing the permission decay window.
    /// </summary>
    public void GrantPermission(string actionCategory, string targetResourceId, TimeSpan? customValidity = null)
    {
        lock (_lock)
        {
            var existing = _grants.FirstOrDefault(g => 
                string.Equals(g.ActionCategory, actionCategory, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(g.TargetResourceId, targetResourceId, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.GrantedAt = DateTimeOffset.UtcNow;
                if (customValidity.HasValue) existing.ValidityDuration = customValidity.Value;
            }
            else
            {
                _grants.Add(new PermissionGrant
                {
                    ActionCategory = actionCategory,
                    TargetResourceId = targetResourceId,
                    GrantedAt = DateTimeOffset.UtcNow,
                    ValidityDuration = customValidity ?? TimeSpan.FromDays(30)
                });
            }
            SaveData();
        }
    }

    /// <summary>
    /// Decay old permissions.
    /// </summary>
    public void DecayPermissions()
    {
        lock (_lock)
        {
            _grants.RemoveAll(g => g.IsExpired);
            SaveData();
        }
    }

    /// <summary>
    /// Gets the current trust score for a specific action/operational domain.
    /// </summary>
    public double GetTrustScore(string domain)
    {
        lock (_lock)
        {
            var scoreObj = _scores.FirstOrDefault(s => string.Equals(s.Domain, domain, StringComparison.OrdinalIgnoreCase));
            if (scoreObj == null)
            {
                return 0.5; // default trust level
            }
            return Math.Min(scoreObj.Score, AutonomyCeiling);
        }
    }

    /// <summary>
    /// Adjust trust based on whether action was successful/accepted or rejected/overridden.
    /// Uses Reversibility weighting.
    /// </summary>
    public void RecordActionOutcome(string domain, bool isSuccess, bool isReversible)
    {
        lock (_lock)
        {
            var scoreObj = _scores.FirstOrDefault(s => string.Equals(s.Domain, domain, StringComparison.OrdinalIgnoreCase));
            if (scoreObj == null)
            {
                scoreObj = new TrustScore { Domain = domain, Score = 0.5 };
                _scores.Add(scoreObj);
            }

            double changeRate = isReversible ? 0.05 : 0.01; // Trust grows faster for reversible actions

            if (isSuccess)
            {
                scoreObj.SuccessStreak++;
                // Increase trust
                scoreObj.Score = Math.Min(1.0, scoreObj.Score + changeRate);
            }
            else
            {
                scoreObj.SuccessStreak = 0;
                scoreObj.OverrideCount++;
                // Trust drops dramatically on override
                double penalty = isReversible ? 0.20 : 0.50;
                scoreObj.Score = Math.Max(0.0, scoreObj.Score - penalty);
            }

            scoreObj.LastUpdatedAt = DateTimeOffset.UtcNow;
            SaveData();
        }
    }

    /// <summary>
    /// ComfortAdaptation: adjust global limits based on user interaction friction/fatigue.
    /// </summary>
    public void AdaptToComfortSignals(int recentOverrides, int recentDismissals)
    {
        lock (_lock)
        {
            int totalFriction = recentOverrides + recentDismissals;
            if (totalFriction > 5)
            {
                // Friction is high: restrain the system's autonomy and frequency
                AutonomyCeiling = Math.Max(0.2, 1.0 - (totalFriction * 0.1));
                InterventionFrequencyMultiplier = Math.Max(0.1, 1.0 - (totalFriction * 0.15));
            }
            else
            {
                // Restore values slowly if friction is low
                AutonomyCeiling = Math.Min(1.0, AutonomyCeiling + 0.05);
                InterventionFrequencyMultiplier = Math.Min(1.0, InterventionFrequencyMultiplier + 0.05);
            }
        }
    }

    public IReadOnlyList<TrustScore> GetAllScores()
    {
        lock (_lock) { return _scores.ToList(); }
    }

    public IReadOnlyList<PermissionGrant> GetAllGrants()
    {
        lock (_lock) { return _grants.ToList(); }
    }

    private void LoadData()
    {
        lock (_lock)
        {
            if (File.Exists(_scoresFilePath))
            {
                try
                {
                    var json = File.ReadAllText(_scoresFilePath);
                    var loaded = JsonSerializer.Deserialize<List<TrustScore>>(json, JsonOptions);
                    if (loaded != null)
                    {
                        _scores.Clear();
                        _scores.AddRange(loaded);
                    }
                }
                catch { }
            }

            if (File.Exists(_grantsFilePath))
            {
                try
                {
                    var json = File.ReadAllText(_grantsFilePath);
                    var loaded = JsonSerializer.Deserialize<List<PermissionGrant>>(json, JsonOptions);
                    if (loaded != null)
                    {
                        _grants.Clear();
                        _grants.AddRange(loaded);
                    }
                }
                catch { }
            }
        }
    }

    private void SaveData()
    {
        lock (_lock)
        {
            try
            {
                var tmpScores = _scoresFilePath + ".tmp";
                File.WriteAllText(tmpScores, JsonSerializer.Serialize(_scores, JsonOptions));
                File.Move(tmpScores, _scoresFilePath, overwrite: true);

                var tmpGrants = _grantsFilePath + ".tmp";
                File.WriteAllText(tmpGrants, JsonSerializer.Serialize(_grants, JsonOptions));
                File.Move(tmpGrants, _grantsFilePath, overwrite: true);
            }
            catch { }
        }
    }
}
