namespace Engram.Store.Wiki;

/// <summary>
/// Represents a single wiki node — an entity in the knowledge graph.
/// Stored as a Markdown file with YAML front matter in .engram/wiki/.
/// </summary>
public class WikiNode
{
    /// <summary>Unique identifier (slug-based, e.g., "project_engram").</summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>Human-readable title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Entity type: Person, Project, Goal, Concept, Document, Receipt, Decision.</summary>
    public WikiNodeType NodeType { get; set; }

    /// <summary>One-line summary of this entity.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Key facts about this entity. Each fact has text and source links.</summary>
    public List<WikiFact> Facts { get; set; } = new();

    /// <summary>Unanswered questions about this entity.</summary>
    public List<string> OpenQuestions { get; set; } = new();

    /// <summary>Related node IDs (for [[link]] generation).</summary>
    public List<string> Links { get; set; } = new();

    /// <summary>Salience score (1.0 = fresh, decays over time). Phase 7 uses this.</summary>
    public double Salience { get; set; } = 1.0;

    /// <summary>Confidence in the accumulated facts (0.0 to 1.0).</summary>
    public double Confidence { get; set; } = 1.0;

    /// <summary>When this node was last updated.</summary>
    public DateTimeOffset LastTouchedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When this node was first created.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Schema version for forward compatibility.</summary>
    public int Version { get; set; } = 1;
}

/// <summary>
/// A single fact within a wiki node, with source attribution.
/// </summary>
public class WikiFact
{
    /// <summary>The fact text.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Source event IDs that support this fact.</summary>
    public List<WikiSourceReference> Sources { get; set; } = new();

    /// <summary>When this fact was last confirmed/updated.</summary>
    public DateTimeOffset LastConfirmedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// A reference to a raw event that is the source of a wiki fact.
/// </summary>
public class WikiSourceReference
{
    /// <summary>The raw event ID.</summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>When the raw event was captured.</summary>
    public DateTimeOffset CapturedAt { get; set; }

    /// <summary>The source that captured this event (e.g., "file_watcher").</summary>
    public string Source { get; set; } = string.Empty;
}

public enum WikiNodeType
{
    Person,
    Project,
    Goal,
    Concept,
    Document,
    Receipt,
    Decision
}
