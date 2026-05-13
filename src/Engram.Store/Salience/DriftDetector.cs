using Engram.Store.Wiki;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Salience;

/// <summary>
/// Detects drift between new raw events and stored wiki facts.
/// Rule-based detection — no cloud model required.
/// </summary>
public class DriftDetector
{
    private readonly WikiNodeStore _nodeStore;
    private readonly ILogger<DriftDetector>? _logger;

    public DriftDetector(WikiNodeStore nodeStore, ILogger<DriftDetector>? logger = null)
    {
        _nodeStore = nodeStore;
        _logger = logger;
    }

    /// <summary>
    /// Analyze a raw event for drift against wiki memory.
    /// Returns list of detected drift alerts.
    /// </summary>
    public List<DriftAlert> DetectDrift(RawEvent rawEvent)
    {
        var alerts = new List<DriftAlert>();
        var nodes = _nodeStore.LoadAll();

        foreach (var node in nodes)
        {
            // Check for keyword contradictions
            var contradiction = DetectKeywordContradiction(rawEvent, node);
            if (contradiction != null)
                alerts.Add(contradiction);

            // Check for status changes
            var statusChange = DetectStatusChange(rawEvent, node);
            if (statusChange != null)
                alerts.Add(statusChange);

            // Check for date conflicts
            var dateConflict = DetectDateConflict(rawEvent, node);
            if (dateConflict != null)
                alerts.Add(dateConflict);
        }

        if (alerts.Count > 0)
            _logger?.LogWarning("Detected {Count} drift alerts for event {EventId}", alerts.Count, rawEvent.EventId);

        return alerts;
    }

    /// <summary>
    /// Analyze multiple raw events for drift.
    /// </summary>
    public List<DriftAlert> DetectDriftBatch(IEnumerable<RawEvent> events)
    {
        var allAlerts = new List<DriftAlert>();
        foreach (var evt in events)
            allAlerts.AddRange(DetectDrift(evt));
        return allAlerts;
    }

    private DriftAlert? DetectKeywordContradiction(RawEvent rawEvent, WikiNode node)
    {
        if (string.IsNullOrEmpty(rawEvent.Text)) return null;

        var eventText = rawEvent.Text.ToLowerInvariant();

        // Look for negation patterns
        var negationPatterns = new[] { "not ", "no longer ", "cancelled", "canceled", "stopped", "ended", "rejected", "denied" };

        foreach (var pattern in negationPatterns)
        {
            if (!eventText.Contains(pattern)) continue;

            // Check if the negated concept appears in wiki facts or summary
            var allTexts = node.Facts.Select(f => f.Text).ToList();
            if (!string.IsNullOrWhiteSpace(node.Summary))
                allTexts.Add(node.Summary);

            foreach (var factText in allTexts)
            {
                var factWords = factText.ToLowerInvariant()
                    .Split(new[] { ' ', ',', '.', ';', ':', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => w.Length >= 4)
                    .ToHashSet();

                var eventWords = eventText
                    .Split(new[] { ' ', ',', '.', ';', ':', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => w.Length >= 4 && !negationPatterns.Any(np => w.Contains(np.Trim())))
                    .ToHashSet();

                var overlap = factWords.Intersect(eventWords).Count();
                if (overlap >= 1)
                {
                    return new DriftAlert
                    {
                        NodeId = node.NodeId,
                        Description = "Possible contradiction: " + Truncate(rawEvent.Text, 80) + " vs wiki: " + Truncate(factText, 80),
                        Severity = DriftSeverity.Medium,
                        SourceEventIds = new List<string> { rawEvent.EventId }
                    };
                }
            }
        }

        return null;
    }

    private DriftAlert? DetectStatusChange(RawEvent rawEvent, WikiNode node)
    {
        if (string.IsNullOrEmpty(rawEvent.Text)) return null;

        var eventText = rawEvent.Text.ToLowerInvariant();

        // Status indicators
        var completedPatterns = new[] { "completed", "finished", "done", "shipped", "launched", "released" };
        var blockedPatterns = new[] { "blocked", "stalled", "delayed", "postponed", "on hold" };

        bool eventCompleted = completedPatterns.Any(p => eventText.Contains(p));
        bool eventBlocked = blockedPatterns.Any(p => eventText.Contains(p));

        if (!eventCompleted && !eventBlocked) return null;

        // Check if wiki has conflicting status
        var allTexts = node.Facts.Select(f => f.Text).ToList();
        if (!string.IsNullOrWhiteSpace(node.Summary))
            allTexts.Add(node.Summary);

        foreach (var text in allTexts)
        {
            var factLower = text.ToLowerInvariant();

            if (eventCompleted && blockedPatterns.Any(p => factLower.Contains(p)))
            {
                return new DriftAlert
                {
                    NodeId = node.NodeId,
                    Description = "Status conflict: event says completed, wiki says blocked. " + Truncate(rawEvent.Text, 60),
                    Severity = DriftSeverity.High,
                    SourceEventIds = new List<string> { rawEvent.EventId }
                };
            }

            if (eventBlocked && completedPatterns.Any(p => factLower.Contains(p)))
            {
                return new DriftAlert
                {
                    NodeId = node.NodeId,
                    Description = "Status conflict: event says blocked, wiki says completed. " + Truncate(rawEvent.Text, 60),
                    Severity = DriftSeverity.High,
                    SourceEventIds = new List<string> { rawEvent.EventId }
                };
            }
        }

        return null;
    }

    private DriftAlert? DetectDateConflict(RawEvent rawEvent, WikiNode node)
    {
        // Simple date conflict: event mentions a date that contradicts wiki
        // This is a basic check — full date parsing would be more robust
        return null; // Phase 7: basic detection only
    }

    private static string Truncate(string text, int max)
    {
        return text.Length > max ? text[..(max - 3)] + "..." : text;
    }
}
