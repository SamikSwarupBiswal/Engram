using System.Text.Json;

namespace Engram.Store;

/// <summary>
/// Reads and writes EngramConfig from .engram/config/engram.json.
/// Creates default config on first access.
/// </summary>
public class EngramConfigStore
{
    private readonly WorkspacePaths _paths;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public EngramConfigStore(WorkspacePaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    /// <summary>
    /// Load config from disk. Creates default config if file doesn't exist.
    /// </summary>
    public EngramConfig Load()
    {
        var configPath = GetConfigPath();

        if (!File.Exists(configPath))
        {
            var defaultConfig = new EngramConfig();
            Save(defaultConfig);
            return defaultConfig;
        }

        var json = File.ReadAllText(configPath);
        return JsonSerializer.Deserialize<EngramConfig>(json, JsonOptions) ?? new EngramConfig();
    }

    /// <summary>
    /// Save config to disk atomically (tmp + rename).
    /// </summary>
    public void Save(EngramConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var configPath = GetConfigPath();
        var dir = Path.GetDirectoryName(configPath)!;
        Directory.CreateDirectory(dir);

        var tmpPath = configPath + ".tmp";
        var json = JsonSerializer.Serialize(config, JsonOptions);

        File.WriteAllText(tmpPath, json);
        File.Move(tmpPath, configPath, overwrite: true);
    }

    private string GetConfigPath()
    {
        return Path.Combine(_paths.Config, "engram.json");
    }
}
