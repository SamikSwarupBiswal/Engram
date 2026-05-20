using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Wiki;

public class WikiMetabolizer : IDisposable
{
    private readonly WikiNodeStore _store;
    private readonly ILogger<WikiMetabolizer>? _logger;

    public WikiMetabolizer(WikiNodeStore store, ILogger<WikiMetabolizer>? logger = null)
    {
        _store = store;
        _logger = logger;
    }

    public IReadOnlyList<string> ProcessEvent(RawEvent rawEvent)
    {
        ArgumentNullException.ThrowIfNull(rawEvent);
        var affected = new List<string>();

        foreach (var (nodeId, nodeType, title, fact) in ExtractEntities(rawEvent))
        {
            var existing = _store.Load(nodeId);
            if (existing != null)
            {
                MergeNode(existing, fact, rawEvent);
                _store.Save(existing);
                _logger?.LogInformation("Updated wiki node: {NodeId}", nodeId);
            }
            else
            {
                var node = CreateNode(nodeId, nodeType, title, fact, rawEvent);
                _store.Save(node);
                _logger?.LogInformation("Created wiki node: {NodeId} ({Type})", nodeId, nodeType);
            }
            affected.Add(nodeId);
        }
        return affected;
    }

    public IReadOnlyList<string> ProcessEvents(IEnumerable<RawEvent> events)
    {
        var affected = new HashSet<string>();
        foreach (var evt in events)
            foreach (var nodeId in ProcessEvent(evt))
                affected.Add(nodeId);
        return affected.ToList();
    }

    private IEnumerable<(string nodeId, WikiNodeType type, string title, string fact)> ExtractEntities(RawEvent rawEvent)
    {
        var entities = new List<(string, WikiNodeType, string, string)>();

        switch (rawEvent.EventType)
        {
            case "file_change":
                if (!string.IsNullOrEmpty(rawEvent.Text))
                {
                    var docTitle = Path.GetFileNameWithoutExtension(rawEvent.Text);
                    if (string.IsNullOrWhiteSpace(docTitle)) docTitle = rawEvent.Text;
                    entities.Add((Slugify(docTitle), WikiNodeType.Document, docTitle,
                        "File changed: " + rawEvent.Text));
                }
                break;
            case "clipboard":
                if (!string.IsNullOrEmpty(rawEvent.Text))
                {
                    var conceptTitle = rawEvent.Text.Length > 60 ? rawEvent.Text[..57] + "..." : rawEvent.Text;
                    entities.Add(("concept_" + Slugify(conceptTitle), WikiNodeType.Concept, conceptTitle,
                        "Clipboard content: " + (rawEvent.Text.Length > 200 ? rawEvent.Text[..197] + "..." : rawEvent.Text)));
                }
                break;
            case "email":
                if (!string.IsNullOrEmpty(rawEvent.Text))
                {
                    var subject = rawEvent.Text.Split('\n').FirstOrDefault() ?? rawEvent.Text;
                    if (subject.Length > 60) subject = subject[..57] + "...";
                    entities.Add(("email_" + Slugify(subject), WikiNodeType.Document, subject,
                        "Email: " + (rawEvent.Text.Length > 200 ? rawEvent.Text[..197] + "..." : rawEvent.Text)));
                }
                break;
            default:
                // Handle conversation events from ConversationMemoryPipeline
                if (rawEvent.EventType.StartsWith("conversation_") && rawEvent.Metadata != null)
                {
                    var memType = rawEvent.Metadata.GetValueOrDefault("memory_type", "");
                    var title = rawEvent.Metadata.GetValueOrDefault("title", "");
                    var wikiNodeType = MapConversationType(memType);

                    if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(rawEvent.Text))
                    {
                        var nodeId = MapConversationPrefix(wikiNodeType) + "_" + Slugify(title);
                        entities.Add((nodeId, wikiNodeType, title, rawEvent.Text));
                    }
                }
                else if (!string.IsNullOrEmpty(rawEvent.Text))
                {
                    var title = rawEvent.Text.Length > 60 ? rawEvent.Text[..57] + "..." : rawEvent.Text;
                    entities.Add(("event_" + Slugify(title), WikiNodeType.Concept, title,
                        "Event (" + rawEvent.EventType + "): " + (rawEvent.Text.Length > 200 ? rawEvent.Text[..197] + "..." : rawEvent.Text)));
                }
                break;
        }
        return entities;
    }

    private static WikiNodeType MapConversationType(string memoryType)
    {
        return memoryType switch
        {
            "Person" => WikiNodeType.Person,
            "Project" => WikiNodeType.Project,
            "Goal" => WikiNodeType.Goal,
            "Decision" => WikiNodeType.Decision,
            "Preference" => WikiNodeType.Concept,
            "Anxiety" => WikiNodeType.Concept,
            "Task" => WikiNodeType.Concept,
            _ => WikiNodeType.Concept
        };
    }

    private static string MapConversationPrefix(WikiNodeType type)
    {
        return type switch
        {
            WikiNodeType.Person => "person",
            WikiNodeType.Project => "project",
            WikiNodeType.Goal => "goal",
            WikiNodeType.Decision => "decision",
            _ => "concept"
        };
    }

    private WikiNode CreateNode(string nodeId, WikiNodeType type, string title, string fact, RawEvent source)
    {
        return new WikiNode
        {
            NodeId = nodeId,
            Title = title,
            NodeType = type,
            Summary = fact,
            Facts = new List<WikiFact>
            {
                new WikiFact
                {
                    Text = fact,
                    Sources = new List<WikiSourceReference>
                    {
                        new() { EventId = source.EventId, CapturedAt = source.CapturedAt, Source = source.Source }
                    }
                }
            },
            Salience = 1.0,
            Confidence = 1.0,
            LastTouchedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private void MergeNode(WikiNode existing, string newFact, RawEvent source)
    {
        var existingFact = existing.Facts.FirstOrDefault(f => f.Text == newFact);
        if (existingFact != null)
        {
            existingFact.Sources.Add(new WikiSourceReference
            {
                EventId = source.EventId,
                CapturedAt = source.CapturedAt,
                Source = source.Source
            });
            existingFact.LastConfirmedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            existing.Facts.Add(new WikiFact
            {
                Text = newFact,
                Sources = new List<WikiSourceReference>
                {
                    new() { EventId = source.EventId, CapturedAt = source.CapturedAt, Source = source.Source }
                }
            });
        }
        existing.LastTouchedAt = DateTimeOffset.UtcNow;
        existing.Salience = 1.0;
    }

    private static string Slugify(string text)
    {
        return Regex.Replace(text.ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_');
    }

    public void Dispose() { _store.Dispose(); }
}
