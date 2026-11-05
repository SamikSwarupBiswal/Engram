using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Engram.Store.Automation;
using AutomationContext = Engram.Store.Automation.ExecutionContext;

namespace Engram.Store.Tests;

public sealed class GenericFileActionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "engram-file-actions-" + Guid.NewGuid().ToString("N"));
    private readonly DefaultUiEmbodimentProvider _provider = new(new AutomationContext());

    public GenericFileActionTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task AutomaticallyScopedNewFile_PreservesExactUtf8Content()
    {
        var path = Path.Combine(_root, "new.txt");
        const string content = "  Exact বাংলা text\r\nsecond line\n";
        var action = Automatic(ActionType.FileWrite, path, content);

        await _provider.ExecuteActionAsync(action);

        Assert.Equal(new UTF8Encoding(false).GetBytes(content), await File.ReadAllBytesAsync(path));
        Assert.Equal(ActionStatus.Completed, action.Status);
        Assert.Equal(new[] { path }, Directory.GetFiles(_root));
    }

    [Fact]
    public async Task FileAppearingAfterAutomaticScope_IsNeverOverwritten()
    {
        var path = Path.Combine(_root, "collision.txt");
        var action = Automatic(ActionType.FileWrite, path, "planned content");
        await File.WriteAllTextAsync(path, "concurrent content");
        var original = await File.ReadAllBytesAsync(path);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _provider.ExecuteActionAsync(action));

        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Equal(new[] { path }, Directory.GetFiles(_root));
    }

    [Fact]
    public async Task ApprovedOverwrite_PreservesOriginalBackupAndVerifiesReplacement()
    {
        var path = Path.Combine(_root, "existing.txt");
        var original = new UTF8Encoding(false).GetBytes("original content\r\n");
        await File.WriteAllBytesAsync(path, original);
        var action = new AutomationAction
        {
            Type = ActionType.FileWrite, Value = path, Target = new() { Text = "replacement\n" },
            RiskLevel = AutomationRiskLevel.High, Permission = ActionPermission.Approved
        };
        string? backup = null;
        try
        {
            var result = await _provider.ExecuteActionAsync(action);
            var match = Regex.Match(result, "backup '(?<path>[^']+)'");
            Assert.True(match.Success);
            backup = match.Groups["path"].Value;
            Assert.Equal(original, await File.ReadAllBytesAsync(backup));
            Assert.Equal("replacement\n", await File.ReadAllTextAsync(path));
            Assert.Contains(Convert.ToHexString(SHA256.HashData(original)), result);
            Assert.Contains("exact content verified", result);
            Assert.Equal(new[] { path }, Directory.GetFiles(_root));
        }
        finally
        {
            if (backup != null) Directory.Delete(Path.GetDirectoryName(backup)!, recursive: true);
        }
    }

    [Theory]
    [InlineData(ActionType.FileWrite)]
    [InlineData(ActionType.DeleteFile)]
    public async Task PendingConsequentialFileAction_LeavesOriginalUntouched(ActionType type)
    {
        var path = Path.Combine(_root, "protected.txt");
        await File.WriteAllTextAsync(path, "keep original");
        var action = new AutomationAction
        {
            Type = type, Value = path, Target = new() { Text = "replacement" },
            RiskLevel = AutomationRiskLevel.High, Permission = ActionPermission.Pending
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _provider.ExecuteActionAsync(action));

        Assert.Equal("keep original", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task AutomaticallyScopedMove_PreservesBytesWithoutOverwritingDestination()
    {
        var source = Path.Combine(_root, "source.bin");
        var destination = Path.Combine(_root, "destination.bin");
        var bytes = new byte[] { 0, 255, 42, 128, 13, 10 };
        await File.WriteAllBytesAsync(source, bytes);

        await _provider.ExecuteActionAsync(Automatic(ActionType.MoveFile, source, destination));

        Assert.False(File.Exists(source));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(destination));
        await File.WriteAllTextAsync(source, "another source");
        await Assert.ThrowsAsync<IOException>(() =>
            _provider.ExecuteActionAsync(Automatic(ActionType.MoveFile, source, destination)));
        Assert.Equal("another source", await File.ReadAllTextAsync(source));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task CancelledCreation_LeavesNoOutputOrTemporaryFile()
    {
        var path = Path.Combine(_root, "cancelled.txt");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _provider.ExecuteActionAsync(Automatic(ActionType.FileWrite, path, "never publish"), cancellation.Token));

        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task ChangedAutomaticScope_IsRejectedBeforeCreatingAnyFile()
    {
        var action = Automatic(ActionType.FileWrite, Path.Combine(_root, "selected.txt"), "selected content");
        action.Value = Path.Combine(_root, "different.txt");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _provider.ExecuteActionAsync(action));

        Assert.Empty(Directory.GetFiles(_root));
    }

    [Theory]
    [InlineData(ActionType.MoveFile)]
    [InlineData(ActionType.DeleteFile)]
    public async Task CancelledMoveOrDeletion_PreservesOriginal(ActionType type)
    {
        var source = Path.Combine(_root, "preserve.txt");
        var destination = Path.Combine(_root, "destination.txt");
        await File.WriteAllTextAsync(source, "original");
        var action = new AutomationAction
        {
            Type = type, Value = source, Target = new() { Text = destination },
            RiskLevel = AutomationRiskLevel.High, Permission = ActionPermission.Approved
        };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _provider.ExecuteActionAsync(action, cancellation.Token));

        Assert.Equal("original", await File.ReadAllTextAsync(source));
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task OverwriteFailingBeforeReplacement_DoesNotRewriteOriginal()
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + new string('x', 188) + ".txt");
        await File.WriteAllTextAsync(path, "unchanged original");
        var writtenAt = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, writtenAt);
        var original = await File.ReadAllBytesAsync(path);
        var action = new AutomationAction
        {
            Type = ActionType.FileWrite, Value = path, Target = new() { Text = "replacement" },
            RiskLevel = AutomationRiskLevel.High, Permission = ActionPermission.Approved
        };

        try
        {
            using var readOnlyLease = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var error = await Assert.ThrowsAnyAsync<IOException>(() => _provider.ExecuteActionAsync(action));

            Assert.Contains(".engram-", error.Message);
            Assert.Equal(original, await File.ReadAllBytesAsync(path));
            Assert.Equal(writtenAt, File.GetLastWriteTimeUtc(path));
            Assert.Equal(new[] { path }, Directory.GetFiles(_root));
        }
        finally
        {
            var backups = Path.Combine(Path.GetTempPath(), "Engram", "Backups");
            if (Directory.Exists(backups))
                foreach (var backup in Directory.GetFiles(backups, Path.GetFileName(path) + ".original", SearchOption.AllDirectories))
                    Directory.Delete(Path.GetDirectoryName(backup)!, recursive: true);
        }
    }

    [Fact]
    public async Task PreCancelledApprovedOverwrite_DoesNotTouchOriginalOrCreateTemporaryFile()
    {
        var path = Path.Combine(_root, $"cancelled-overwrite-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "unchanged original");
        var writtenAt = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, writtenAt);
        var original = await File.ReadAllBytesAsync(path);
        var action = new AutomationAction
        {
            Type = ActionType.FileWrite, Value = path, Target = new() { Text = "replacement" },
            RiskLevel = AutomationRiskLevel.High, Permission = ActionPermission.Approved
        };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _provider.ExecuteActionAsync(action, cancellation.Token));

        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Equal(writtenAt, File.GetLastWriteTimeUtc(path));
        Assert.Equal(new[] { path }, Directory.GetFiles(_root));
        var backups = Path.Combine(Path.GetTempPath(), "Engram", "Backups");
        if (Directory.Exists(backups))
            Assert.Empty(Directory.GetFiles(backups, Path.GetFileName(path) + ".original", SearchOption.AllDirectories));
    }

    private static AutomationAction Automatic(ActionType type, string path, string contentOrDestination)
    {
        var action = new AutomationAction
        {
            Type = type, Value = path, Target = new() { Text = contentOrDestination },
            Permission = ActionPermission.AutoApproved
        };
        AutomationApprovalPolicy.GrantAutomaticExecution(action);
        return action;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
