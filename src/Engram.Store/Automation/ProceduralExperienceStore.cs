using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Engram.Store.Automation;

public class ProceduralExperienceEntry
{
    public string AppName { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
    public ActionType ActionType { get; set; }
    public string Selector { get; set; } = string.Empty;
    public double AverageDurationMs { get; set; }
    public double Variance { get; set; }
    public double StandardDeviationMs { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public int ConsecutiveFailures { get; set; }
    public double DecayConfidence { get; set; } = 1.0;
    public DateTimeOffset LastAccessedTime { get; set; } = DateTimeOffset.UtcNow;
    public List<string> SeenModals { get; set; } = new();
}

public class ProceduralExperienceStore
{
    private readonly string _filePath;
    private readonly double _halfLifeDays;
    private readonly Dictionary<string, ProceduralExperienceEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public ProceduralExperienceStore(string? baseDir = null, double halfLifeDays = 7.0)
    {
        var baseDirectory = baseDir ?? Directory.GetCurrentDirectory();
        _filePath = Path.Combine(baseDirectory, ".engram", "automation", "experience.json");
        _halfLifeDays = halfLifeDays;
        Load();
    }

    private string GetKey(string app, string version, ActionType type, string selector)
    {
        return $"{app}:{version}:{type}:{selector}";
    }

    private void Load()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    var list = JsonSerializer.Deserialize<List<ProceduralExperienceEntry>>(json);
                    if (list != null)
                    {
                        foreach (var entry in list)
                        {
                            var key = GetKey(entry.AppName, entry.AppVersion, entry.ActionType, entry.Selector);
                            _cache[key] = entry;
                        }
                    }
                }
            }
            catch
            {
                // Fallback / ignore corrupt files
            }
        }
    }

    private void Save()
    {
        lock (_lock)
        {
            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (dir != null && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                var list = new List<ProceduralExperienceEntry>(_cache.Values);
                var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch
            {
                // Ignore save errors
            }
        }
    }

    public void RecordMetric(string app, string version, ActionType type, string selector, TimeSpan duration, bool success)
    {
        var key = GetKey(app, version, type, selector ?? string.Empty);
        lock (_lock)
        {
            if (!_cache.TryGetValue(key, out var entry))
            {
                entry = new ProceduralExperienceEntry
                {
                    AppName = app,
                    AppVersion = version,
                    OsVersion = Environment.OSVersion.ToString(),
                    ActionType = type,
                    Selector = selector ?? string.Empty,
                    DecayConfidence = 1.0,
                    LastAccessedTime = DateTimeOffset.UtcNow
                };
                _cache[key] = entry;
            }

            // Apply temporal decay to historical confidence before updating
            var now = DateTimeOffset.UtcNow;
            var elapsedDays = (now - entry.LastAccessedTime).TotalDays;
            entry.DecayConfidence = entry.DecayConfidence * Math.Pow(0.5, elapsedDays / _halfLifeDays);

            // Record timing metrics
            if (success)
            {
                var totalSuccess = entry.SuccessCount;
                var durationMs = duration.TotalMilliseconds;
                
                if (totalSuccess == 0)
                {
                    entry.AverageDurationMs = durationMs;
                    entry.Variance = 0;
                    entry.StandardDeviationMs = 0;
                }
                else
                {
                    var oldMean = entry.AverageDurationMs;
                    var newMean = oldMean + (durationMs - oldMean) / (totalSuccess + 1);
                    var oldVar = entry.Variance;
                    // Welford's algorithm for updating variance online
                    entry.Variance = (oldVar * totalSuccess + (durationMs - oldMean) * (durationMs - newMean)) / (totalSuccess + 1);
                    entry.AverageDurationMs = newMean;
                    entry.StandardDeviationMs = Math.Sqrt(entry.Variance);
                }

                entry.SuccessCount++;
                entry.ConsecutiveFailures = 0;
                // Reset/refresh confidence on success
                entry.DecayConfidence = 1.0;
            }
            else
            {
                entry.FailureCount++;
                entry.ConsecutiveFailures++;
                // Decrease confidence slightly on failure
                entry.DecayConfidence = Math.Max(0.0, entry.DecayConfidence - 0.2);
            }

            entry.LastAccessedTime = now;
            Save();
        }
    }

    public TimeSpan GetRecommendedDelay(string app, string version, ActionType type, string selector)
    {
        var key = GetKey(app, version, type, selector ?? string.Empty);
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                var now = DateTimeOffset.UtcNow;
                var elapsedDays = (now - entry.LastAccessedTime).TotalDays;
                var currentConfidence = entry.DecayConfidence * Math.Pow(0.5, elapsedDays / _halfLifeDays);
                
                // Update access time but don't save immediately (read operation)
                entry.LastAccessedTime = now;

                if (currentConfidence >= 0.2 && entry.SuccessCount > 0)
                {
                    // Recommended delay: Average duration * 1.5, with minimum/maximum caps
                    var ms = entry.AverageDurationMs * 1.5;
                    return TimeSpan.FromMilliseconds(Math.Max(50, Math.Min(10000, ms)));
                }
            }
        }

        // Return a default delay if confidence is low or entry is missing
        return GetDefaultDelayForType(type);
    }

    public ProceduralExperienceEntry? GetEntry(string app, string version, ActionType type, string selector)
    {
        var key = GetKey(app, version, type, selector ?? string.Empty);
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                return new ProceduralExperienceEntry
                {
                    AppName = entry.AppName,
                    AppVersion = entry.AppVersion,
                    OsVersion = entry.OsVersion,
                    ActionType = entry.ActionType,
                    Selector = entry.Selector,
                    AverageDurationMs = entry.AverageDurationMs,
                    Variance = entry.Variance,
                    StandardDeviationMs = entry.StandardDeviationMs,
                    SuccessCount = entry.SuccessCount,
                    FailureCount = entry.FailureCount,
                    ConsecutiveFailures = entry.ConsecutiveFailures,
                    DecayConfidence = entry.DecayConfidence,
                    LastAccessedTime = entry.LastAccessedTime,
                    SeenModals = new List<string>(entry.SeenModals)
                };
            }
        }
        return null;
    }

    public double GetConfidence(string app, string version, ActionType type, string selector)
    {
        var key = GetKey(app, version, type, selector ?? string.Empty);
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                var now = DateTimeOffset.UtcNow;
                var elapsedDays = (now - entry.LastAccessedTime).TotalDays;
                return entry.DecayConfidence * Math.Pow(0.5, elapsedDays / _halfLifeDays);
            }
        }
        return 0.0;
    }

    public void AddSeenModal(string app, string version, ActionType type, string selector, string modalTitle)
    {
        var key = GetKey(app, version, type, selector ?? string.Empty);
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                if (!entry.SeenModals.Contains(modalTitle))
                {
                    entry.SeenModals.Add(modalTitle);
                    Save();
                }
            }
        }
    }

    private TimeSpan GetDefaultDelayForType(ActionType type)
    {
        return type switch
        {
            ActionType.Navigate => TimeSpan.FromMilliseconds(2000),
            ActionType.Click => TimeSpan.FromMilliseconds(500),
            ActionType.Type => TimeSpan.FromMilliseconds(100),
            ActionType.KeyPress => TimeSpan.FromMilliseconds(50),
            ActionType.Wait => TimeSpan.FromMilliseconds(1000),
            ActionType.Scroll => TimeSpan.FromMilliseconds(300),
            ActionType.Select => TimeSpan.FromMilliseconds(500),
            ActionType.Upload => TimeSpan.FromMilliseconds(1000),
            ActionType.Download => TimeSpan.FromMilliseconds(1500),
            _ => TimeSpan.FromMilliseconds(200)
        };
    }
}
