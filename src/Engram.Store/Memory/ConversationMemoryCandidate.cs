namespace Engram.Store.Memory;

/// <summary>
/// A structured memory candidate extracted from a conversation.
/// These are fed into the WikiMetabolizer to create/update wiki nodes.
/// </summary>
public class ConversationMemoryCandidate
{
    /// <summary>The type of memory extracted.</summary>
    public MemoryType MemoryType { get; set; }

    /// <summary>Human-readable title for the wiki node.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The extracted fact text.</summary>
    public string Fact { get; set; } = string.Empty;

    /// <summary>Confidence score (0.0 to 1.0).</summary>
    public double Confidence { get; set; } = 0.8;

    /// <summary>The original message that contained this memory.</summary>
    public string SourceMessage { get; set; } = string.Empty;

    /// <summary>Role of the speaker (user or assistant).</summary>
    public string SourceRole { get; set; } = "user";

    /// <summary>Timestamp of the conversation.</summary>
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Types of memories that can be extracted from conversations.
/// Maps to WikiNodeType for metabolizer integration.
/// </summary>
public enum MemoryType
{
    /// <summary>A person mentioned in conversation (e.g., "my friend Alex").</summary>
    Person,

    /// <summary>A project being worked on (e.g., "I'm building Engram").</summary>
    Project,

    /// <summary>A goal or aspiration (e.g., "I want to become...").</summary>
    Goal,

    /// <summary>A decision made (e.g., "I decided to...").</summary>
    Decision,

    /// <summary>A preference or opinion (e.g., "I prefer...").</summary>
    Preference,

    /// <summary>An anxiety or concern (e.g., "I'm worried about...").</summary>
    Anxiety,

    /// <summary>A task or action item (e.g., "I need to...").</summary>
    Task,

    /// <summary>A general concept or topic discussed.</summary>
    Concept
}
