using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Engram.Store.Automation;
using Engram.Store.Automation.Skills;
using Engram.Store.Governance;
using Engram.Store.Inference;
using Engram.Store.Wiki;

namespace Engram.Store.Memory;

public sealed record TaskOutcomeStep(string Id, string Type, string Status, string? Error);

public sealed record TaskOutputArtifact(string ArtifactId, string? FileId, string Path,
    string HistoricalText, string Sha256, long SizeBytes, DateTimeOffset VerifiedAt,
    string CurrentState, string? CurrentSha256, long? CurrentSizeBytes, string RegistryState);

public sealed record TaskOutcome(string TaskId, string ConversationId, string State,
    DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, DateTimeOffset RecordedAt,
    string SourceEventId, string? Model, string? Provider, string? Tool, bool FromCache,
    string? Error, IReadOnlyList<TaskOutcomeStep> Steps, IReadOnlyList<TaskOutputArtifact> Artifacts)
{
    // Keep content-free suppression across subsequent state snapshots and restarts.
    public IReadOnlyList<string> SuppressedArtifactIds { get; init; } = [];
    public string? DisclosureIdentity { get; init; }
}

/// <summary>Source-linked outcomes for explicitly selected tasks in one conversation.
/// Historical generated text is replayable evidence, never a current file observation or permission grant.</summary>
public sealed class TaskOutcomeMemory
{
    private const int MaxSourceBytes = 4 * 1024 * 1024;
    private const int MaxArtifacts = 5;
    private const string Source = "task_outcome_memory";
    private readonly WorkspacePaths _paths;
    private readonly RawEventWriter _writer;
    private readonly WikiNodeStore _nodes;
    private readonly MemorySovereigntySystem _sovereignty;
    private readonly FileIdentityRegistry? _registry;
    private readonly object _gate = new();
    private readonly ContentHasher _hasher = new();
    private long _captureGeneration;
    public long CaptureGeneration { get { lock (_gate) return _captureGeneration; } }
    private string IndexDirectory => Path.Combine(_paths.Runs, "task-outcomes");

    private sealed record SourcePointer(string TaskId, string ConversationId, string EventId,
        string Date, string Hash);
    private sealed record ConversationScope(string ConversationId, Dictionary<string, string> ReplyProviders);

    public TaskOutcomeMemory(WorkspacePaths paths, RawEventWriter writer, WikiNodeStore nodes,
        MemorySovereigntySystem sovereignty, FileIdentityRegistry? registry = null)
    {
        _paths = paths;
        _writer = writer;
        _nodes = nodes;
        _sovereignty = sovereignty;
        _registry = registry;
    }

    /// <summary>Provider provenance comes from the server's recorded response, never from a caller's marker.
    /// Unknown, pruned, or ambiguously attributed assistant text stays local.</summary>
    public IReadOnlyList<ChatMessage> ScopeConversation(string conversationId, IReadOnlyList<ChatMessage> messages)
    {
        ValidateId(conversationId, nameof(conversationId));
        ArgumentNullException.ThrowIfNull(messages);
        lock (_gate)
        {
            var scope = ReadConversationScope(conversationId);
            return messages.Select(message => string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                ? new ChatMessage { Role = message.Role, Content = message.Content,
                    MemoryProvider = SafeDisclosureIdentity(scope?.ReplyProviders.GetValueOrDefault(HashReply(message.Content))) }
                : message).ToArray();
        }
    }

