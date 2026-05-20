using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Metabolism;

/// <summary>
/// User agency protection — the user controls the organism.
/// 
/// Users must control:
/// - Intensity: how strong interventions can be
/// - Frequency: how often interventions appear
/// - Domains: which areas the organism can observe
/// - Sensitivity: how sensitive the organism is to patterns
/// 
/// Without this, the organism becomes invasive.
/// </summary>
public class InterventionConsentModel : IDisposable
{
    private readonly string _configPath;
    private readonly ILogger<InterventionConsentModel>? _logger;
    private readonly object _lock = new();
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public InterventionConsentModel(
        WorkspacePaths paths,
        ILogger<InterventionConsentModel>? logger = null)
    {
        _configPath = Path.Combine(paths.Config, "intervention_consent.json");
        _logger = logger;
    }

    /// <summary>
    /// Load the current consent configuration.
    /// Returns default if not configured.
    /// </summary>
    public ConsentConfiguration LoadConfiguration()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_lock)
        {
            if (!File.Exists(_configPath))
                return ConsentConfiguration.Default();

            try
            {
                var json = File.ReadAllText(_configPath);
                return JsonSerializer.Deserialize<ConsentConfiguration>(json, JsonOptions)
                       ?? ConsentConfiguration.Default();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load consent configuration, using defaults");
                return ConsentConfiguration.Default();
            }
        }
    }

    /// <summary>
    /// Save consent configuration.
    /// </summary>
    public void SaveConfiguration(ConsentConfiguration config)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_lock)
        {
            var dir = Path.GetDirectoryName(_configPath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(_configPath, json);
            _logger?.LogInformation("Consent configuration saved");
        }
    }

    /// <summary>
    /// Check if an intervention is allowed under current consent.
    /// </summary>
    public ConsentCheck IsInterventionAllowed(Intervention intervention)
    {
        var config = LoadConfiguration();

        var check = new ConsentCheck
        {
            IsAllowed = true,
            InterventionId = intervention.InterventionId
        };

        // Check intensity limit
        if ((int)intervention.Severity > (int)config.MaxIntensity)
        {
            check.IsAllowed = false;
            check.Reason = $"Severity {intervention.Severity} exceeds max intensity {config.MaxIntensity}";
            return check;
        }

        // Check domain restrictions
        if (config.BlockedDomains.Count > 0 &&
            config.BlockedDomains.Any(d =>
                intervention.Message.Contains(d, StringComparison.OrdinalIgnoreCase) ||
                (intervention.DeclaredIntent?.Contains(d, StringComparison.OrdinalIgnoreCase) ?? false)))
        {
            check.IsAllowed = false;
            check.Reason = "Intervention touches blocked domain";
            return check;
        }

        // Check sensitivity threshold
        if (config.SensitivityLevel == SensitivityLevel.Low &&
            intervention.Severity < InterventionSeverity.High)
        {
            check.IsAllowed = false;
            check.Reason = "Low sensitivity — only high+ severity interventions allowed";
            return check;
        }

        return check;
    }

    /// <summary>
    /// Update a specific consent setting.
    /// </summary>
    public void UpdateIntensity(InterventionSeverity maxIntensity)
    {
        var config = LoadConfiguration();
        config.MaxIntensity = maxIntensity;
        SaveConfiguration(config);
    }

    /// <summary>
    /// Update sensitivity level.
    /// </summary>
    public void UpdateSensitivity(SensitivityLevel sensitivity)
    {
        var config = LoadConfiguration();
        config.SensitivityLevel = sensitivity;
        SaveConfiguration(config);
    }

    /// <summary>
    /// Add a blocked domain.
    /// </summary>
    public void BlockDomain(string domain)
    {
        var config = LoadConfiguration();
        if (!config.BlockedDomains.Contains(domain))
        {
            config.BlockedDomains.Add(domain);
            SaveConfiguration(config);
        }
    }

    /// <summary>
    /// Remove a blocked domain.
    /// </summary>
    public void UnblockDomain(string domain)
    {
        var config = LoadConfiguration();
        if (config.BlockedDomains.Remove(domain))
        {
            SaveConfiguration(config);
        }
    }

    public void Dispose()
    {
        _disposed = true;
    }
}

/// <summary>
/// User consent configuration for interventions.
/// </summary>
public class ConsentConfiguration
{
    /// <summary>Maximum intervention severity allowed.</summary>
    public InterventionSeverity MaxIntensity { get; set; } = InterventionSeverity.Critical;

    /// <summary>How sensitive the organism is to patterns.</summary>
    public SensitivityLevel SensitivityLevel { get; set; } = SensitivityLevel.Medium;

    /// <summary>Domains the organism should not observe.</summary>
    public List<string> BlockedDomains { get; set; } = new();

    /// <summary>Whether positive signals are enabled.</summary>
    public bool PositiveSignalsEnabled { get; set; } = true;

    /// <summary>Whether curiosity prompts are enabled.</summary>
    public bool CuriosityEnabled { get; set; } = true;

    /// <summary>Whether the organism can ask follow-up questions.</summary>
    public bool FollowUpEnabled { get; set; } = true;

    public static ConsentConfiguration Default() => new();
}

public enum SensitivityLevel
{
    Low,     // Only notice major issues
    Medium,  // Balanced sensitivity
    High     // Notice subtle patterns
}

/// <summary>
/// Result of a consent check.
/// </summary>
public class ConsentCheck
{
    public bool IsAllowed { get; set; }
    public string InterventionId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
