using Engram.Store.Events;
using Engram.Store.Wiki;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Memory;

/// <summary>
/// Pipeline that bridges conversation memory extraction to wiki metabolism.
/// 
/// Flow:
///   conversation → ConversationMemoryExtractor → candidates → 
///   ConversationMemoryPipeline → RawEvents → WikiMetabolizer.ProcessEvent() → wiki nodes
/// 
/// This is THE bridge that makes Engram remember conversations.
/// </summary>
public class ConversationMemoryPipeline
{
    private readonly ConversationMemoryExtractor _extractor;
    private readonly WikiMetabolizer _metabolizer;
    private readonly IEventBus? _eventBus;
    private readonly ILogger<ConversationMemoryPipeline>? _logger;

    public ConversationMemoryPipeline(
        ConversationMemoryExtractor extractor,
        WikiMetabolizer metabolizer,
        IEventBus? eventBus = null,
        ILogger<ConversationMemoryPipeline>? logger = null)
    {
        _extractor = extractor;
        _metabolizer = metabolizer;
        _eventBus = eventBus;
        _logger = logger;
    }

    /// <summary>
    /// Process a conversation exchange: extract memories and feed to wiki metabolizer.
    /// Call after each chat response.
    /// Returns the IDs of wiki nodes that were created or updated.
    /// </summary>
    public ConversationMemoryResult ProcessConversation(string userMessage, string assistantResponse)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new ConversationMemoryResult();

        try
        {
            // Step 1: Extract memory candidates
            var candidates = _extractor.Extract(userMessage, assistantResponse);
            result.CandidatesExtracted = candidates.Count;

            if (candidates.Count == 0)
            {
                result.Success = true;
                result.Duration = sw.Elapsed;
                return result;
            }

            // Step 2: Convert candidates to RawEvents
            var events = candidates.Select(ConvertToRawEvent).ToList();

            // Step 3: Feed to metabolizer
            var affectedNodes = _metabolizer.ProcessEvents(events);
            result.NodesCreated = affectedNodes.Count;
            result.AffectedNodeIds = affectedNodes.ToList();

            result.Success = true;
            _logger?.LogInformation(
                "Conversation processed: {Candidates} candidates → {Nodes} wiki nodes in {Ms}ms",
                result.CandidatesExtracted, result.NodesCreated, sw.ElapsedMilliseconds);

            // Publish events to the bus
            _eventBus?.Publish(new EventEnvelope
            {
                EventType = EventTypes.MemoryExtracted,
                Source = "conversation_pipeline",
                Payload = new { Candidates = result.CandidatesExtracted, Nodes = result.NodesCreated }
            });

            foreach (var nodeId in affectedNodes)
            {
                _eventBus?.Publish(new EventEnvelope
                {
                    EventType = EventTypes.WikiNodeUpdated,
                    Source = "conversation_pipeline",
                    Payload = new { NodeId = nodeId }
                });
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            _logger?.LogError(ex, "Failed to process conversation memory");
        }

        result.Duration = sw.Elapsed;
        return result;
    }

    /// <summary>
    /// Convert a ConversationMemoryCandidate to a RawEvent for the metabolizer.
    /// </summary>
    private static RawEvent ConvertToRawEvent(ConversationMemoryCandidate candidate)
    {
        return new RawEvent
        {
            EventId = Guid.NewGuid().ToString("n"),
            EventType = MapMemoryTypeToEventType(candidate.MemoryType),
            CapturedAt = candidate.CapturedAt,
            Source = "conversation",
            SourceUri = null,
            ActiveWindow = null,
            Text = candidate.Fact,
            Metadata = new Dictionary<string, string>
            {
                ["memory_type"] = candidate.MemoryType.ToString(),
                ["title"] = candidate.Title,
                ["confidence"] = candidate.Confidence.ToString("F2"),
                ["source_role"] = candidate.SourceRole,
                ["source_message"] = candidate.SourceMessage
            },
            PrivacyClass = "private",
            Hash = ComputeHash(candidate.Fact),
            ProcessingStatus = "pending"
        };
    }

    /// <summary>
    /// Map MemoryType to event type string for the metabolizer.
    /// The metabolizer's ExtractEntities handles: file_change, clipboard, email, and default.
    /// We use "conversation" as the event type, which falls through to the default case.
    /// The metadata carries the semantic type information.
    /// </summary>
    private static string MapMemoryTypeToEventType(MemoryType memoryType)
    {
        return memoryType switch
        {
            MemoryType.Person => "conversation_person",
            MemoryType.Project => "conversation_project",
            MemoryType.Goal => "conversation_goal",
            MemoryType.Decision => "conversation_decision",
            MemoryType.Preference => "conversation_preference",
            MemoryType.Anxiety => "conversation_anxiety",
            MemoryType.Task => "conversation_task",
            MemoryType.Concept => "conversation_concept",
            _ => "conversation"
        };
    }

    private static string ComputeHash(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash)[..16];
    }
}

/// <summary>
/// Result of processing a conversation through the memory pipeline.
/// </summary>
public class ConversationMemoryResult
{
    public bool Success { get; set; }
    public int CandidatesExtracted { get; set; }
    public int NodesCreated { get; set; }
    public List<string> AffectedNodeIds { get; set; } = new();
    public TimeSpan Duration { get; set; }
    public string? Error { get; set; }
}