    /// <summary>Stores only bounded response hashes and actual provider names; no conversation text.</summary>
    public void RecordConversationReply(string conversationId, string disclosureIdentity, string assistantText,
        long? captureGeneration = null)
    {
        ValidateId(conversationId, nameof(conversationId));
        if (!ValidProvider(disclosureIdentity)) throw new ArgumentException("A bounded disclosure identity is required.", nameof(disclosureIdentity));
        var identity = SafeDisclosureIdentity(disclosureIdentity);
        var hash = HashReply(assistantText);
        lock (_gate)
        {
            EnsureCaptureGeneration(captureGeneration);
            var scope = ReadConversationScope(conversationId) ?? new(conversationId, new(StringComparer.Ordinal));
            var previous = scope.ReplyProviders.GetValueOrDefault(hash);
            if (previous == identity || previous == "local") return;
            scope.ReplyProviders[hash] = previous == null ? identity : "local";
            while (scope.ReplyProviders.Count > 200) scope.ReplyProviders.Remove(scope.ReplyProviders.Keys.First());
            WriteAtomicJson(ConversationScopeFile(conversationId), scope);
        }
    }

    private string ConversationScopeFile(string conversationId) =>
        Path.Combine(_paths.Runs, "conversation-scopes", conversationId + ".json");

    private ConversationScope? ReadConversationScope(string conversationId)
    {
        var file = ConversationScopeFile(conversationId);
        EnsureStorageSafe(file);
        if (!File.Exists(file)) return null;
        var scope = ReadJson<ConversationScope>(file, 64 * 1024);
        if (scope.ConversationId != conversationId || scope.ReplyProviders == null || scope.ReplyProviders.Count > 200 ||
            scope.ReplyProviders.Any(entry => entry.Key.Length != 64 || !entry.Key.All(char.IsAsciiHexDigit) || !ValidProvider(entry.Value)))
            throw new InvalidDataException("Conversation provider scope verification failed.");
        return scope;
    }

    private static bool ValidProvider(string provider) => !string.IsNullOrWhiteSpace(provider) && provider.Length <= 128 &&
        provider.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private static string SafeDisclosureIdentity(string? identity) => identity is { Length: 73 } &&
        identity.StartsWith("cloud-v1-", StringComparison.Ordinal) && identity[9..].All(char.IsAsciiHexDigit)
            ? identity : "local";

    private static string HashReply(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > 1_000_000) throw new ArgumentException("The reply exceeds the provider scope size limit.", nameof(text));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>Invalidates in-flight writers and serializes deletion with outcome and reply writes.</summary>
    public void ResetForDeletion(Action delete)
    {
        ArgumentNullException.ThrowIfNull(delete);
        lock (_gate)
        {
            ++_captureGeneration;
            delete();
        }
    }

    private void EnsureCaptureGeneration(long? generation)
    {
        if (generation != null && generation != _captureGeneration)
            throw new InvalidOperationException("Memory was cleared after this work started. Earlier work cannot recreate it.");
    }

    public void RunIfCurrentGeneration(long? generation, Action write)
    {
        ArgumentNullException.ThrowIfNull(write);
        lock (_gate)
        {
            EnsureCaptureGeneration(generation);
            write();
        }
    }

