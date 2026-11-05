using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Security;

public class DataExport
{
    private readonly string _workspaceRoot;
    private readonly ILogger<DataExport>? _logger;

    private static readonly char Sep = Path.DirectorySeparatorChar;

    public DataExport(string workspaceRoot, ILogger<DataExport>? logger = null)
    {
        _workspaceRoot = workspaceRoot;
        _logger = logger;
    }

    public async Task<ExportResult> ExportAsync(string outputPath, CancellationToken ct = default)
    {
        var result = new ExportResult { OutputPath = outputPath };
        try
        {
            using var zip = ZipFile.Open(outputPath, ZipArchiveMode.Create);
            var dirs = new[]
            {
                ("events", Path.Combine(_workspaceRoot, "events")),
                ("raw", Path.Combine(_workspaceRoot, "raw")),
                ("wiki", Path.Combine(_workspaceRoot, "wiki")),
                ("runs", Path.Combine(_workspaceRoot, "runs")),
                ("config", Path.Combine(_workspaceRoot, "config")),
                ("research", Path.Combine(_workspaceRoot, "config", "research"))
            };

            foreach (var (name, dir) in dirs)
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var file in WorkspaceDataFiles.Collect(_workspaceRoot, dir))
                {
                    ct.ThrowIfCancellationRequested();
                    var relativePath = Path.GetRelativePath(_workspaceRoot, file);
                    var entryName = relativePath.Replace(Sep, '/');
                    zip.CreateEntryFromFile(file, entryName, CompressionLevel.Fastest);
                    result.FileCount++;
                    result.TotalBytes += new FileInfo(file).Length;
                }
            }

            var manifest = new ExportManifest
            {
                ExportedAt = DateTimeOffset.UtcNow,
                Version = "1.0.0",
                FileCount = result.FileCount,
                TotalBytes = result.TotalBytes
            };
            var manifestEntry = zip.CreateEntry("manifest.json");
            using var stream = manifestEntry.Open();
            await JsonSerializer.SerializeAsync(stream, manifest, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                WriteIndented = true
            }, ct);

            result.Success = true;
            _logger?.LogInformation("Export complete: {Files} files, {Bytes} bytes", result.FileCount, result.TotalBytes);
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            _logger?.LogError(ex, "Export failed");
        }
        return result;
    }

    public async Task<ImportResult> ImportAsync(string zipPath, CancellationToken ct = default)
    {
        var result = new ImportResult { InputPath = zipPath };
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            foreach (var entry in zip.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (entry.FullName == "manifest.json") continue;
                var targetPath = Path.Combine(_workspaceRoot, entry.FullName.Replace('/', Sep));
                var targetDir = Path.GetDirectoryName(targetPath);
                if (targetDir != null) Directory.CreateDirectory(targetDir);
                entry.ExtractToFile(targetPath, overwrite: true);
                result.FileCount++;
            }
            result.Success = true;
            _logger?.LogInformation("Import complete: {Files} files", result.FileCount);
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            _logger?.LogError(ex, "Import failed");
        }
        return result;
    }
}

public class DataDelete
{
    private readonly string _workspaceRoot;
    private readonly ILogger<DataDelete>? _logger;

    public DataDelete(string workspaceRoot, ILogger<DataDelete>? logger = null)
    {
        _workspaceRoot = workspaceRoot;
        _logger = logger;
    }

    public DeleteResult DeleteAll()
    {
        var result = new DeleteResult();
        var dirs = new[] { "events", "raw", "wiki", "runs", "config" };

        foreach (var dir in dirs)
        {
            var fullPath = Path.Combine(_workspaceRoot, dir);
            if (!Directory.Exists(fullPath)) continue;
            try
            {
                var files = WorkspaceDataFiles.Collect(_workspaceRoot, fullPath);
                result.FileCount += files.Count;
                foreach (var file in files)
                {
                    // Unlink only: overwriting shared hard-link data can corrupt a
                    // file outside the workspace and cannot promise secure erasure.
                    File.Delete(file);
                }
                Directory.Delete(fullPath, recursive: true);
                result.DirectoriesDeleted.Add(dir);
            }
            catch (Exception ex)
            {
                result.Errors.Add(dir + ": " + ex.Message);
            }
        }

        result.Success = result.Errors.Count == 0;
        _logger?.LogInformation("Data deletion: {Files} files, {Dirs} directories", result.FileCount, result.DirectoriesDeleted.Count);
        return result;
    }
}

internal static class WorkspaceDataFiles
{
    internal static List<string> Collect(string workspaceRoot, string directory)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        var directoryPath = Path.GetFullPath(directory);
        var relative = Path.GetRelativePath(root, directoryPath);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new IOException("Data path is outside the workspace.");

        for (var ancestor = new DirectoryInfo(directoryPath); ancestor != null; ancestor = ancestor.Parent)
        {
            RejectLink(ancestor.FullName, ancestor.Attributes);
            if (string.Equals(ancestor.FullName, root, StringComparison.OrdinalIgnoreCase)) break;
        }

        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(directoryPath);
        while (pending.TryPop(out var current))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                var attributes = File.GetAttributes(entry);
                RejectLink(entry, attributes);
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                else files.Add(entry);
            }
        }
        return files;
    }

    private static void RejectLink(string path, FileAttributes attributes)
    {
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Workspace data contains a link; refusing to follow it: {path}");
    }
}

public class ExportResult
{
    public bool Success { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public int FileCount { get; set; }
    public long TotalBytes { get; set; }
    public string? Error { get; set; }
}

public class ImportResult
{
    public bool Success { get; set; }
    public string InputPath { get; set; } = string.Empty;
    public int FileCount { get; set; }
    public string? Error { get; set; }
}

public class DeleteResult
{
    public bool Success { get; set; }
    public int FileCount { get; set; }
    public List<string> DirectoriesDeleted { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

public class ExportManifest
{
    public DateTimeOffset ExportedAt { get; set; }
    public string Version { get; set; } = string.Empty;
    public int FileCount { get; set; }
    public long TotalBytes { get; set; }
}
