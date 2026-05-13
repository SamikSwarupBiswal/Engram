using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for .engram workspace initialization.
/// Derived from: REQ-002, D-005, D-009, NFR-003
/// 
/// PRD Contract:
/// - "Structure: .engram/raw, wiki, runs, config, logs, archives"
/// - "Initialize a local .engram workspace with required directories"
/// - "Fresh install can create .engram with required folders"
/// - "Tests run locally without cloud credentials"
/// </summary>
public class WorkspaceInitializerTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();
    private readonly WorkspaceInitializer _sut = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void Initialize_CreatesAllRequiredDirectories()
    {
        // REQ-002: .engram workspace with raw, wiki, runs, config, logs, archives
        // D-005: All six folders must exist

        _sut.Initialize(_workspace.Paths);

        Assert.True(Directory.Exists(_workspace.Paths.Raw), "raw/ must exist");
        Assert.True(Directory.Exists(_workspace.Paths.Wiki), "wiki/ must exist");
        Assert.True(Directory.Exists(_workspace.Paths.Runs), "runs/ must exist");
        Assert.True(Directory.Exists(_workspace.Paths.Config), "config/ must exist");
        Assert.True(Directory.Exists(_workspace.Paths.Logs), "logs/ must exist");
        Assert.True(Directory.Exists(_workspace.Paths.Archives), "archives/ must exist");
    }

    [Fact]
    public void Initialize_CreatesRootDirectory()
    {
        // The .engram root must be created if it doesn't exist
        Assert.False(Directory.Exists(_workspace.Root), "Precondition: root should not exist");

        _sut.Initialize(_workspace.Paths);

        Assert.True(Directory.Exists(_workspace.Root), ".engram root must be created");
    }

    [Fact]
    public void Initialize_IsIdempotent_DoesNotThrowOnSecondCall()
    {
        // PRD: "Running it twice must not fail or rewrite unrelated files"
        // D-005: Initialization is idempotent

        _sut.Initialize(_workspace.Paths);
        var exception = Record.Exception(() => _sut.Initialize(_workspace.Paths));

        Assert.Null(exception);
    }

    [Fact]
    public void Initialize_IsIdempotent_DoesNotCorruptExistingFiles()
    {
        // Verify that re-running init preserves existing data
        _sut.Initialize(_workspace.Paths);

        // Write a file into the workspace
        var testFile = Path.Combine(_workspace.Paths.Config, "test.json");
        File.WriteAllText(testFile, "{ \"test\": true }");

        // Re-initialize
        _sut.Initialize(_workspace.Paths);

        // File must still exist with correct content
        Assert.True(File.Exists(testFile), "Existing files must be preserved");
        Assert.Equal("{ \"test\": true }", File.ReadAllText(testFile));
    }

    [Fact]
    public void IsInitialized_ReturnsFalse_ForNonExistentPath()
    {
        Assert.False(_sut.IsInitialized(_workspace.Paths));
    }

    [Fact]
    public void IsInitialized_ReturnsTrue_AfterInitialization()
    {
        _sut.Initialize(_workspace.Paths);
        Assert.True(_sut.IsInitialized(_workspace.Paths));
    }

    [Fact]
    public void IsInitialized_ReturnsFalse_WhenOnlyRootExists()
    {
        // Just creating the root is not enough
        Directory.CreateDirectory(_workspace.Root);
        Assert.False(_sut.IsInitialized(_workspace.Paths));
    }

    [Fact]
    public void WorkspacePaths_DerivesFromRoot()
    {
        // D-005: Paths must be derived from a supplied root
        var paths = new WorkspacePaths("/tmp/test_engram");

        Assert.Equal("/tmp/test_engram", paths.Root);
        Assert.Equal(Path.Combine("/tmp/test_engram", "raw"), paths.Raw);
        Assert.Equal(Path.Combine("/tmp/test_engram", "wiki"), paths.Wiki);
        Assert.Equal(Path.Combine("/tmp/test_engram", "runs"), paths.Runs);
        Assert.Equal(Path.Combine("/tmp/test_engram", "config"), paths.Config);
        Assert.Equal(Path.Combine("/tmp/test_engram", "logs"), paths.Logs);
        Assert.Equal(Path.Combine("/tmp/test_engram", "archives"), paths.Archives);
    }

    [Fact]
    public void WorkspacePaths_ThrowsOnNullRoot()
    {
        Assert.Throws<ArgumentNullException>(() => new WorkspacePaths(null!));
    }

    [Fact]
    public void GetAllRequiredPaths_ReturnsAllSevenPaths()
    {
        var paths = new WorkspacePaths("/tmp/test");
        var all = paths.GetAllRequiredPaths();

        Assert.Equal(7, all.Length);
        Assert.Contains(paths.Root, all);
        Assert.Contains(paths.Raw, all);
        Assert.Contains(paths.Wiki, all);
        Assert.Contains(paths.Runs, all);
        Assert.Contains(paths.Config, all);
        Assert.Contains(paths.Logs, all);
        Assert.Contains(paths.Archives, all);
    }
}