    public TaskOutcome Record(AutomationTaskRecord task, string conversationId, long? captureGeneration = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        ValidateId(task.TaskId, nameof(task.TaskId));
        ValidateId(conversationId, nameof(conversationId));
        lock (_gate)
        {
            EnsureCaptureGeneration(captureGeneration);
            EnsureNotForgotten(task.TaskId);
            var previous = Read(task.TaskId);
            if (previous != null && previous.ConversationId != conversationId)
                throw new InvalidOperationException("The task belongs to another conversation.");
            if (task.Plan.Steps.Count > 256)
                throw new InvalidOperationException("The task has too many steps to record safely.");

            var now = DateTimeOffset.UtcNow;
            var eventId = "task_outcome_" + Guid.NewGuid().ToString("N");
            var planning = task.Plan.Planning;
            var artifacts = new List<TaskOutputArtifact>();
            var suppressed = new HashSet<string>(previous?.SuppressedArtifactIds ?? [], StringComparer.Ordinal);
            foreach (var artifact in previous?.Artifacts ?? [])
                if (artifact.FileId is { } fileId && _sovereignty.IsDeleted(fileId) ||
                    _registry?.IsKnownForgottenPath(artifact.Path) == true) suppressed.Add(artifact.ArtifactId);
            foreach (var step in task.Plan.Steps.Values)
            {
                var action = step.Action;
                if (artifacts.Count == MaxArtifacts) break;
                if (step.Status != StepStatus.Completed || action.Status != Automation.ActionStatus.Completed ||
                    action.TextFileOutput?.Matches(action) != true ||
                    string.IsNullOrWhiteSpace(planning?.Model) || string.IsNullOrWhiteSpace(planning.Provider)) continue;
                var artifactId = task.TaskId + ":" + step.Id;
                if (suppressed.Contains(artifactId)) continue;
                if (_registry?.IsKnownForgottenPath(action.Value!) == true)
                {
                    suppressed.Add(artifactId);
                    continue;
                }
                var historical = previous?.Artifacts.FirstOrDefault(artifact => artifact.ArtifactId == artifactId &&
                    artifact.Path == action.Value && artifact.HistoricalText == action.Target?.Text);
                if (historical != null)
                {
                    artifacts.Add(Revalidate(historical));
                    continue;
                }
                var artifact = VerifyGeneratedOutput(artifactId, action, now, eventId);
                if (artifact != null) artifacts.Add(artifact);
            }
            var outcome = new TaskOutcome(task.TaskId, conversationId, task.State.ToString(),
                task.CreatedAt, task.CompletedAt, now, eventId, planning?.Model, planning?.Provider,
                planning?.Tool, planning?.FromCache ?? false, Bound(task.Error),
                task.Plan.Steps.Values.Select(step => new TaskOutcomeStep(step.Id, step.Action.Type.ToString(),
                    step.Status.ToString(), Bound(step.Error))).ToArray(), artifacts)
            {
                SuppressedArtifactIds = suppressed.ToArray(), DisclosureIdentity = planning?.DisclosureIdentity
            };
            var content = JsonSerializer.Serialize(outcome);
            if (Encoding.UTF8.GetByteCount(content) > MaxSourceBytes / 2)
                throw new InvalidOperationException("The outcome exceeds the memory source size limit.");

            // The immutable raw event is authoritative; the replaceable pointer is only an index.
            EnsureStorageSafe(Path.Combine(_paths.Raw, now.ToString("yyyy-MM-dd")));
            var result = _writer.Write(new RawEvent
            {
                EventId = eventId, EventType = "task_outcome_recorded", CapturedAt = now,
                Source = Source, SourceUri = "task:" + task.TaskId, PrivacyClass = "private", Text = content
            });
            EnsureNotForgotten(task.TaskId);
            WritePointer(new(task.TaskId, conversationId, eventId, now.ToString("yyyy-MM-dd"), result.Hash));
            SaveProjection(outcome);
            EnsureNotForgotten(task.TaskId);
            return outcome;
        }
    }

    public IReadOnlyList<TaskOutcome> GetForConversation(string conversationId, IReadOnlyList<string> selectedTaskIds)
    {
        ValidateId(conversationId, nameof(conversationId));
        ArgumentNullException.ThrowIfNull(selectedTaskIds);
        if (selectedTaskIds.Count > 5) throw new ArgumentException("Select at most five task outcomes.", nameof(selectedTaskIds));
        foreach (var id in selectedTaskIds) ValidateId(id, nameof(selectedTaskIds));
        lock (_gate)
        {
            var outcomes = new List<TaskOutcome>();
            foreach (var id in selectedTaskIds.Distinct(StringComparer.Ordinal))
            {
                if (_sovereignty.IsDeleted("workflow_memory_" + id)) continue;
                var outcome = Read(id);
                if (outcome == null || outcome.ConversationId != conversationId) continue;
                var current = outcome with { Artifacts = outcome.Artifacts
                    .Where(artifact => (artifact.FileId == null || !_sovereignty.IsDeleted(artifact.FileId)) &&
                        _registry?.IsKnownForgottenPath(artifact.Path) != true)
                    .Select(Revalidate).ToArray() };
                if (!_sovereignty.IsDeleted("workflow_memory_" + id)) outcomes.Add(current);
            }
            return outcomes;
        }
    }

