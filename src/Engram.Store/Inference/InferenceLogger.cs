using System.Collections.Concurrent;

namespace Engram.Store.Inference;

/// <summary>
/// Structured logger for Engram inference lifecycle.
/// Writes tagged, timestamped logs to stdout for sidecar output capture.
/// 
/// Tags: [BOOT] [API] [GPU] [MODEL] [INFERENCE] [ROUTER] [HEALTH] [LIFECYCLE]
/// 
/// Tauri captures sidecar stdout/stderr and forwards to its own log.
/// This logger ensures every lifecycle event is visible and parseable.
/// </summary>
public sealed class InferenceLogger
{
    private static readonly Lazy<InferenceLogger> _instance = new(() => new InferenceLogger());
    public static InferenceLogger Instance => _instance.Value;

    private readonly ConcurrentQueue<LogEntry> _recentEntries = new();
    private const int MaxRecentEntries = 200;

    private InferenceLogger() { }

    public void Boot(string message) => Write("BOOT", message);
    public void Api(string message) => Write("API", message);
    public void Gpu(string message) => Write("GPU", message);
    public void GpuWarn(string message) => Write("GPU", message, level: "WARN");
    public void GpuError(string message, Exception? ex = null) => Write("GPU", message + FormatEx(ex), level: "ERROR");
    public void Model(string message) => Write("MODEL", message);
    public void ModelWarn(string message) => Write("MODEL", message, level: "WARN");
    public void ModelError(string message, Exception? ex = null) => Write("MODEL", message + FormatEx(ex), level: "ERROR");
    public void Inference(string message) => Write("INFERENCE", message);
    public void InferenceWarn(string message) => Write("INFERENCE", message, level: "WARN");
    public void InferenceError(string message, Exception? ex = null) => Write("INFERENCE", message + FormatEx(ex), level: "ERROR");
    public void Router(string message) => Write("ROUTER", message);
    public void Health(string message) => Write("HEALTH", message);
    public void Lifecycle(string message) => Write("LIFECYCLE", message);
    public void LifecycleWarn(string message) => Write("LIFECYCLE", message, level: "WARN");
    public void LifecycleError(string message, Exception? ex = null) => Write("LIFECYCLE", message + FormatEx(ex), level: "ERROR");

    public void Info(string tag, string message) => Write(tag, message);
    public void Warn(string tag, string message) => Write(tag, message, level: "WARN");
    public void Error(string tag, string message, Exception? ex = null) => Write(tag, message + FormatEx(ex), level: "ERROR");

    private void Write(string tag, string message, string level = "INFO")
    {
        var timestamp = DateTime.UtcNow.ToString("HH:mm:ss.fff");
        var levelPad = level switch
        {
            "WARN" => "WARN ",
            "ERROR" => "ERROR",
            _ => "INFO "
        };
        var line = $"[{timestamp}] [{tag,-10}] [{levelPad}] {message}";

        // Write to stderr so it doesn't interfere with stdout-based IPC
        // Tauri captures both stdout and stderr from sidecar
        Console.Error.WriteLine(line);

        _recentEntries.Enqueue(new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            Tag = tag,
            Level = level,
            Message = message
        });

        // Trim old entries
        while (_recentEntries.Count > MaxRecentEntries)
            _recentEntries.TryDequeue(out _);
    }

    public List<LogEntry> GetRecent(int count = 50)
    {
        return _recentEntries.ToArray().TakeLast(count).ToList();
    }

    private static string FormatEx(Exception? ex)
    {
        if (ex == null) return "";
        return $" | {ex.GetType().Name}: {ex.Message}";
    }
}

public class LogEntry
{
    public DateTime Timestamp { get; init; }
    public string Tag { get; init; } = "";
    public string Level { get; init; } = "INFO";
    public string Message { get; init; } = "";
}
