using System;
using System.Collections.Generic;
using System.Linq;
using Engram.Store.Wiki;

namespace Engram.Store.Deployment;

public enum MigrationConfidence
{
    Safe,
    Degraded,
    Risky,
    Unsafe
}

public interface ISemanticMigration
{
    int TargetVersion { get; }
    string Description { get; }
    string BehavioralImpact { get; } // User-facing change summary for Update Explainability
    bool Run(WikiNodeStore store);
    bool Rollback(WikiNodeStore store);
}

public class SemanticMigrationEngine
{
    private readonly WikiNodeStore _store;
    private readonly VersionCompatibilityManager _versionManager;
    private readonly List<ISemanticMigration> _migrations = new();

    public SemanticMigrationEngine(WikiNodeStore store, VersionCompatibilityManager versionManager)
    {
        _store = store;
        _versionManager = versionManager;
        RegisterDefaultMigrations();
    }

    private void RegisterDefaultMigrations()
    {
        // Register standard migrations
        _migrations.Add(new Migration_001_InitSchema());
        _migrations.Add(new Migration_002_SemanticRefinement());
    }

    public void AddMigration(ISemanticMigration migration)
    {
        if (migration == null) throw new ArgumentNullException(nameof(migration));
        _migrations.Add(migration);
    }

    public IReadOnlyList<ISemanticMigration> GetPendingMigrations()
    {
        var currentVersion = _versionManager.GetCurrentVersionInfo().SchemaVersion;
        return _migrations
            .Where(m => m.TargetVersion > currentVersion)
            .OrderBy(m => m.TargetVersion)
            .ToList();
    }

    public MigrationConfidence ExecuteMigrations(out List<string> errors)
    {
        errors = new List<string>();
        var pending = GetPendingMigrations();
        if (!pending.Any()) return MigrationConfidence.Safe;

        var currentInfo = _versionManager.GetCurrentVersionInfo();
        var originalVersion = currentInfo.SchemaVersion;

        foreach (var migration in pending)
        {
            try
            {
                bool success = migration.Run(_store);
                if (!success)
                {
                    errors.Add($"Migration to version {migration.TargetVersion} failed execution.");
                    RollbackTo(originalVersion);
                    return MigrationConfidence.Unsafe;
                }

                currentInfo.SchemaVersion = migration.TargetVersion;
                currentInfo.LastUpdatedAt = DateTimeOffset.UtcNow;
                _versionManager.SaveVersionInfo(currentInfo);
            }
            catch (Exception ex)
            {
                errors.Add($"Migration to version {migration.TargetVersion} threw exception: {ex.Message}");
                RollbackTo(originalVersion);
                return MigrationConfidence.Unsafe;
            }
        }

        // Assess post-migration confidence (simplified logic)
        if (errors.Any())
        {
            return MigrationConfidence.Unsafe;
        }

        return MigrationConfidence.Safe;
    }

    public void RollbackTo(int targetVersion)
    {
        var currentVersion = _versionManager.GetCurrentVersionInfo().SchemaVersion;
        var rollbacks = _migrations
            .Where(m => m.TargetVersion <= currentVersion && m.TargetVersion > targetVersion)
            .OrderByDescending(m => m.TargetVersion)
            .ToList();

        foreach (var migration in rollbacks)
        {
            try
            {
                migration.Rollback(_store);
            }
            catch
            {
                // Force continuous rollback of metadata even if single script fails
            }
        }

        var info = _versionManager.GetCurrentVersionInfo();
        info.SchemaVersion = targetVersion;
        info.LastUpdatedAt = DateTimeOffset.UtcNow;
        _versionManager.SaveVersionInfo(info);
    }
}

// ── Migration Mock Concrete Implementations ──

public class Migration_001_InitSchema : ISemanticMigration
{
    public int TargetVersion => 1;
    public string Description => "Initialize base semantic entity schema.";
    public string BehavioralImpact => "Enables tracking of person, project, and goal nodes.";

    public bool Run(WikiNodeStore store)
    {
        // Already initialized at base code.
        return true;
    }

    public bool Rollback(WikiNodeStore store)
    {
        return true;
    }
}

public class Migration_002_SemanticRefinement : ISemanticMigration
{
    public int TargetVersion => 2;
    public string Description => "Migrate claims to support explicit confidence ranges.";
    public string BehavioralImpact => "Allows precise confidence measurement for inferred memories.";

    public bool Run(WikiNodeStore store)
    {
        try
        {
            var allNodes = store.LoadAll();
            foreach (var node in allNodes)
            {
                bool modified = false;
                foreach (var claim in node.Claims)
                {
                    // Ensure confidence is normalized
                    if (claim.Confidence < 0)
                    {
                        claim.Confidence = 0.0;
                        modified = true;
                    }
                    else if (claim.Confidence > 1)
                    {
                        claim.Confidence = 1.0;
                        modified = true;
                    }
                }
                if (modified)
                {
                    store.Save(node);
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool Rollback(WikiNodeStore store)
    {
        return true;
    }
}
