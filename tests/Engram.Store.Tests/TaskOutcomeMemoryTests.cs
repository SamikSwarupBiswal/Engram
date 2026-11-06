using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Engram.Store.Automation;
using Engram.Store.Automation.Skills;
using Engram.Store.Governance;
using Engram.Store.Inference;
using Engram.Store.Memory;
using Engram.Store.Wiki;

namespace Engram.Store.Tests;

public sealed class TaskOutcomeMemoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "engram-task-memory-" + Guid.NewGuid().ToString("N"));
    private readonly WorkspacePaths _paths;
    private readonly WikiNodeStore _nodes;
    private readonly RawEventWriter _writer;
    private readonly MemorySovereigntySystem _sovereignty;
    private readonly TaskOutcomeMemory _memory;
    private const string Conversation = "conversation-a";
    private static readonly string ScopeA = "cloud-v1-" + new string('A', 64);
    private static readonly string ScopeB = "cloud-v1-" + new string('B', 64);

    public TaskOutcomeMemoryTests()
    {
        Directory.CreateDirectory(_root);
        _paths = new(Path.Combine(_root, "memory"));
        _nodes = new(_paths);
        _writer = new(_paths, new ContentHasher());
        _sovereignty = new(_nodes, _paths);
        _memory = NewMemory();
    }

    private TaskOutcomeMemory NewMemory(FileIdentityRegistry? registry = null) =>
        new(_paths, _writer, _nodes, _sovereignty, registry);

    private AutomationTaskRecord TaskRecord(string content = "Welcome ${name}! বাংলা\r\n", bool generated = true)
    {
        var id = Guid.NewGuid().ToString("N")[..12];
        var action = new AutomationAction
        {
            Type = ActionType.FileWrite, Value = "desktop/" + id + ".txt", Target = new() { Text = content }
        };
        TextFileOutputBinding.Bind(action, _ => _root);
        if (!generated) action.TextFileOutput = null;
        File.WriteAllText(action.Value!, content, new UTF8Encoding(false));
        action.Status = Engram.Store.Automation.ActionStatus.Completed;
        var plan = new ExecutionPlan
        {
            PlanId = id, Goal = "Write a welcome note.", Planning = new("actual-model", "actual-provider", "desktop.plan", false) { DisclosureIdentity = ScopeA },
            Steps = new() { ["save"] = new() { Id = "save", Action = action, Status = StepStatus.Completed, CompletedAt = DateTimeOffset.UtcNow } }
        };
        return new() { TaskId = id, Goal = plan.Goal, Plan = plan, State = AutomationTaskState.Completed, CompletedAt = DateTimeOffset.UtcNow };
    }

    [Fact]
    public void VerifiedGeneratedOutputSurvivesRestartWithExactSnapshotAndProvenance()
    {
        var task = TaskRecord();
        var recorded = _memory.Record(task, Conversation);
        var restored = NewMemory().GetForConversation(Conversation, [task.TaskId]).Single();
        Assert.Equal(task.TaskId, restored.TaskId);
        Assert.Equal("Completed", restored.State);
        Assert.Equal("actual-model", restored.Model);
        Assert.Equal("actual-provider", restored.Provider);
        Assert.Equal(ScopeA, restored.DisclosureIdentity);
        Assert.Equal("desktop.plan", restored.Tool);
        Assert.False(restored.FromCache);
        var artifact = Assert.Single(restored.Artifacts);
        Assert.Equal(task.Plan.Steps["save"].Action.Target!.Text, artifact.HistoricalText);
        Assert.Equal("Unchanged", artifact.CurrentState);
        Assert.Equal(recorded.SourceEventId, restored.SourceEventId);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(artifact.Path))), artifact.Sha256);
        Assert.DoesNotContain("Welcome", _nodes.Load("workflow_memory_" + task.TaskId)!.Summary);
        var rawPath = Directory.GetFiles(_paths.Raw, "task_outcome_*.json", SearchOption.AllDirectories).Single();
        using var raw = JsonDocument.Parse(File.ReadAllText(rawPath));
        Assert.Equal("private", raw.RootElement.GetProperty("privacy_class").GetString());
    }

    [Fact]
    public void ChangedAndMissingFilesNeverReplaceHistoricalSnapshotWithNewContent()
    {
        var task = TaskRecord("original generated text");
        _memory.Record(task, Conversation);
        var path = task.Plan.Steps["save"].Action.Value!;
        File.WriteAllText(path, "new private text that must not be captured");
        var changed = _memory.GetForConversation(Conversation, [task.TaskId]).Single().Artifacts.Single();
        Assert.Equal("Changed", changed.CurrentState);
        Assert.Equal("original generated text", changed.HistoricalText);
        Assert.NotEqual(changed.Sha256, changed.CurrentSha256);
        Assert.DoesNotContain("new private", JsonSerializer.Serialize(changed));
        File.Delete(path);
        var missing = _memory.GetForConversation(Conversation, [task.TaskId]).Single().Artifacts.Single();
        Assert.Equal("Missing", missing.CurrentState);
        Assert.Equal("original generated text", missing.HistoricalText);
    }

    [Theory]
    [InlineData(StepStatus.Pending, Engram.Store.Automation.ActionStatus.Pending)]
    [InlineData(StepStatus.Failed, Engram.Store.Automation.ActionStatus.Failed)]
    [InlineData(StepStatus.Skipped, Engram.Store.Automation.ActionStatus.Pending)]
    [InlineData(StepStatus.Completed, Engram.Store.Automation.ActionStatus.Pending)]
    public void ProposedAndUnverifiedStepsCannotProduceVerifiedArtifacts(StepStatus stepStatus, Engram.Store.Automation.ActionStatus actionStatus)
    {
        var task = TaskRecord();
        task.State = AutomationTaskState.Failed;
        task.Plan.Steps["save"].Status = stepStatus;
        task.Plan.Steps["save"].Action.Status = actionStatus;
        Assert.Empty(_memory.Record(task, Conversation).Artifacts);
    }

    [Fact]
    public void FailedTaskRetainsCompletedVerifiedStepWithoutClaimingOverallSuccess()
    {
        var task = TaskRecord();
        task.State = AutomationTaskState.Failed;
        task.Error = "A later step failed.";
        var recorded = _memory.Record(task, Conversation);
        Assert.Equal("Failed", recorded.State);
        Assert.Equal(task.Error, recorded.Error);
        Assert.Single(recorded.Artifacts);
    }

    [Fact]
    public void RawFileWriteDoesNotBecomeGeneratedReplayContext()
    {
        var task = TaskRecord(generated: false);
        Assert.Empty(_memory.Record(task, Conversation).Artifacts);
    }

    [Fact]
    public void ChangedBeforeRecordingDoesNotCapturePlannedContentAsVerified()
    {
        var task = TaskRecord();
        File.WriteAllText(task.Plan.Steps["save"].Action.Value!, "somebody else changed it");
        Assert.Empty(_memory.Record(task, Conversation).Artifacts);
    }

    [Fact]
    public void AnotherConversationCannotReadOrTakeOwnershipOfTask()
    {
        var task = TaskRecord();
        _memory.Record(task, Conversation);
        Assert.Empty(_memory.GetForConversation("another-conversation", [task.TaskId]));
        Assert.Throws<InvalidOperationException>(() => _memory.Record(task, "another-conversation"));
    }

    [Fact]
    public void ForgotWorkflowCannotBeReadOrRecreatedFromLateCompletion()
    {
        var task = TaskRecord();
        _memory.Record(task, Conversation);
        _sovereignty.Forget("workflow_memory_" + task.TaskId);
        Assert.Empty(NewMemory().GetForConversation(Conversation, [task.TaskId]));
        Assert.Throws<InvalidOperationException>(() => NewMemory().Record(task, Conversation));
    }

    [Fact]
    public void CorruptSourceCannotBeUsedAsContext()
    {
        var task = TaskRecord();
        _memory.Record(task, Conversation);
        var rawPath = Directory.GetFiles(_paths.Raw, "task_outcome_*.json", SearchOption.AllDirectories).Single();
        File.WriteAllText(rawPath, File.ReadAllText(rawPath).Replace("actual-provider", "forged-provider"));
        Assert.Throws<InvalidDataException>(() => NewMemory().GetForConversation(Conversation, [task.TaskId]));
    }

    [Fact]
    public void RegistryDenialDoesNotGrantWatchingRightsOrDiscardVerifiedSnapshot()
    {
        var registry = new FileIdentityRegistry(_paths, _writer, _nodes, _sovereignty);
        var task = TaskRecord();
        var artifact = Assert.Single(NewMemory(registry).Record(task, Conversation).Artifacts);
        Assert.Null(artifact.FileId);
        Assert.Equal("Unavailable", artifact.RegistryState);
        Assert.NotEmpty(artifact.HistoricalText);
        Assert.Empty(registry.FindCandidates("txt"));
    }

    [Fact]
    public void RepeatedRecordingKeepsImmutableSourcesAndOriginalVerification()
    {
        var task = TaskRecord("first verified text");
        var first = _memory.Record(task, Conversation);
        File.WriteAllText(task.Plan.Steps["save"].Action.Value!, "private later change");
        task.State = AutomationTaskState.Failed;
        task.Error = "Later step failed.";
        var second = _memory.Record(task, Conversation);
        Assert.NotEqual(first.SourceEventId, second.SourceEventId);
        Assert.Equal(2, Directory.GetFiles(_paths.Raw, "task_outcome_*.json", SearchOption.AllDirectories).Length);
        var current = NewMemory().GetForConversation(Conversation, [task.TaskId]).Single();
        Assert.Equal("Failed", current.State);
        var artifact = Assert.Single(current.Artifacts);
        Assert.Equal("first verified text", artifact.HistoricalText);
        Assert.Equal("Changed", artifact.CurrentState);
        Assert.Equal(first.Artifacts.Single().VerifiedAt, artifact.VerifiedAt);
    }

    [Fact]
    public void ForgettingSharedFileIdentityAlsoSuppressesOutcomeReplay()
    {
        var registry = new FileIdentityRegistry(_paths, _writer, _nodes, _sovereignty, _ => true);
        var memory = NewMemory(registry);
        var task = TaskRecord();
        var recorded = memory.Record(task, Conversation);
        var fileId = Assert.Single(recorded.Artifacts).FileId;
        Assert.NotNull(fileId);
        _sovereignty.Forget(fileId);
        Assert.Empty(memory.GetForConversation(Conversation, [task.TaskId]).Single().Artifacts);
        Assert.Empty(memory.Record(task, Conversation).Artifacts);
        Assert.Empty(NewMemory(registry).Record(task, Conversation).Artifacts);
    }

    [Fact]
    public void FileForgottenBeforeFirstOutcomeCannotBecomeAnUnregisteredArtifact()
    {
        var registry = new FileIdentityRegistry(_paths, _writer, _nodes, _sovereignty, _ => true);
        var task = TaskRecord();
        var observed = registry.Observe(new(task.Plan.Steps["save"].Action.Value!, "created", DateTimeOffset.UtcNow));
        _sovereignty.Forget(observed.FileId);
        var memory = NewMemory(registry);
        Assert.Empty(memory.Record(task, Conversation).Artifacts);
        Assert.Empty(memory.GetForConversation(Conversation, [task.TaskId]).Single().Artifacts);
        Assert.Empty(NewMemory(registry).Record(task, Conversation).Artifacts);
    }

    [Fact]
    public void FileForgottenAfterUnregisteredSnapshotAlsoSuppressesReplay()
    {
        var consent = false;
        var registry = new FileIdentityRegistry(_paths, _writer, _nodes, _sovereignty, _ => consent);
        var task = TaskRecord();
        var memory = NewMemory(registry);
        Assert.Null(memory.Record(task, Conversation).Artifacts.Single().FileId);
        consent = true;
        var observed = registry.Observe(new(task.Plan.Steps["save"].Action.Value!, "created", DateTimeOffset.UtcNow));
        _sovereignty.Forget(observed.FileId);
        consent = false;
        Assert.Empty(memory.GetForConversation(Conversation, [task.TaskId]).Single().Artifacts);
        Assert.Empty(memory.Record(task, Conversation).Artifacts);
    }

    [Fact]
    public void MissingModelProvenanceCannotProduceGeneratedReplayContext()
    {
        var task = TaskRecord();
        task.Plan.Planning = null;
        Assert.Empty(_memory.Record(task, Conversation).Artifacts);
    }

    [Fact]
    public void VerificationTimeDescribesReadbackRatherThanAnOlderStepTimestamp()
    {
        var task = TaskRecord();
        task.Plan.Steps["save"].CompletedAt = DateTimeOffset.UtcNow.AddDays(-1);
        var beforeReadback = DateTimeOffset.UtcNow;
        var artifact = _memory.Record(task, Conversation).Artifacts.Single();
        Assert.InRange(artifact.VerifiedAt, beforeReadback, DateTimeOffset.UtcNow);
    }

    [Fact]
    public void OversizeCurrentFileIsUnavailableWithoutReplacingSnapshot()
    {
        var task = TaskRecord("tiny generated text");
        _memory.Record(task, Conversation);
        using (var file = File.OpenWrite(task.Plan.Steps["save"].Action.Value!))
            file.SetLength(FileIdentityRegistry.MaxHashBytes + 1);
        var artifact = _memory.GetForConversation(Conversation, [task.TaskId]).Single().Artifacts.Single();
        Assert.Equal("Unavailable", artifact.CurrentState);
        Assert.Null(artifact.CurrentSha256);
        Assert.Equal("tiny generated text", artifact.HistoricalText);
    }

    [Fact]
    public void ReplacedOutputLinkCannotBeRevalidatedThroughTarget()
    {
        var task = TaskRecord("verified before replacement");
        _memory.Record(task, Conversation);
        var path = task.Plan.Steps["save"].Action.Value!;
        var privateFile = Path.Combine(_root, "other.txt");
        File.WriteAllText(privateFile, "private unrelated content");
        File.Delete(path);
        File.CreateSymbolicLink(path, privateFile);
        try
        {
            var artifact = _memory.GetForConversation(Conversation, [task.TaskId]).Single().Artifacts.Single();
            Assert.Equal("Unavailable", artifact.CurrentState);
            Assert.Null(artifact.CurrentSha256);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void OutcomeIndexCannotFollowAnExternalDirectoryLink()
    {
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(_paths.Runs);
        var link = Path.Combine(_paths.Runs, "task-outcomes");
        Directory.CreateSymbolicLink(link, outside);
        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => _memory.Record(TaskRecord(), Conversation));
            Assert.Empty(Directory.GetFiles(outside));
        }
        finally { Directory.Delete(link); }
    }

    [Fact]
    public void FailedPersistenceDoesNotRelabelExecutedTask()
    {
        Directory.CreateDirectory(_paths.Runs);
        File.WriteAllText(Path.Combine(_paths.Runs, "task-outcomes"), "index is unavailable");
        var task = TaskRecord();
        Assert.Throws<IOException>(() => _memory.Record(task, Conversation));
        Assert.Equal(AutomationTaskState.Completed, task.State);
    }

    [Fact]
    public void ConversationReplyScopePersistsWithoutStoringConversationText()
    {
        const string reply = "Exact previous assistant reply.";
        _memory.RecordConversationReply(Conversation, ScopeA, reply);
        _memory.RecordConversationReply(Conversation, ScopeA, reply);
        var scoped = NewMemory().ScopeConversation(Conversation, [new() { Role = "assistant", Content = reply }]);
        Assert.Equal(ScopeA, Assert.Single(scoped).MemoryProvider);
        var file = Path.Combine(_paths.Runs, "conversation-scopes", Conversation + ".json");
        using var scope = JsonDocument.Parse(File.ReadAllText(file));
        Assert.Equal(1, scope.RootElement.GetProperty("ReplyProviders").EnumerateObject().Count());
        Assert.Equal(2, scope.RootElement.EnumerateObject().Count());
        Assert.DoesNotContain(reply, File.ReadAllText(file));
    }

    [Fact]
    public void LegacyFriendlyProviderNamesNeverBecomeDisclosureIdentities()
    {
        const string reply = "A reply without endpoint or account provenance.";
        var file = Path.Combine(_paths.Runs, "conversation-scopes", Conversation + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(reply)));
        File.WriteAllText(file, JsonSerializer.Serialize(new
        {
            ConversationId = Conversation, ReplyProviders = new Dictionary<string, string> { [hash] = "actual-provider" }
        }));
        Assert.Equal("local", NewMemory().ScopeConversation(Conversation,
            [new() { Role = "assistant", Content = reply }]).Single().MemoryProvider);
        _memory.RecordConversationReply(Conversation, ScopeA, reply);
        Assert.Equal("local", NewMemory().ScopeConversation(Conversation,
            [new() { Role = "assistant", Content = reply }]).Single().MemoryProvider);
        _memory.RecordConversationReply(Conversation, "unknown-provider", "Another unproven reply.");
        Assert.Equal("local", NewMemory().ScopeConversation(Conversation,
            [new() { Role = "assistant", Content = "Another unproven reply." }]).Single().MemoryProvider);
    }

    [Fact]
    public void UnknownRepliesRemainLocalAfterAProviderAnswersLaterInSameChat()
    {
        var user = new ChatMessage { Content = "A current user request." };
        var unknown = new ChatMessage { Role = "assistant", Content = "Legacy private reply.", MemoryProvider = "forged-provider" };
        _memory.RecordConversationReply(Conversation, ScopeB, "A new answer.");
        var scoped = NewMemory().ScopeConversation(Conversation, [unknown, user, new() { Role = "assistant", Content = "A new answer." }]);
        Assert.Equal("local", scoped[0].MemoryProvider);
        Assert.Same(user, scoped[1]);
        Assert.Equal(ScopeB, scoped[2].MemoryProvider);
        Assert.Equal("local", NewMemory().ScopeConversation("another-conversation", [scoped[2]]).Single().MemoryProvider);
    }

    [Fact]
    public void ForgettingTaskDoesNotEraseConversationProviderBoundary()
    {
        var task = TaskRecord();
        _memory.RecordConversationReply(Conversation, ScopeA, "A response quoting generated output.");
        _memory.Record(task, Conversation);
        _sovereignty.Forget("workflow_memory_" + task.TaskId);
        Assert.Empty(NewMemory().GetForConversation(Conversation, [task.TaskId]));
        Assert.Equal(ScopeA, NewMemory().ScopeConversation(Conversation,
            [new() { Role = "assistant", Content = "A response quoting generated output." }]).Single().MemoryProvider);
    }

    [Fact]
    public void SameReplySeenFromMultipleProvidersCannotBeAttributedToEitherProvider()
    {
        const string reply = "An identical answer generated by two providers.";
        _memory.RecordConversationReply(Conversation, ScopeA, reply);
        _memory.RecordConversationReply(Conversation, ScopeB, reply);
        _memory.RecordConversationReply(Conversation, ScopeA, reply);
        Assert.Equal("local", NewMemory().ScopeConversation(Conversation,
            [new() { Role = "assistant", Content = reply }]).Single().MemoryProvider);
    }

    [Fact]
    public void PrunedReplyProvenanceBecomesUnknownRatherThanInheritingNewestProvider()
    {
        _memory.RecordConversationReply(Conversation, ScopeA, "Old reply.");
        for (var index = 0; index < 200; index++)
            _memory.RecordConversationReply(Conversation, ScopeB, "Later reply " + index);
        var scoped = NewMemory().ScopeConversation(Conversation,
            [new() { Role = "assistant", Content = "Old reply." }, new() { Role = "assistant", Content = "Later reply 199" }]);
        Assert.Equal("local", scoped[0].MemoryProvider);
        Assert.Equal(ScopeB, scoped[1].MemoryProvider);
    }

    [Fact]
    public void ReplyProviderScopeCannotFollowAnExternalDirectoryLink()
    {
        var outside = Path.Combine(_root, "outside-scopes");
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(_paths.Runs);
        var link = Path.Combine(_paths.Runs, "conversation-scopes");
        Directory.CreateSymbolicLink(link, outside);
        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => _memory.RecordConversationReply(Conversation, ScopeA, "A reply."));
            Assert.Throws<UnauthorizedAccessException>(() => _memory.ScopeConversation(Conversation, []));
            Assert.Empty(Directory.GetFiles(outside));
        }
        finally { Directory.Delete(link); }
    }

    [Fact]
    public void CorruptConversationProviderScopeFailsClosed()
    {
        _memory.RecordConversationReply(Conversation, ScopeA, "A scoped answer.");
        var file = Path.Combine(_paths.Runs, "conversation-scopes", Conversation + ".json");
        File.WriteAllText(file, "{\"ConversationId\":\"another-chat\",\"ReplyProviders\":{}}");
        Assert.Throws<InvalidDataException>(() => NewMemory().ScopeConversation(Conversation, []));
        Assert.Throws<InvalidDataException>(() => NewMemory().RecordConversationReply(Conversation, ScopeB, "Another answer."));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("a/b")]
    public void ConversationScopeIdentifiersCannotEscapeStorage(string conversationId)
    {
        Assert.Throws<ArgumentException>(() => _memory.ScopeConversation(conversationId, []));
        Assert.Throws<ArgumentException>(() => _memory.RecordConversationReply(conversationId, ScopeA, "A scoped answer."));
    }

    [Fact]
    public void DeleteAllRejectsLateOutcomeAndReplyWritesFromAnEarlierGeneration()
    {
        var task = TaskRecord();
        var generation = _memory.CaptureGeneration;
        _memory.Record(task, Conversation, generation);
        _memory.RecordConversationReply(Conversation, ScopeA, "Before deletion.", generation);
        _memory.ResetForDeletion(() =>
            Assert.True(new Engram.Store.Security.DataDelete(_paths.Root).DeleteAll().Success));
        Assert.Throws<InvalidOperationException>(() => _memory.Record(task, Conversation, generation));
        Assert.Throws<InvalidOperationException>(() => _memory.RecordConversationReply(Conversation, ScopeA, "Late response.", generation));
        var draftWritten = false;
        Assert.Throws<InvalidOperationException>(() => _memory.RunIfCurrentGeneration(generation, () => draftWritten = true));
        Assert.False(draftWritten);
        Assert.False(Directory.Exists(_paths.Raw));
        Assert.False(Directory.Exists(_paths.Runs));

        var fresh = TaskRecord("Explicit new work after deletion.");
        _memory.Record(fresh, Conversation, _memory.CaptureGeneration);
        _memory.RecordConversationReply(Conversation, ScopeA, "New response.", _memory.CaptureGeneration);
        _memory.RunIfCurrentGeneration(_memory.CaptureGeneration, () => draftWritten = true);
        Assert.True(draftWritten);
        Assert.Single(_memory.GetForConversation(Conversation, [fresh.TaskId]));
        Assert.Equal(ScopeA, _memory.ScopeConversation(Conversation,
            [new() { Role = "assistant", Content = "New response." }]).Single().MemoryProvider);
    }

    [Fact]
    public void FailedDeletionStillInvalidatesEarlierWork()
    {
        var generation = _memory.CaptureGeneration;
        Assert.Throws<IOException>(() => _memory.ResetForDeletion(() => throw new IOException("Deletion failed.")));
        Assert.NotEqual(generation, _memory.CaptureGeneration);
        Assert.Throws<InvalidOperationException>(() => _memory.Record(TaskRecord(), Conversation, generation));
    }

    [Fact]
    public async Task DeletionSerializesWithALateWriterBeforeClearingStorage()
    {
        var generation = _memory.CaptureGeneration;
        var task = TaskRecord();
        using var deleting = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var deletion = Task.Run(() => _memory.ResetForDeletion(() =>
        {
            deleting.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(new Engram.Store.Security.DataDelete(_paths.Root).DeleteAll().Success);
        }));
        Assert.True(deleting.Wait(TimeSpan.FromSeconds(5)));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = Task.Run(() =>
        {
            started.SetResult();
            return _memory.Record(task, Conversation, generation);
        });
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(writer.IsCompleted);
        }
        finally { release.Set(); }
        await deletion;
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer);
        Assert.False(Directory.Exists(_paths.Raw));
        Assert.False(Directory.Exists(_paths.Runs));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("a/b")]
    [InlineData("C:\\private")]
    public void SelectedTaskIdsCannotEscapeIndexDirectory(string id)
    {
        Assert.Throws<ArgumentException>(() => _memory.GetForConversation(Conversation, [id]));
    }

    public void Dispose()
    {
        _writer.Dispose();
        _nodes.Dispose();
        Directory.Delete(_root, true);
    }
}
