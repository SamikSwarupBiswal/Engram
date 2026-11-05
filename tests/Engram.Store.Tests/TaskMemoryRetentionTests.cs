using System.IO.Compression;
using System.Text;
using System.Runtime.InteropServices;
using Engram.Store.Security;

namespace Engram.Store.Tests;

public sealed class TaskMemoryRetentionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "engram-memory-retention-" + Guid.NewGuid().ToString("N"));
    private string Workspace => Path.Combine(_root, "workspace");

    public TaskMemoryRetentionTests() => Directory.CreateDirectory(Workspace);

    [Fact]
    public async Task ExportIncludesRawAndRunEvidenceWithOriginalBytesAndRelativePaths()
    {
        var files = CreateEvidence();
        var zipPath = Path.Combine(_root, "export.zip");

        var result = await new DataExport(Workspace).ExportAsync(zipPath);

        Assert.True(result.Success, result.Error);
        Assert.Equal(files.Count, result.FileCount);
        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Equal(files.Keys.Append("manifest.json").Order(), zip.Entries.Select(entry => entry.FullName).Order());
        foreach (var (relativePath, bytes) in files)
        {
            using var input = Assert.IsType<ZipArchiveEntry>(zip.GetEntry(relativePath)).Open();
            using var copy = new MemoryStream();
            await input.CopyToAsync(copy);
            Assert.Equal(bytes, copy.ToArray());
        }
    }

    [Fact]
    public async Task OwnExportRoundTripRestoresRawAndRunEvidence()
    {
        var files = CreateEvidence();
        var zipPath = Path.Combine(_root, "roundtrip.zip");
        var exported = await new DataExport(Workspace).ExportAsync(zipPath);
        Assert.True(exported.Success, exported.Error);
        var restoredRoot = Path.Combine(_root, "restored");

        var imported = await new DataExport(restoredRoot).ImportAsync(zipPath);

        Assert.True(imported.Success, imported.Error);
        foreach (var (relativePath, bytes) in files)
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(restoredRoot, relativePath)));
    }

    [Fact]
    public void ExplicitDeleteAllRemovesEvidenceAndPreservesUnrelatedAndOutsideFiles()
    {
        var files = CreateEvidence();
        var outside = Path.Combine(_root, "outside.txt");
        var unrelated = Path.Combine(Workspace, "unrelated.txt");
        File.WriteAllText(outside, "outside original");
        File.WriteAllText(unrelated, "unrelated original");

        var deleted = new DataDelete(Workspace).DeleteAll();

        Assert.True(deleted.Success, string.Join("; ", deleted.Errors));
        Assert.Equal(files.Count, deleted.FileCount);
        foreach (var directory in new[] { "events", "raw", "wiki", "runs", "config" })
        {
            Assert.Contains(directory, deleted.DirectoriesDeleted);
            Assert.False(Directory.Exists(Path.Combine(Workspace, directory)));
        }
        Assert.Equal("outside original", File.ReadAllText(outside));
        Assert.Equal("unrelated original", File.ReadAllText(unrelated));
    }

    [Theory]
    [InlineData("raw", "root-directory")]
    [InlineData("runs", "root-directory")]
    [InlineData("raw", "nested-directory")]
    [InlineData("runs", "nested-directory")]
    [InlineData("raw", "file")]
    [InlineData("runs", "file")]
    public async Task OutsideLinkIsNotExportedOrDeleted(string directory, string kind)
    {
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "keep.txt");
        File.WriteAllText(sentinel, "outside original");
        var dataDirectory = Path.Combine(Workspace, directory);
        var link = dataDirectory;
        string? ownFile = null;
        if (kind != "root-directory")
        {
            Directory.CreateDirectory(dataDirectory);
            ownFile = Path.Combine(dataDirectory, "own.txt");
            File.WriteAllText(ownFile, "own original");
            link = Path.Combine(dataDirectory, "linked");
        }
        if (kind == "file") File.CreateSymbolicLink(link, sentinel);
        else Directory.CreateSymbolicLink(link, outside);
        try
        {
            var exported = await new DataExport(Workspace).ExportAsync(Path.Combine(_root, "linked.zip"));
            var deleted = new DataDelete(Workspace).DeleteAll();

            Assert.False(exported.Success);
            Assert.Contains("link", exported.Error, StringComparison.OrdinalIgnoreCase);
            Assert.False(deleted.Success);
            Assert.Contains(deleted.Errors, error => error.Contains("link", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("outside original", File.ReadAllText(sentinel));
            if (ownFile != null) Assert.Equal("own original", File.ReadAllText(ownFile));
        }
        finally
        {
            if (kind == "file") File.Delete(link);
            else Directory.Delete(link);
        }
    }

    private Dictionary<string, byte[]> CreateEvidence()
    {
        var files = new Dictionary<string, byte[]>
        {
            ["events/legacy.jsonl"] = Encoding.UTF8.GetBytes("legacy\n"),
            ["raw/2026-11-05/outcome.json"] = Encoding.UTF8.GetBytes("{\"text\":\"source evidence\"}\r\n"),
            ["wiki/workflow.md"] = Encoding.UTF8.GetBytes("# Source-linked workflow\n"),
            ["runs/task-123/outcome.json"] = [0, 255, 42, 13, 10],
            ["config/settings.json"] = Encoding.UTF8.GetBytes("{}")
        };
        foreach (var (relativePath, bytes) in files)
        {
            var fullPath = Path.Combine(Workspace, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllBytes(fullPath, bytes);
        }
        return files;
    }

    [Fact]
    public void DeleteAllUnlinksEvidenceWithoutOverwritingOutsideHardLink()
    {
        if (!OperatingSystem.IsWindows()) return;
        var outside = Path.Combine(_root, "outside-hardlink.txt");
        File.WriteAllText(outside, "outside original");
        var raw = Path.Combine(Workspace, "raw");
        Directory.CreateDirectory(raw);
        Assert.True(CreateHardLink(Path.Combine(raw, "evidence.json"), outside, IntPtr.Zero));

        var result = new DataDelete(Workspace).DeleteAll();

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal("outside original", File.ReadAllText(outside));
        Assert.False(Directory.Exists(raw));
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
