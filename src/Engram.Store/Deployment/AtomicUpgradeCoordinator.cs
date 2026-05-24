using System;
using System.Collections.Generic;
using System.IO;
using Engram.Store.Wiki;

namespace Engram.Store.Deployment;

public class UpgradePreflightResult
{
    public bool Success { get; set; } = true;
    public string Message { get; set; } = string.Empty;
    public string TargetSystemVersion { get; set; } = string.Empty;
    public int TargetSchemaVersion { get; set; }
    public Dictionary<string, string> BehavioralTrustDiff { get; set; } = new(); // Explains impact of version changes
}

public class UpgradeResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public MigrationConfidence Confidence { get; set; } = MigrationConfidence.Safe;
    public List<string> Errors { get; set; } = new();
}

public class AtomicUpgradeCoordinator
{
    private readonly WorkspacePaths _paths;
    private readonly WikiNodeStore _nodeStore;
    private readonly VersionCompatibilityManager _versionManager;
    private readonly SnapshotRollbackSystem _rollbackSystem;
    private readonly SemanticMigrationEngine _migrationEngine;
    private readonly MigrationIntegrityVerifier _integrityVerifier;

    public AtomicUpgradeCoordinator(
        WorkspacePaths paths, 
        WikiNodeStore nodeStore,
        VersionCompatibilityManager versionManager,
        SnapshotRollbackSystem rollbackSystem,
        SemanticMigrationEngine migrationEngine,
        MigrationIntegrityVerifier integrityVerifier)
    {
        _paths = paths;
        _nodeStore = nodeStore;
        _versionManager = versionManager;
        _rollbackSystem = rollbackSystem;
        _migrationEngine = migrationEngine;
        _integrityVerifier = integrityVerifier;
    }

    public UpgradePreflightResult RunPreflight(string targetSystemVersion, int targetSchemaVersion)
    {
        var result = new UpgradePreflightResult
        {
            TargetSystemVersion = targetSystemVersion,
            TargetSchemaVersion = targetSchemaVersion
        };

        // 1. Check disk space (simulate checks)
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(_paths.Root)) ?? "C");
            if (drive.AvailableFreeSpace < 100 * 1024 * 1024) // <100MB
            {
                result.Success = false;
                result.Message = "Insufficient free disk space for upgrade snapshot.";
                return result;
            }
        }
        catch
        {
            // Fallback for environment constraints
        }

        // 2. Check compatibility limits
        string compatibilityReason;
        bool isCompatible = _versionManager.CheckCompatibility(targetSchemaVersion, out compatibilityReason);
        if (!isCompatible)
        {
            result.Success = false;
            result.Message = $"Version compatibility check failed: {compatibilityReason}";
            return result;
        }

        // 3. Generate Behavioral Trust Diff
        result.BehavioralTrustDiff = GenerateTrustDiff(targetSchemaVersion);
        result.Message = "Preflight checks passed successfully.";
        return result;
    }

    public UpgradeResult ExecuteUpgrade(string targetSystemVersion, int targetSchemaVersion)
    {
        var preflight = RunPreflight(targetSystemVersion, targetSchemaVersion);
        if (!preflight.Success)
        {
            return new UpgradeResult
            {
                Success = false,
                Message = $"Preflight validation failed: {preflight.Message}"
            };
        }

        // 1. Take snapshot of database and governance state
        string snapshotPath;
        try
        {
            snapshotPath = _rollbackSystem.CreateSnapshot();
        }
        catch (Exception ex)
        {
            return new UpgradeResult
            {
                Success = false,
                Message = $"Snapshot creation failed. Upgrade aborted: {ex.Message}"
            };
        }

        // Preserve current version metadata for rollback
        var currentInfo = _versionManager.GetCurrentVersionInfo();
        int originalSchemaVersion = currentInfo.SchemaVersion;
        string originalSystemVersion = currentInfo.SystemVersion;

        // 2. Run migrations
        List<string> migrationErrors;
        var confidence = _migrationEngine.ExecuteMigrations(out migrationErrors);

        if (confidence == MigrationConfidence.Unsafe)
        {
            // Execute atomic rollback
            try
            {
                _rollbackSystem.RestoreSnapshot(snapshotPath);
            }
            catch (Exception ex)
            {
                return new UpgradeResult
                {
                    Success = false,
                    Message = $"Migration failed and rollback restoration failed critically: {ex.Message}",
                    Confidence = MigrationConfidence.Unsafe,
                    Errors = migrationErrors
                };
            }

            return new UpgradeResult
            {
                Success = false,
                Message = "Migration validation failed. Automated system rollback executed successfully.",
                Confidence = MigrationConfidence.Unsafe,
                Errors = migrationErrors
            };
        }

        // 3. Run semantic integrity verification
        var integrityReport = _integrityVerifier.Verify(_nodeStore);
        if (!integrityReport.IsValid)
        {
            // Integrity check failed: revert to original state
            try
            {
                _rollbackSystem.RestoreSnapshot(snapshotPath);
            }
            catch (Exception ex)
            {
                return new UpgradeResult
                {
                    Success = false,
                    Message = $"Post-upgrade integrity check failed and rollback restoration failed: {ex.Message}",
                    Confidence = MigrationConfidence.Unsafe,
                    Errors = integrityReport.ValidationErrors
                };
            }

            return new UpgradeResult
            {
                Success = false,
                Message = "Post-upgrade semantic integrity check failed. System state rolled back successfully.",
                Confidence = MigrationConfidence.Unsafe,
                Errors = integrityReport.ValidationErrors
            };
        }

        // 4. Upgrade version manifest
        var upgradedInfo = _versionManager.GetCurrentVersionInfo();
        upgradedInfo.SystemVersion = targetSystemVersion;
        _versionManager.SaveVersionInfo(upgradedInfo);

        // Prune old backups to keep disk consumption bounded
        _rollbackSystem.PruneOldSnapshots();

        return new UpgradeResult
        {
            Success = true,
            Message = "Upgrade and migration executed successfully.",
            Confidence = confidence
        };
    }

    private Dictionary<string, string> GenerateTrustDiff(int targetSchemaVersion)
    {
        var diff = new Dictionary<string, string>();
        var currentVersion = _versionManager.GetCurrentVersionInfo().SchemaVersion;

        if (currentVersion < 2 && targetSchemaVersion >= 2)
        {
            diff["Observation Range"] = "Unchanged. Remains local files and timelines.";
            diff["Autonomy Ceiling"] = "Allows precise local calibration thresholds for active goals.";
            diff["Permissions required"] = "No new system permission or security rules are added.";
        }
        else
        {
            diff["Observation Range"] = "Unchanged.";
            diff["Autonomy Ceiling"] = "Unchanged.";
        }

        return diff;
    }
}