    private TaskOutputArtifact? VerifyGeneratedOutput(string id, AutomationAction action,
        DateTimeOffset verifiedAt, string sourceEventId)
    {
        var path = action.Value!;
        byte[] bytes;
        try
        {
            EnsureOutputPathSafe(path);
            var expected = new UTF8Encoding(false, true).GetBytes(action.Target!.Text!);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length != expected.LongLength || stream.Length > FileIdentityRegistry.MaxHashBytes) return null;
            bytes = new byte[expected.Length];
            stream.ReadExactly(bytes);
            if (!bytes.AsSpan().SequenceEqual(expected)) return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (_registry?.IsKnownForgottenPath(path) == true) return null;
        string? fileId = null;
        var registryState = "Unavailable";
        if (_registry?.CanObservePath(path) == true)
        {
            try
            {
                var reference = _registry.RegisterVerifiedFile(path, hash, bytes.LongLength, sourceEventId, verifiedAt);
                fileId = reference.FileId;
                registryState = reference.State;
            }
            catch (UnauthorizedAccessException) { /* Consent may have been revoked since the check. */ }
        }
        if (_registry?.IsKnownForgottenPath(path) == true) return null;
        return new(id, fileId, path, action.Target!.Text!, hash, bytes.LongLength, verifiedAt,
            "Unchanged", hash, bytes.LongLength, registryState);
    }

    private TaskOutputArtifact Revalidate(TaskOutputArtifact artifact)
    {
        try
        {
            EnsureOutputPathSafe(artifact.Path);
            using var stream = new FileStream(artifact.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > FileIdentityRegistry.MaxHashBytes)
                return artifact with { CurrentState = "Unavailable", CurrentSha256 = null, CurrentSizeBytes = stream.Length };
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            return artifact with { CurrentState = hash == artifact.Sha256 && stream.Length == artifact.SizeBytes ? "Unchanged" : "Changed",
                CurrentSha256 = hash, CurrentSizeBytes = stream.Length };
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return artifact with { CurrentState = "Missing", CurrentSha256 = null, CurrentSizeBytes = null };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return artifact with { CurrentState = "Unavailable", CurrentSha256 = null, CurrentSizeBytes = null };
        }
    }

    private TaskOutcome? Read(string taskId)
    {
        var pointerFile = Path.Combine(IndexDirectory, taskId + ".json");
        EnsureStorageSafe(pointerFile);
        if (!File.Exists(pointerFile)) return null;
        var pointer = ReadJson<SourcePointer>(pointerFile, 16 * 1024);
        if (pointer.TaskId != taskId || pointer.EventId == null || pointer.EventId.Length != 45 ||
            !pointer.EventId.StartsWith("task_outcome_", StringComparison.Ordinal) ||
            !pointer.EventId[13..].All(char.IsAsciiHexDigit) ||
            !DateOnly.TryParseExact(pointer.Date, "yyyy-MM-dd", out _))
            throw new InvalidDataException("Invalid task outcome source pointer.");
        ValidateId(pointer.ConversationId, nameof(pointer.ConversationId));
        var sourceFile = Path.Combine(_paths.Raw, pointer.Date, pointer.EventId + ".json");
        EnsureStorageSafe(sourceFile);
        var raw = ReadJson<RawEvent>(sourceFile, MaxSourceBytes);
        if (raw.EventId != pointer.EventId || raw.Source != Source || raw.EventType != "task_outcome_recorded" ||
            raw.PrivacyClass != "private" || raw.Hash != pointer.Hash || _hasher.ComputeHash(raw) != raw.Hash || raw.Text == null)
            throw new InvalidDataException("Task outcome source verification failed.");
        TaskOutcome outcome;
        try { outcome = JsonSerializer.Deserialize<TaskOutcome>(raw.Text) ?? throw new InvalidDataException("Empty outcome source."); }
        catch (JsonException ex) { throw new InvalidDataException("Invalid outcome source.", ex); }
        if (outcome.TaskId != taskId || outcome.ConversationId != pointer.ConversationId ||
            outcome.SourceEventId != pointer.EventId || outcome.Steps == null || outcome.Artifacts == null ||
            outcome.Steps.Count > 256 || outcome.Artifacts.Count > MaxArtifacts ||
            outcome.SuppressedArtifactIds == null || outcome.SuppressedArtifactIds.Count > 256)
            throw new InvalidDataException("Task outcome identity verification failed.");
        return outcome;
    }

