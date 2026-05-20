using Microsoft.Extensions.Logging;

namespace Engram.Store.Perception;

/// <summary>
/// Stores human corrections to Engram's interpretations.
/// 
/// This is the feedback loop that keeps Engram connected to reality.
/// Without it, the semantic graph slowly diverges from truth.
/// 
/// NOT RLHF. Real longitudinal correction:
/// - "This interpretation was wrong"
/// - "This pattern mattered"
/// - "Ignore this category"
/// - "This was temporary"
/// 
/// Corrections are stored persistently and influence:
/// - FalsePatternDetector (which modes are systematically wrong)
/// - CognitiveRestraintEngine (when to stay silent)
/// - AccuracyTracker (ground truth for validation)
/// </summary>
public class TruthCalibrationStore
{
    private readonly string _storePath;
    private readonly ILogger<TruthCalibrationStore>? _logger;
    private readonly List<CorrectionRecord> _corrections = new();
    private readonly object _lock = new();

    public TruthCalibrationStore(
        string storePath,
        ILogger<TruthCalibrationStore>? logger = null)
    {
        _storePath = storePath;
        _logger = logger;
        Directory.CreateDirectory(_storePath);
        LoadCorrections();
    }

    /// <summary>
    /// Record a human correction — "Engram was wrong about this."
    /// </summary>
    public CorrectionRecord AddCorrection(
        string snapshotId,
        string engramInterpretation,
        string actualBehavior,
        CorrectionType type,
        string? note = null)
    {
        var record = new CorrectionRecord
        {
            CorrectionId = Guid.NewGuid().ToString("n")[..12],
            SnapshotId = snapshotId,
            EngramInterpretation = engramInterpretation,
            ActualBehavior = actualBehavior,
            Type = type,
            Note = note,
            CorrectedAt = DateTimeOffset.UtcNow
        };

        lock (_lock)
        {
            _corrections.Add(record);
            SaveCorrections();
        }

        _logger?.LogInformation(
            "Human correction: '{Engram}' was actually '{Actual}' ({Type})",
            engramInterpretation, actualBehavior, type);

        return record;
    }

    /// <summary>
    /// Record that a mode interpretation was wrong.
    /// </summary>
    public CorrectionRecord CorrectMode(string snapshotId, string wrongMode, string correctMode,
        string? note = null)
    {
        return AddCorrection(snapshotId, wrongMode, correctMode, CorrectionType.WrongInterpretation, note);
    }

    /// <summary>
    /// Record that a pattern is not meaningful — "stop reading into this."
    /// </summary>
    public CorrectionRecord DismissPattern(string pattern, string? note = null)
    {
        return AddCorrection(string.Empty, pattern, "not_meaningful", CorrectionType.PatternDismissed, note);
    }

    /// <summary>
    /// Record that a situation was temporary — "this doesn't count."
    /// </summary>
    public CorrectionRecord MarkTemporary(string snapshotId, string interpretation, string? note = null)
    {
        return AddCorrection(snapshotId, interpretation, "temporary", CorrectionType.Temporary, note);
    }

    /// <summary>
    /// Record that a category should be ignored entirely.
    /// </summary>
    public CorrectionRecord IgnoreCategory(string category, string? note = null)
    {
        return AddCorrection(string.Empty, category, "ignored", CorrectionType.CategoryIgnored, note);
    }

    /// <summary>
    /// Get all corrections for a specific mode.
    /// </summary>
    public List<CorrectionRecord> GetCorrectionsForMode(string mode)
    {
        lock (_lock)
        {
            return _corrections
                .Where(c => c.EngramInterpretation == mode)
                .OrderByDescending(c => c.CorrectedAt)
                .ToList();
        }
    }

    /// <summary>
    /// Get corrections of a specific type.
    /// </summary>
    public List<CorrectionRecord> GetCorrectionsByType(CorrectionType type)
    {
        lock (_lock)
        {
            return _corrections
                .Where(c => c.Type == type)
                .OrderByDescending(c => c.CorrectedAt)
                .ToList();
        }
    }

