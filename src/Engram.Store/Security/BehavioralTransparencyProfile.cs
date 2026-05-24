using System;
using System.Collections.Generic;
using Engram.Store.Inference;

namespace Engram.Store.Security;

/// <summary>
/// Exposes security boundaries, sandboxing profiles, local-only bounds, and active degradations.
/// Used for AV white-listing audits and transparency verification.
/// </summary>
public class BehavioralTransparencyProfile
{
    public string SandboxRoot { get; set; } = string.Empty;
    public List<string> ReadWriteDirectories { get; set; } = new();
    public List<string> LocalOnlyBounds { get; set; } = new();
    public Dictionary<string, string> ActiveDegradations { get; set; } = new();
    public Dictionary<string, string> BackgroundLoops { get; set; } = new();
    public double EnvironmentalConfidence { get; set; }
    public bool SafeModeActive { get; set; }

    public static BehavioralTransparencyProfile Generate(WorkspacePaths paths)
    {
        var profile = new BehavioralTransparencyProfile
        {
            SandboxRoot = paths.Root,
            ReadWriteDirectories = new List<string>
            {
                paths.Raw,
                paths.Wiki,
                paths.Runs,
                paths.Config,
                paths.Logs,
                paths.Archives
            },
            LocalOnlyBounds = new List<string>
            {
                "localhost:5000 (API Server)",
                "No outbound external metrics",
                "Local inference phi-4-mini execution",
                "Local-only database writes"
            },
            ActiveDegradations = new Dictionary<string, string>(),
            BackgroundLoops = new Dictionary<string, string>
            {
                { "BackgroundMetabolism", "Metabolizes raw events into semantic node updates" },
                { "ScreenCaptureService", "Windows Desktop frame capture" }
            },
            EnvironmentalConfidence = DegradationTracker.Instance.GetEnvironmentalConfidence(),
            SafeModeActive = DegradationTracker.Instance.IsDegraded("SafeModeActive")
        };

        var degradations = DegradationTracker.Instance.GetCapabilityDetails();
        foreach (var kvp in degradations)
        {
            profile.ActiveDegradations[kvp.Key] = kvp.Value;
        }

        return profile;
    }
}
