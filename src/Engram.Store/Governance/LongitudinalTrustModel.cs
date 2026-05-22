using System;
using System.IO;
using System.Text.Json;

namespace Engram.Store.Governance;

/// <summary>
/// Longitudinal trust model tracking annoyance accumulation, trust repair rates,
/// and forgiveness over long term coexistence periods.
/// </summary>
public class LongitudinalTrustModel
{
    private readonly string _stateFilePath;
    private readonly object _lock = new();

    public double AnnoyanceScore { get; private set; } // 0.0 (calm) to 10.0 (catastrophic fatigue)
    public DateTimeOffset LastFrictionAt { get; private set; } = DateTimeOffset.MinValue;
    public double HistoricalTrustIndex { get; private set; } = 1.0; // 0.0 to 1.0

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public LongitudinalTrustModel(WorkspacePaths paths)
    {
        var dir = Path.Combine(paths.Config, "governance");
        Directory.CreateDirectory(dir);
        _stateFilePath = Path.Combine(dir, "longitudinal_trust.json");
        LoadState();
    }

    /// <summary>
    /// Records an occurrence of small user friction (e.g., dismissal within 5 seconds).
    /// </summary>
    public void RecordAnnoyance(double intensity = 1.0)
    {
        lock (_lock)
        {
            AnnoyanceScore = Math.Min(10.0, AnnoyanceScore + intensity);
            LastFrictionAt = DateTimeOffset.UtcNow;
            
            // Decays historical trust index based on annoyance load
            HistoricalTrustIndex = Math.Max(0.0, HistoricalTrustIndex - (intensity * 0.05));
            SaveState();
        }
    }

    /// <summary>
    /// Promotes trust repair and decays annoyance score if no friction occurs over time.
    /// </summary>
    public void ApplyForgivenessDecay(TimeSpan timeSpanPassed)
    {
        lock (_lock)
        {
            if (AnnoyanceScore <= 0.0) return;

            // Decay annoyance by 0.5 points per hour of quiet coexistence
            double decayAmount = timeSpanPassed.TotalHours * 0.5;
            AnnoyanceScore = Math.Max(0.0, AnnoyanceScore - decayAmount);

            // Repair historical trust slowly (e.g. +0.02 per hour of calm)
            if (AnnoyanceScore < 2.0)
            {
                HistoricalTrustIndex = Math.Min(1.0, HistoricalTrustIndex + (timeSpanPassed.TotalHours * 0.02));
            }
            SaveState();
        }
    }

    private void LoadState()
    {
        lock (_lock)
        {
            if (!File.Exists(_stateFilePath)) return;
            try
            {
                var json = File.ReadAllText(_stateFilePath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("annoyance_score", out var pAnnoyance)) AnnoyanceScore = pAnnoyance.GetDouble();
                if (root.TryGetProperty("last_friction_at", out var pFriction)) LastFrictionAt = DateTimeOffset.Parse(pFriction.GetString() ?? string.Empty);
                if (root.TryGetProperty("historical_trust_index", out var pTrust)) HistoricalTrustIndex = pTrust.GetDouble();
            }
            catch
            {
                // Graceful fallback on parse error
            }
        }
    }

    private void SaveState()
    {
        lock (_lock)
        {
            try
            {
                var state = new
                {
                    annoyance_score = AnnoyanceScore,
                    last_friction_at = LastFrictionAt,
                    historical_trust_index = HistoricalTrustIndex
                };
                var tmpPath = _stateFilePath + ".tmp";
                File.WriteAllText(tmpPath, JsonSerializer.Serialize(state, JsonOptions));
                File.Move(tmpPath, _stateFilePath, overwrite: true);
            }
            catch
            {
                // Fallback
            }
        }
    }
}
