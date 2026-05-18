using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Security;

public class SyncService : IDisposable
{
    private readonly EncryptionService _encryption;
    private readonly string _workspaceRoot;
    private readonly ILogger<SyncService>? _logger;
    private bool _disposed;

    private static readonly char Sep = Path.DirectorySeparatorChar;

    public SyncService(EncryptionService encryption, string workspaceRoot, ILogger<SyncService>? logger = null)
    {
        _encryption = encryption;
        _workspaceRoot = workspaceRoot;
        _logger = logger;
    }

    public async Task<SyncPackage> PrepareSyncAsync(CancellationToken ct = default)
    {
        var package = new SyncPackage { DeviceId = Environment.MachineName, CreatedAt = DateTimeOffset.UtcNow };
        var dirs = new[] { "events", "wiki", "config" };

        foreach (var dir in dirs)
        {
            var fullPath = Path.Combine(_workspaceRoot, dir);
            if (!Directory.Exists(fullPath)) continue;

            foreach (var file in Directory.GetFiles(fullPath, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var relativePath = Path.GetRelativePath(_workspaceRoot, file);
                var content = await File.ReadAllBytesAsync(file, ct);
                var encrypted = _encryption.Encrypt(content);

                package.Entries.Add(new SyncEntry
                {
                    Path = relativePath.Replace(Sep, '/'),
                    EncryptedContent = Convert.ToBase64String(encrypted),
                    Hash = Convert.ToBase64String(SHA256.HashData(content)),
                    SizeBytes = content.Length
                });
            }
        }

        package.TotalEntries = package.Entries.Count;
        _logger?.LogInformation("Sync package prepared: {Entries} entries", package.TotalEntries);
        return package;
    }

    public async Task<SyncResult> ApplySyncAsync(SyncPackage package, CancellationToken ct = default)
    {
        var result = new SyncResult { DeviceId = package.DeviceId };

        foreach (var entry in package.Entries)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var encrypted = Convert.FromBase64String(entry.EncryptedContent);
                var decrypted = _encryption.Decrypt(encrypted);
                var targetPath = Path.Combine(_workspaceRoot, entry.Path.Replace('/', Sep));
                var targetDir = Path.GetDirectoryName(targetPath);
                if (targetDir != null) Directory.CreateDirectory(targetDir);

                var hash = Convert.ToBase64String(SHA256.HashData(decrypted));
                if (File.Exists(targetPath))
                {
                    var existing = await File.ReadAllBytesAsync(targetPath, ct);
                    var existingHash = Convert.ToBase64String(SHA256.HashData(existing));
                    if (hash == existingHash) { result.SkippedCount++; continue; }
                }

                await File.WriteAllBytesAsync(targetPath, decrypted, ct);
                result.AppliedCount++;
            }
            catch (Exception ex)
            {
                result.Errors.Add(entry.Path + ": " + ex.Message);
                _logger?.LogWarning(ex, "Failed to apply sync entry: {Path}", entry.Path);
            }
        }

        result.Success = result.Errors.Count == 0;
        _logger?.LogInformation("Sync applied: {Applied} applied, {Skipped} skipped, {Errors} errors",
            result.AppliedCount, result.SkippedCount, result.Errors.Count);
        return result;
    }

    public void Dispose()
    {
        if (!_disposed) { _disposed = true; }
    }
}

public class SyncPackage
{
    public string DeviceId { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public int TotalEntries { get; set; }
    public List<SyncEntry> Entries { get; set; } = new();
}

public class SyncEntry
{
    public string Path { get; init; } = string.Empty;
    public string EncryptedContent { get; init; } = string.Empty;
    public string Hash { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
}

public class SyncResult
{
    public bool Success { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public int AppliedCount { get; set; }
    public int SkippedCount { get; set; }
    public List<string> Errors { get; set; } = new();
}
