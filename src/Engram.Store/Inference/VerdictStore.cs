using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Engram.Store.Inference;

/// <summary>
/// Persists backend compatibility verdicts to disk.
/// Prevents repeatedly hurting users with known-bad backends.
/// 
/// Verdicts include:
/// - Which backend failed/succeeded
/// - Failure stage (init, context_alloc, inference, etc.)
/// - Hardware/driver info for correlation
/// - Expiry policy (re-attempt after driver change, app update, or 14 days)
/// 
/// Stored at: ~/.engram/backend-verdicts.json
/// </summary>
public sealed class VerdictStore
{
    private readonly string _verdictPath;
    private readonly object _lock = new();
    private VerdictDatabase _db;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>How long a failed verdict is considered valid before re-attempt.</summary>
    public static readonly TimeSpan FailedVerdictExpiry = TimeSpan.FromDays(14);

    /// <summary>How long a success verdict is cached (re-validate on app update).</summary>
    public static readonly TimeSpan SuccessVerdictExpiry = TimeSpan.FromDays(7);

    public VerdictStore(string workspaceRoot)
    {
        _verdictPath = Path.Combine(workspaceRoot, "backend-verdicts.json");
        _db = Load();
    }

    /// <summary>
    /// Check if a backend has a valid (non-expired) verdict.
    /// Returns null if no verdict exists or verdict is expired.
    /// </summary>
    public BackendVerdict? GetVerdict(string backend)
    {
        lock (_lock)
        {
            var verdict = _db.Verdicts.GetValueOrDefault(backend);
            if (verdict == null) return null;

            // Check expiry
            var age = DateTime.UtcNow - verdict.Timestamp;
            var maxAge = verdict.Status == VerdictStatus.Success
                ? SuccessVerdictExpiry
                : FailedVerdictExpiry;

            if (age > maxAge)
            {
                return null; // Expired — should re-probe
            }

            return verdict;
        }
    }

    /// <summary>
    /// Record a verdict for a backend.
    /// </summary>
    public void Record(BackendVerdict verdict)
    {
        lock (_lock)
        {
            _db.Verdicts[verdict.Backend] = verdict;
            _db.LastUpdated = DateTime.UtcNow;
            Save();
        }
    }

    /// <summary>
    /// Check if a backend should be skipped based on verdict history.
    /// Returns true if the backend has a recent failure verdict.
    /// </summary>
    public bool ShouldSkipBackend(string backend)
    {
        var verdict = GetVerdict(backend);
        return verdict is { Status: VerdictStatus.Failed } or { Status: VerdictStatus.Timeout };
    }

    /// <summary>
    /// Invalidate all verdicts for a backend (force re-probe).
    /// Called when: driver changes, app updates, user requests retry.
    /// </summary>
    public void Invalidate(string backend)
    {
        lock (_lock)
        {
            _db.Verdicts.Remove(backend);
            Save();
        }
    }

    /// <summary>
    /// Invalidate all verdicts (nuclear option).
    /// </summary>
    public void InvalidateAll()
    {
        lock (_lock)
        {
            _db.Verdicts.Clear();
            Save();
        }
    }

    /// <summary>
    /// Get all stored verdicts for diagnostics.
    /// </summary>
    public Dictionary<string, BackendVerdict> GetAll()
    {
        lock (_lock)
        {
            return new Dictionary<string, BackendVerdict>(_db.Verdicts);
        }
    }

    // ── Persistence ──

    private VerdictDatabase Load()
    {
        try
        {
            if (!File.Exists(_verdictPath))
                return new VerdictDatabase();

            var json = File.ReadAllText(_verdictPath);
            return JsonSerializer.Deserialize<VerdictDatabase>(json, JsonOpts) ?? new VerdictDatabase();
        }
        catch
        {
            return new VerdictDatabase();
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_verdictPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_db, JsonOpts);
            File.WriteAllText(_verdictPath, json);
        }
        catch
        {
            // Non-fatal — verdict persistence is best-effort
        }
    }
}

/// <summary>
/// A single backend compatibility verdict.
/// </summary>
public class BackendVerdict
{
    /// <summary>Backend name: "Vulkan", "Cpu"</summary>
    public string Backend { get; init; } = "";

    /// <summary>Success or failure.</summary>
    public VerdictStatus Status { get; init; }

    /// <summary>Where failure occurred: "init", "context_alloc", "first_inference", "clean_shutdown"</summary>
    public string? FailureStage { get; init; }

    /// <summary>Human-readable failure reason.</summary>
    public string? Reason { get; init; }

    /// <summary>GPU device name detected.</summary>
    public string? GpuDevice { get; init; }

    /// <summary>GPU driver version if available.</summary>
    public string? DriverVersion { get; init; }

    /// <summary>VRAM in MB.</summary>
    public int VramMb { get; init; }

    /// <summary>Machine hash for correlation (changes on hardware change).</summary>
    public string? MachineHash { get; init; }

    /// <summary>App version when verdict was recorded.</summary>
    public string? AppVersion { get; init; }

    /// <summary>When the probe was run.</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>How long the probe took (ms).</summary>
    public int ProbeDurationMs { get; init; }

    /// <summary>Whether this verdict should be re-attempted.</summary>
    public bool IsExpired => (DateTime.UtcNow - Timestamp) > (
        Status == VerdictStatus.Success
            ? VerdictStore.SuccessVerdictExpiry
            : VerdictStore.FailedVerdictExpiry);
}

public enum VerdictStatus
{
    Success,
    Failed,
    Timeout,
    Skipped
}

internal class VerdictDatabase
{
    public Dictionary<string, BackendVerdict> Verdicts { get; set; } = new();
    public DateTime LastUpdated { get; set; }
    public string? AppVersion { get; set; }
}
