using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for file integrity verification on read.
/// Derived from: D-013
///
/// Contract:
/// - Replay verifies hash by recomputing from file content
/// - Corrupted files are detected and reported
/// - Valid files pass verification
/// </summary>
public class IntegrityTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();
    private readonly ContentHasher _hasher = new();
    private readonly WorkspaceInitializer _init = new();

    public void Dispose() => _workspace.Dispose();

    private (RawEventWriter writer, ReplayEnumerator replay) CreatePair()
    {
        _init.Initialize(_workspace.Paths);
        return (new RawEventWriter(_workspace.Paths, _hasher),
                new ReplayEnumerator(_workspace.Paths));
    }

    [Fact]
    public void Replay_VerifiesIntegrity_ValidFilesPass()
    {
        var (writer, replay) = CreatePair();
        writer.Write(TestEvents.Create(text: "valid event"));

        var result = replay.EnumerateWithIntegrityCheck();

        Assert.Single(result.ValidEvents);
        Assert.Empty(result.CorruptedEvents);
    }

    [Fact]
    public void Replay_DetectsCorruptedFile()
    {
        var (writer, replay) = CreatePair();
        var writeResult = writer.Write(TestEvents.Create(text: "will be corrupted"));

        // Corrupt the file by modifying content
        File.WriteAllText(writeResult.FilePath, "{ \"corrupted\": true }");

        var result = replay.EnumerateWithIntegrityCheck();

        Assert.Empty(result.ValidEvents);
        Assert.Single(result.CorruptedEvents);
    }

    [Fact]
    public void Replay_CorruptedFile_DoesNotStopEnumeration()
    {
        var (writer, replay) = CreatePair();
        var good = writer.Write(TestEvents.Create(text: "good event"));
        var bad = writer.Write(TestEvents.Create(text: "bad event"));

        // Corrupt only the second file
        File.WriteAllText(bad.FilePath, "CORRUPTED");

        var result = replay.EnumerateWithIntegrityCheck();

        Assert.Single(result.ValidEvents);
        Assert.Single(result.CorruptedEvents);
        Assert.Equal("good event", result.ValidEvents[0].Text);
    }

    [Fact]
    public void Replay_IntegrityCheck_IncludesCorruptedFilePaths()
    {
        var (writer, replay) = CreatePair();
        var bad = writer.Write(TestEvents.Create(text: "bad"));
        File.WriteAllText(bad.FilePath, "BROKEN");

        var result = replay.EnumerateWithIntegrityCheck();

        Assert.Single(result.CorruptedEvents);
        Assert.Equal(bad.FilePath, result.CorruptedEvents[0].FilePath);
    }
}
