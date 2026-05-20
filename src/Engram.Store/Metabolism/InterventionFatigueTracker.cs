using Microsoft.Extensions.Logging;

namespace Engram.Store.Metabolism;

/// <summary>
/// Tracks intervention fatigue — how the user responds to Engram's interventions over time.
/// 
/// The next failure mode is NOT insufficient cognition. It's EXCESSIVE cognition.
/// A system that constantly interprets becomes exhausting, uncanny, invasive.
/// 
/// This tracker measures:
/// - Dismissal rate (how often interventions are dismissed)
/// - Time-to-dismissal (how quickly interventions are dismissed)
/// - Silence periods (how long user stays quiet after intervention)
/// - Ignored interventions (interventions that received no response)
/// - Perceived usefulness (when user acts on intervention)
/// 
/// These metrics become the UX truth layer — they tell you whether
/// the organism is helping or exhausting.
/// </summary>
public class InterventionFatigueTracker
{
    private readonly ILogger<InterventionFatigueTracker>? _logger;
    private readonly List<FatigueEvent> _events = new();
    private readonly object _lock = new();

    /// <summary>Window for measuring fatigue (default: 7 days).</summary>
    public TimeSpan MeasurementWindow { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Fatigue threshold — above this, the organism should reduce output.</summary>
    public double FatigueThreshold { get; set; } = 0.6;

    public InterventionFatigueTracker(ILogger<InterventionFatigueTracker>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Record that an intervention was presented to the user.
    /// </summary>
    public void RecordInterventionPresented(string interventionId, string category)
    {
        Record(new FatigueEvent
        {
            InterventionId = interventionId,
            Category = category,
            Type = FatigueEventType.Presented,
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// Record that an intervention was acknowledged (user read it).
    /// </summary>
    public void RecordInterventionAcknowledged(string interventionId)
    {
        Record(new FatigueEvent
        {
            InterventionId = interventionId,
            Type = FatigueEventType.Acknowledged,
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// Record that an intervention was acted on (user did what it suggested).
    /// This is the best outcome — the intervention was useful.
    /// </summary>
    public void RecordInterventionActed(string interventionId)
    {
        Record(new FatigueEvent
        {
            InterventionId = interventionId,
            Type = FatigueEventType.Acted,
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// Record that an intervention was dismissed (user explicitly rejected it).
    /// </summary>
    public void RecordInterventionDismissed(string interventionId, TimeSpan? timeToDismiss = null)
    {
        Record(new FatigueEvent
        {
            InterventionId = interventionId,
            Type = FatigueEventType.Dismissed,
            Duration = timeToDismiss,
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// Record that an intervention was ignored (user didn't respond at all).
    /// </summary>
    public void RecordInterventionIgnored(string interventionId)
    {
        Record(new FatigueEvent
        {
            InterventionId = interventionId,
            Type = FatigueEventType.Ignored,
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// Record a silence period after an intervention.
    /// </summary>
    public void RecordPostInterventionSilence(string interventionId, TimeSpan silenceDuration)
    {
        Record(new FatigueEvent
        {
            InterventionId = interventionId,
            Type = FatigueEventType.Silence,
            Duration = silenceDuration,
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// Generate a fatigue report for the measurement window.
    /// </summary>
    public FatigueReport GenerateReport()
    {
        var cutoff = DateTimeOffset.UtcNow - MeasurementWindow;

        List<FatigueEvent> events;
        lock (_lock)
        {
            events = _events.Where(e => e.Timestamp >= cutoff).ToList();
        }

        var presented = events.Where(e => e.Type == FatigueEventType.Presented).ToList();
        var dismissed = events.Where(e => e.Type == FatigueEventType.Dismissed).ToList();
        var ignored = events.Where(e => e.Type == FatigueEventType.Ignored).ToList();
        var acted = events.Where(e => e.Type == FatigueEventType.Acted).ToList();
        var silenceEvents = events.Where(e => e.Type == FatigueEventType.Silence).ToList();

        var totalInterventions = presented.Count;
        var dismissalRate = totalInterventions > 0 ? (double)dismissed.Count / totalInterventions : 0;
        var ignoreRate = totalInterventions > 0 ? (double)ignored.Count / totalInterventions : 0;
        var actionRate = totalInterventions > 0 ? (double)acted.Count / totalInterventions : 0;

        var avgTimeToDismiss = dismissed
            .Where(e => e.Duration.HasValue)
            .Select(e => e.Duration!.Value)
            .DefaultIfEmpty(TimeSpan.Zero)
            .Average(d => d.TotalSeconds);

        var avgSilenceAfter = silenceEvents
            .Select(e => e.Duration ?? TimeSpan.Zero)
            .DefaultIfEmpty(TimeSpan.Zero)
            .Average(d => d.TotalSeconds);

        // Fatigue score: higher = more fatigued
        // Weighted: dismissals (0.4) + ignores (0.3) + low action rate (0.3)
        var fatigueScore = (dismissalRate * 0.4) + (ignoreRate * 0.3) + ((1.0 - actionRate) * 0.3);

        // Per-category breakdown
        var categoryBreakdown = presented
            .GroupBy(e => e.Category)
            .ToDictionary(
                g => g.Key,
                g => new CategoryFatigue
                {
                    Category = g.Key,
                    Presented = g.Count(),
                    Dismissed = dismissed.Count(d => d.Category == g.Key),
                    Ignored = ignored.Count(i => i.Category == g.Key),
                    Acted = acted.Count(a => a.Category == g.Key)
                });

        return new FatigueReport
        {
            Period = MeasurementWindow,
            TotalInterventions = totalInterventions,
            Dismissed = dismissed.Count,
            Ignored = ignored.Count,
            Acted = acted.Count,
            DismissalRate = dismissalRate,
            IgnoreRate = ignoreRate,
            ActionRate = actionRate,
            AvgTimeToDismissSeconds = avgTimeToDismiss,
            AvgPostInterventionSilenceSeconds = avgSilenceAfter,
            FatigueScore = fatigueScore,
            IsFatigued = fatigueScore >= FatigueThreshold,
            CategoryBreakdown = categoryBreakdown,
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Check if the organism should reduce intervention frequency.
    /// </summary>
    public bool ShouldReduceFrequency()
    {
        var report = GenerateReport();
        return report.IsFatigued;
    }

    private void Record(FatigueEvent fatigueEvent)
    {
        lock (_lock)
        {
            _events.Add(fatigueEvent);
            // Keep last 1000 events
            while (_events.Count > 1000)
                _events.RemoveAt(0);
        }
    }
}

/// <summary>
/// Types of fatigue events.
/// </summary>
public enum FatigueEventType
{
    Presented,
    Acknowledged,
    Acted,
    Dismissed,
    Ignored,
    Silence
}

/// <summary>
/// A single fatigue event.
/// </summary>
public record FatigueEvent
{
    public string InterventionId { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public FatigueEventType Type { get; init; }
    public TimeSpan? Duration { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// Fatigue report for a time period.
/// </summary>
public record FatigueReport
{
    public TimeSpan Period { get; init; }
    public int TotalInterventions { get; init; }
    public int Dismissed { get; init; }
    public int Ignored { get; init; }
    public int Acted { get; init; }
    public double DismissalRate { get; init; }
    public double IgnoreRate { get; init; }
    public double ActionRate { get; init; }
    public double AvgTimeToDismissSeconds { get; init; }
    public double AvgPostInterventionSilenceSeconds { get; init; }
    public double FatigueScore { get; init; }
    public bool IsFatigued { get; init; }
    public Dictionary<string, CategoryFatigue> CategoryBreakdown { get; init; } = new();
    public DateTimeOffset GeneratedAt { get; init; }
}

/// <summary>
/// Fatigue metrics for a specific intervention category.
/// </summary>
public record CategoryFatigue
{
    public string Category { get; init; } = string.Empty;
    public int Presented { get; init; }
    public int Dismissed { get; init; }
    public int Ignored { get; init; }
    public int Acted { get; init; }
    public double DismissalRate => Presented > 0 ? (double)Dismissed / Presented : 0;
    public double ActionRate => Presented > 0 ? (double)Acted / Presented : 0;
}
