using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Perception;

/// <summary>
/// Privacy and transparency dashboard.
/// 
/// NON-NEGOTIABLE. Existential for trust.
/// 
/// Shows:
/// - What Engram saw
/// - What was stored
/// - What was discarded
/// - Why it mattered
/// 
/// Controls:
/// - Kill switches
/// - Domain exclusions
/// - Pause capture
/// - App blacklists
/// - Retention controls
/// 
/// Without this, Engram becomes psychologically creepy.
/// </summary>
public class PerceptionDashboard : IDisposable
{
    private readonly string _configPath;
    private readonly ILogger<PerceptionDashboard>? _logger;
    private readonly object _lock = new();
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public PerceptionDashboard(
        WorkspacePaths paths,
        ILogger<PerceptionDashboard>? logger = null)
    {
        _configPath = Path.Combine(paths.Config, "perception_config.json");
        _logger = logger;
    }

    /// <summary>
    /// Load perception configuration.
    /// </summary>
    public PerceptionConfiguration LoadConfiguration()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_lock)
        {
            if (!File.Exists(_configPath))
                return PerceptionConfiguration.Default();

            try
            {
                var json = File.ReadAllText(_configPath);
                return JsonSerializer.Deserialize<PerceptionConfiguration>(json, JsonOptions)
                       ?? PerceptionConfiguration.Default();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load perception configuration, using defaults");
                return PerceptionConfiguration.Default();
            }
        }
    }

    /// <summary>
    /// Save perception configuration.
    /// </summary>
    public void SaveConfiguration(PerceptionConfiguration config)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_lock)
        {
            var dir = Path.GetDirectoryName(_configPath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(_configPath, json);
            _logger?.LogInformation("Perception configuration saved");
        }
    }

    /// <summary>
    /// Check if perception is currently enabled.
    /// </summary>
    public bool IsPerceptionEnabled()
    {
        var config = LoadConfiguration();
        return config.IsEnabled && !config.IsPaused;
    }

    /// <summary>
    /// Check if a specific app is allowed to be tracked.
    /// </summary>
    public bool IsAppAllowed(string processName)
    {
        var config = LoadConfiguration();

        if (!config.IsEnabled) return false;
        if (config.IsPaused) return false;

        // Check blacklist
        if (config.BlacklistedApps.Contains(processName, StringComparer.OrdinalIgnoreCase))
            return false;

        // Check allowlist (if specified, only track allowed apps)
        if (config.AllowedApps.Count > 0 &&
            !config.AllowedApps.Contains(processName, StringComparer.OrdinalIgnoreCase))
            return false;

        return true;
    }

    /// <summary>
    /// Check if a file path is allowed to be watched.
    /// </summary>
    public bool IsPathAllowed(string filePath)
    {
        var config = LoadConfiguration();

        if (!config.IsEnabled) return false;
        if (config.IsPaused) return false;

        // Check excluded paths
        foreach (var excluded in config.ExcludedPaths)
        {
            if (filePath.StartsWith(excluded, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Pause all perception.
    /// </summary>
    public void PausePerception()
    {
        var config = LoadConfiguration();
        config.IsPaused = true;
        SaveConfiguration(config);
        _logger?.LogInformation("Perception paused");
    }

    /// <summary>
    /// Resume perception.
    /// </summary>
    public void ResumePerception()
    {
        var config = LoadConfiguration();
        config.IsPaused = false;
        SaveConfiguration(config);
        _logger?.LogInformation("Perception resumed");
    }

    /// <summary>
    /// Add an app to the blacklist.
    /// </summary>
    public void BlacklistApp(string processName)
    {
        var config = LoadConfiguration();
        if (!config.BlacklistedApps.Contains(processName, StringComparer.OrdinalIgnoreCase))
        {
            config.BlacklistedApps.Add(processName);
            SaveConfiguration(config);
        }
    }

    /// <summary>
    /// Remove an app from the blacklist.
    /// </summary>
    public void UnblacklistApp(string processName)
    {
        var config = LoadConfiguration();
        config.BlacklistedApps.RemoveAll(a =>
            a.Equals(processName, StringComparison.OrdinalIgnoreCase));
        SaveConfiguration(config);
    }

    /// <summary>
    /// Add a path to exclusions.
    /// </summary>
    public void ExcludePath(string path)
    {
        var config = LoadConfiguration();
        if (!config.ExcludedPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            config.ExcludedPaths.Add(path);
            SaveConfiguration(config);
        }
    }

    /// <summary>
    /// Remove a path from exclusions.
    /// </summary>
    public void UnexcludePath(string path)
    {
        var config = LoadConfiguration();
        config.ExcludedPaths.RemoveAll(p =>
            p.Equals(path, StringComparison.OrdinalIgnoreCase));
        SaveConfiguration(config);
    }

    /// <summary>
    /// Get a summary of what Engram has perceived.
    /// </summary>
    public PerceptionSummary GetPerceptionSummary()
    {
        var config = LoadConfiguration();

        return new PerceptionSummary
        {
            IsEnabled = config.IsEnabled,
            IsPaused = config.IsPaused,
            BlacklistedAppCount = config.BlacklistedApps.Count,
            ExcludedPathCount = config.ExcludedPaths.Count,
            BlacklistedApps = config.BlacklistedApps.ToList(),
            ExcludedPaths = config.ExcludedPaths.ToList(),
            Status = GetStatus(config)
        };
    }

    private static string GetStatus(PerceptionConfiguration config)
    {
        if (!config.IsEnabled) return "Perception disabled";
        if (config.IsPaused) return "Perception paused";
        return "Perception active";
    }

    public void Dispose()
    {
        _disposed = true;
    }
}

/// <summary>
/// Perception configuration.
/// </summary>
public class PerceptionConfiguration
{
    /// <summary>Whether perception is enabled at all.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Whether perception is temporarily paused.</summary>
    public bool IsPaused { get; set; }

    /// <summary>Apps that should never be tracked.</summary>
    public List<string> BlacklistedApps { get; set; } = new();

    /// <summary>Paths that should never be watched.</summary>
    public List<string> ExcludedPaths { get; set; } = new();

    /// <summary>If specified, only these apps are tracked.</summary>
    public List<string> AllowedApps { get; set; } = new();

    /// <summary>Whether OCR perception is enabled.</summary>
    public bool OcrEnabled { get; set; } = true;

    /// <summary>Whether file watching is enabled.</summary>
    public bool FileWatchingEnabled { get; set; } = true;

    /// <summary>Maximum retention period for perception data.</summary>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(30);

    public static PerceptionConfiguration Default() => new();
}

/// <summary>
/// Summary of perception state.
/// </summary>
public class PerceptionSummary
{
    public bool IsEnabled { get; set; }
    public bool IsPaused { get; set; }
    public int BlacklistedAppCount { get; set; }
    public int ExcludedPathCount { get; set; }
    public List<string> BlacklistedApps { get; set; } = new();
    public List<string> ExcludedPaths { get; set; } = new();
    public string Status { get; set; } = string.Empty;
}
