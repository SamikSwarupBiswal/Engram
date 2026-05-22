using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Engram.Store.Automation;
using Xunit;

namespace Engram.Store.Tests;

public class FailureArchaeologyTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose()
    {
        _workspace.Dispose();
    }

    [Fact]
    public async Task RecordFailureAsync_SavesFailureToFile()
    {
        var store = new FailureArchaeologyStore(_workspace.Root);
        var record = new FailureRecord
        {
            WorkflowId = "wf1",
            Goal = "Goal 1",
            FailedStepId = "s1",
            StepDescription = "Step 1",
            ErrorMessage = "Timeout error",
            ErrorType = FailureErrorType.Timeout
        };

        await store.RecordFailureAsync(record);

        var list = await store.GetFailuresAsync();
        Assert.Single(list);
        Assert.Equal("wf1", list[0].WorkflowId);
        Assert.Equal("Timeout error", list[0].ErrorMessage);
    }

    [Fact]
    public async Task PruneAsync_DeletesOldRawLogsButKeepsRecent()
    {
        var store = new FailureArchaeologyStore(_workspace.Root);

        // Recent failure
        var recent = new FailureRecord
        {
            FailureId = "recent1",
            WorkflowId = "wf1",
            RecordedAt = DateTimeOffset.UtcNow
        };
        await store.RecordFailureAsync(recent);

        // Old failure (95 days ago)
        var old = new FailureRecord
        {
            FailureId = "old1",
            WorkflowId = "wf1",
            RecordedAt = DateTimeOffset.UtcNow - TimeSpan.FromDays(95)
        };
        await store.RecordFailureAsync(old);

        var failuresBefore = await store.GetFailuresAsync();
        Assert.Equal(2, failuresBefore.Count);

        // Prune older than 90 days
        await store.PruneAsync(TimeSpan.FromDays(90));

        var failuresAfter = await store.GetFailuresAsync();
        Assert.Single(failuresAfter);
        Assert.Equal("recent1", failuresAfter[0].FailureId);
    }

    [Fact]
    public async Task DetectPatternsAsync_ConsolidatesFailuresAndKeepsLessonsIndefinitely()
    {
        var store = new FailureArchaeologyStore(_workspace.Root);

        // Add 3 failures of the same step and error
        for (int i = 0; i < 3; i++)
        {
            await store.RecordFailureAsync(new FailureRecord
            {
                FailureId = $"fail_{i}",
                WorkflowId = "wf1",
                FailedStepId = "git_push",
                ErrorMessage = "Permission denied",
                ErrorType = FailureErrorType.PermissionDenied,
                EnvironmentSnapshot = new Dictionary<string, string> { ["os"] = "windows" },
                RecordedAt = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(2)
            });
        }

        var patterns = await store.DetectPatternsAsync();
        Assert.Single(patterns);
        Assert.Equal(FailureErrorType.PermissionDenied, patterns[0].FailureType);
        Assert.Equal(3, patterns[0].Occurrences);
        Assert.Contains("Verify execution constraints", patterns[0].SuggestedMitigation);

        // Verify lessons.json is persisted
        var lessonsPath = Path.Combine(_workspace.Root, "automation", "failures", "lessons.json");
        Assert.True(File.Exists(lessonsPath));

        // Now prune all failures (cutoff 1 second ago)
        await store.PruneAsync(TimeSpan.FromSeconds(1));
        var rawFailures = await store.GetFailuresAsync();
        Assert.Empty(rawFailures);

        // Re-detect or load lessons. The pattern should STILL exist because it's kept indefinitely in lessons.json
        var secondStoreInstance = new FailureArchaeologyStore(_workspace.Root);
        var activePatterns = await secondStoreInstance.DetectPatternsAsync();
        Assert.Single(activePatterns);
        Assert.Equal(3, activePatterns[0].Occurrences);
    }
}
