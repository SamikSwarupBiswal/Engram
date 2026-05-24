using System;
using System.IO;
using System.Text.Json;

namespace Engram.Store.Deployment;

public class VersionInfo
{
    public string SystemVersion { get; set; } = "1.0.0";
    public int SchemaVersion { get; set; } = 1;
    public int CapabilityVersion { get; set; } = 1;
    public DateTimeOffset LastUpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class VersionCompatibilityManager
{
    private readonly string _versionFilePath;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public VersionCompatibilityManager(WorkspacePaths paths)
    {
        _versionFilePath = Path.Combine(paths.Config, "version.json");
    }

    public VersionInfo GetCurrentVersionInfo()
    {
        if (!File.Exists(_versionFilePath))
        {
            var initial = new VersionInfo();
            SaveVersionInfo(initial);
            return initial;
        }

        try
        {
            var json = File.ReadAllText(_versionFilePath);
            var info = JsonSerializer.Deserialize<VersionInfo>(json, JsonOptions);
            return info ?? new VersionInfo();
        }
        catch
        {
            return new VersionInfo();
        }
    }

    public void SaveVersionInfo(VersionInfo info)
    {
        try
        {
            var json = JsonSerializer.Serialize(info, JsonOptions);
            File.WriteAllText(_versionFilePath, json);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to save version metadata.", ex);
        }
    }

    public bool CheckCompatibility(int currentSystemSchemaVersion, out string reason)
    {
        var dbInfo = GetCurrentVersionInfo();
        
        if (dbInfo.SchemaVersion > currentSystemSchemaVersion)
        {
            reason = $"Database schema version ({dbInfo.SchemaVersion}) is newer than the system schema version ({currentSystemSchemaVersion}). Downgrades are not supported without a rollback.";
            return false;
        }

        if (dbInfo.SchemaVersion < currentSystemSchemaVersion)
        {
            reason = $"Database schema version ({dbInfo.SchemaVersion}) is older than the system schema version ({currentSystemSchemaVersion}). Migration is required.";
            return true; // Migration is possible
        }

        reason = "Schema versions are fully compatible.";
        return true;
    }
}