    private static T ReadJson<T>(string path, int maximumBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maximumBytes) throw new InvalidDataException("Memory source exceeds its size limit.");
        try { return JsonSerializer.Deserialize<T>(stream) ?? throw new InvalidDataException("Empty memory source."); }
        catch (JsonException ex) { throw new InvalidDataException("Invalid memory source.", ex); }
    }

    private void WritePointer(SourcePointer pointer)
    {
        var file = Path.Combine(IndexDirectory, pointer.TaskId + ".json");
        WriteAtomicJson(file, pointer);
    }

    private static void WriteAtomicJson<T>(string file, T value)
    {
        EnsureStorageSafe(file);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        EnsureStorageSafe(file);
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value));
            File.Move(temporary, file, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private void SaveProjection(TaskOutcome outcome)
    {
        var nodeId = "workflow_memory_" + outcome.TaskId;
        EnsureStorageSafe(Path.Combine(_paths.Wiki, nodeId + ".md"));
        EnsureNotForgotten(outcome.TaskId);
        _nodes.Save(new WikiNode
        {
            NodeId = nodeId, NodeType = WikiNodeType.Workflow, Title = "Task outcome " + outcome.TaskId,
            Summary = "Recorded task state: " + outcome.State,
            CreatedAt = outcome.CreatedAt, LastTouchedAt = outcome.RecordedAt,
            ProvenanceWorkflowId = outcome.TaskId,
            Facts = [new() { Text = "Execution state: " + outcome.State, LastConfirmedAt = outcome.RecordedAt,
                Sources = [new() { EventId = outcome.SourceEventId, CapturedAt = outcome.RecordedAt, Source = Source }] }],
            Links = outcome.Artifacts.Where(artifact => artifact.FileId != null).Select(artifact => artifact.FileId!).ToList()
        });
        if (_sovereignty.IsDeleted(nodeId))
        {
            _nodes.Delete(nodeId);
            throw new InvalidOperationException("This task memory was forgotten.");
        }
    }

    private void EnsureNotForgotten(string taskId)
    {
        if (_sovereignty.IsDeleted("workflow_memory_" + taskId))
            throw new InvalidOperationException("This task memory was forgotten.");
    }

    private void EnsureOutputPathSafe(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 4096 || !Path.IsPathFullyQualified(path) ||
            path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal) ||
            path[Path.GetPathRoot(path)!.Length..].Contains(':') || path.IndexOfAny(['*', '?', '\0']) >= 0)
            throw new ArgumentException("A normal absolute output path is required.");
        var normalized = Path.GetFullPath(path);
        var root = Path.GetFullPath(_paths.Root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (normalized.Equals(root, StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The memory store cannot be captured as task output.");
        EnsureStorageSafe(normalized);
    }

    private static void EnsureStorageSafe(string path)
    {
        for (var parent = Path.GetFullPath(path); parent != null; parent = Path.GetDirectoryName(parent))
            if ((File.Exists(parent) || Directory.Exists(parent)) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Memory paths must not traverse symbolic links.");
    }

    private static void ValidateId(string id, string name)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || !id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
            throw new ArgumentException("A bounded identifier without path characters is required.", name);
    }

    private static string? Bound(string? text) => text?.Length > 2048 ? text[..2048] : text;
}
