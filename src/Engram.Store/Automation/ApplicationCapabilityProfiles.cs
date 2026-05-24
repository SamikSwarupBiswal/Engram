using System;
using System.Collections.Generic;

namespace Engram.Store.Automation;

public class AppCapabilityProfile
{
    public string AppName { get; init; } = string.Empty;
    public HashSet<string> SupportedOperations { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public double VerificationReliability { get; set; } = 1.0;
    public string FallbackStrategy { get; init; } = "Default";
    public double AccessibilityQuality { get; init; } = 1.0;
}

public class ApplicationCapabilityProfiles
{
    private readonly Dictionary<string, AppCapabilityProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);

    public ApplicationCapabilityProfiles()
    {
        // Pre-populate common environments
        RegisterProfile(new AppCapabilityProfile
        {
            AppName = "Chrome",
            SupportedOperations = new HashSet<string> { "Navigate", "Click", "Type", "ReadDOM" },
            VerificationReliability = 0.85,
            FallbackStrategy = "WebView2Fallback",
            AccessibilityQuality = 0.9
        });

        RegisterProfile(new AppCapabilityProfile
        {
            AppName = "Word",
            SupportedOperations = new HashSet<string> { "Save", "Edit", "Read" },
            VerificationReliability = 0.95,
            FallbackStrategy = "Win32API",
            AccessibilityQuality = 0.95
        });

        RegisterProfile(new AppCapabilityProfile
        {
            AppName = "Explorer",
            SupportedOperations = new HashSet<string> { "CreateDirectory", "MoveFile", "DeleteFile", "Zip" },
            VerificationReliability = 0.98,
            FallbackStrategy = "ShellExecute",
            AccessibilityQuality = 1.0
        });
    }

    public void RegisterProfile(AppCapabilityProfile profile)
    {
        if (profile == null) throw new ArgumentNullException(nameof(profile));
        _profiles[profile.AppName] = profile;
    }

    public AppCapabilityProfile GetProfile(string appName)
    {
        if (_profiles.TryGetValue(appName, out var profile))
        {
            return profile;
        }

        // Return a generic fallback profile
        return new AppCapabilityProfile
        {
            AppName = appName,
            SupportedOperations = new HashSet<string> { "Click", "Type" },
            VerificationReliability = 0.5,
            FallbackStrategy = "CoordinateBased",
            AccessibilityQuality = 0.4
        };
    }
}
