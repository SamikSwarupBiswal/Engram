using System.Security.Cryptography;
using System.Text;

namespace Engram.Store.Automation.Applications;

public enum ApplicationDiscoverySource
{
    AppPaths,
    StartMenu,
    UninstallRegistry,
    RunningProcess,
    MicrosoftStore,
    Manual
}

public sealed class InstalledApplication
{
    public required string ApplicationId { get; init; }
    public required string Name { get; set; }
    public string? ExecutablePath { get; set; }
    public string? LaunchTarget { get; set; }
    public string? ProcessName { get; set; }
    public string? Version { get; set; }
    public string? Publisher { get; set; }
    public bool IsLaunchable { get; set; }
    public HashSet<ApplicationDiscoverySource> Sources { get; set; } = new();
    public HashSet<string> Aliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;

    public static string CreateStableId(string name, string? executablePath, string? launchTarget, string? processName)
    {
        var identity = string.Join("|", new[]
        {
            executablePath?.Trim().ToLowerInvariant(),
            launchTarget?.Trim().ToLowerInvariant(),
            processName?.Trim().ToLowerInvariant(),
            name.Trim().ToLowerInvariant()
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }
}

public sealed class ApplicationInventorySnapshot
{
    public DateTimeOffset ScannedAt { get; init; } = DateTimeOffset.UtcNow;
    public List<InstalledApplication> Applications { get; init; } = new();
}