    /// <summary>
    /// Get all corrections.
    /// </summary>
    public List<CorrectionRecord> GetAllCorrections()
    {
        lock (_lock)
        {
            return _corrections.ToList();
        }
    }

    /// <summary>
    /// Get a calibration summary — which modes have been corrected most?
    /// </summary>
    public CalibrationSummary GetSummary()
    {
        lock (_lock)
        {
            var modeCorrections = _corrections
                .Where(c => c.Type == CorrectionType.WrongInterpretation)
                .GroupBy(c => c.EngramInterpretation)
                .ToDictionary(g => g.Key, g => g.Count());

            var dismissedPatterns = _corrections
                .Where(c => c.Type == CorrectionType.PatternDismissed)
                .Select(c => c.EngramInterpretation)
                .ToList();

            var ignoredCategories = _corrections
                .Where(c => c.Type == CorrectionType.CategoryIgnored)
                .Select(c => c.EngramInterpretation)
                .ToList();

            return new CalibrationSummary
            {
                TotalCorrections = _corrections.Count,
                ModeCorrectionCounts = modeCorrections,
                DismissedPatterns = dismissedPatterns,
                IgnoredCategories = ignoredCategories,
                MostCorrectedMode = modeCorrections
                    .OrderByDescending(kv => kv.Value)
                    .FirstOrDefault().Key,
                GeneratedAt = DateTimeOffset.UtcNow
            };
        }
    }

    /// <summary>
    /// Check if a mode has been corrected frequently enough to warrant caution.
    /// </summary>
    public bool IsModeFrequentlyCorrected(string mode, int threshold = 3)
    {
        lock (_lock)
        {
            return _corrections.Count(c =>
                c.Type == CorrectionType.WrongInterpretation &&
                c.EngramInterpretation == mode) >= threshold;
        }
    }

    /// <summary>
    /// Check if a category has been explicitly ignored.
    /// </summary>
    public bool IsCategoryIgnored(string category)
    {
        lock (_lock)
        {
            return _corrections.Any(c =>
                c.Type == CorrectionType.CategoryIgnored &&
                c.EngramInterpretation == category);
        }
    }

    private void LoadCorrections()
    {
        var filePath = Path.Combine(_storePath, "corrections.json");
        if (!File.Exists(filePath)) return;

        try
        {
            var json = File.ReadAllText(filePath);
            var corrections = System.Text.Json.JsonSerializer.Deserialize<List<CorrectionRecord>>(json);
            if (corrections != null)
            {
                lock (_lock) _corrections.AddRange(corrections);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load corrections from {Path}", filePath);
        }
    }

    private void SaveCorrections()
    {
        var filePath = Path.Combine(_storePath, "corrections.json");
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(_corrections,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to save corrections to {Path}", filePath);
        }
    }
}

/// <summary>
/// Types of human corrections.
/// </summary>
public enum CorrectionType
{
    /// <summary>The interpretation was simply wrong.</summary>
    WrongInterpretation,

    /// <summary>The pattern is not meaningful — stop reading into it.</summary>
    PatternDismissed,

    /// <summary>The situation was temporary and shouldn't count.</summary>
    Temporary,

    /// <summary>This entire category should be ignored.</summary>
    CategoryIgnored
}

/// <summary>
/// A single human correction record.
/// </summary>
public record CorrectionRecord
{
    public string CorrectionId { get; init; } = string.Empty;
    public string SnapshotId { get; init; } = string.Empty;
    public string EngramInterpretation { get; init; } = string.Empty;
    public string ActualBehavior { get; init; } = string.Empty;
    public CorrectionType Type { get; init; }
    public string? Note { get; init; }
    public DateTimeOffset CorrectedAt { get; init; }
}

/// <summary>
/// Summary of calibration state.
/// </summary>
public record CalibrationSummary
{
    public int TotalCorrections { get; init; }
    public Dictionary<string, int> ModeCorrectionCounts { get; init; } = new();
    public List<string> DismissedPatterns { get; init; } = new();
    public List<string> IgnoredCategories { get; init; } = new();
    public string? MostCorrectedMode { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
}
