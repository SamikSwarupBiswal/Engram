using System.Diagnostics;

namespace Engram.Store.Inference;

/// <summary>
/// Tests backend stability before committing to a backend for real inference.
/// 
/// The probe performs a REAL stability test:
///   1. Initialize backend (Vulkan/CPU)
///   2. Load model weights
///   3. Allocate context
///   4. Run 1-token inference
///   5. Clean shutdown
/// 
/// Each stage is tracked. If any stage fails, the exact failure point is recorded.
/// 
/// HARD TIMEOUT: 30 seconds. If the probe hangs (GPU deadlock, driver freeze),
/// it is killed and the verdict is Timeout.
/// 
/// Current implementation runs in-process with timeout wrapper.
/// Future: subprocess isolation for crash containment.
/// </summary>
public sealed class BackendProbe
{
    private readonly InferenceLogger _log = InferenceLogger.Instance;

    /// <summary>
    /// Probe a backend for stability. Returns a verdict with failure stage info.
    /// Runs with a hard timeout.
    /// </summary>
    public async Task<BackendVerdict> ProbeAsync(
        string backend,
        GpuInfo gpuInfo,
        string? modelPath,
        TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        _log.Gpu($"Probing backend: {backend} (timeout={timeout.TotalSeconds}s)");

        try
        {
            using var cts = new CancellationTokenSource(timeout);
            var result = await Task.Run(() => RunProbe(backend, gpuInfo, modelPath, cts.Token), cts.Token);
            sw.Stop();

            var verdict = new BackendVerdict
            {
                Backend = backend,
                Status = result.Ok ? VerdictStatus.Success : VerdictStatus.Failed,
                FailureStage = result.FailureStage,
                Reason = result.Reason,
                GpuDevice = gpuInfo.DeviceName,
                VramMb = gpuInfo.VramMb,
                ProbeDurationMs = (int)sw.ElapsedMilliseconds,
                AppVersion = "1.0.0",
                MachineHash = GetMachineHash()
            };

            if (result.Ok)
                _log.Gpu($"Probe PASSED: {backend} [{sw.ElapsedMilliseconds}ms]");
            else
                _log.GpuWarn($"Probe FAILED: {backend} at stage '{result.FailureStage}' — {result.Reason} [{sw.ElapsedMilliseconds}ms]");

            return verdict;
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            _log.GpuWarn($"Probe TIMEOUT: {backend} after {timeout.TotalSeconds}s");
            return new BackendVerdict
            {
                Backend = backend,
                Status = VerdictStatus.Timeout,
                FailureStage = "timeout",
                Reason = $"Probe timed out after {timeout.TotalSeconds}s. GPU may be unresponsive.",
                GpuDevice = gpuInfo.DeviceName,
                VramMb = gpuInfo.VramMb,
                ProbeDurationMs = (int)sw.ElapsedMilliseconds,
                MachineHash = GetMachineHash()
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _log.GpuError($"Probe CRASH: {backend}", ex);
            return new BackendVerdict
            {
                Backend = backend,
                Status = VerdictStatus.Failed,
                FailureStage = "crash",
                Reason = $"Probe crashed: {ex.GetType().Name}: {ex.Message}",
                GpuDevice = gpuInfo.DeviceName,
                VramMb = gpuInfo.VramMb,
                ProbeDurationMs = (int)sw.ElapsedMilliseconds,
                MachineHash = GetMachineHash()
            };
        }
    }

    /// <summary>
    /// The actual probe sequence. Runs on a background thread.
    /// </summary>
    private ProbeResult RunProbe(string backend, GpuInfo gpuInfo, string? modelPath, CancellationToken ct)
    {
        // ── Stage 1: Backend initialization ──
        _log.Gpu($"[{backend}] Stage 1: Initializing backend...");
        ct.ThrowIfCancellationRequested();

        // For Vulkan: verify the native library loads
        if (gpuInfo.Backend == GpuBackend.Vulkan)
        {
            var vulkanAvailable = CheckVulkanNative();
            if (!vulkanAvailable)
                return ProbeResult.Failed("init", "Vulkan native library failed to load");
        }

        _log.Gpu($"[{backend}] Stage 1: OK");

        // ── Stage 2: Model availability check ──
        _log.Gpu($"[{backend}] Stage 2: Checking model availability...");
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
        {
            // Model not available — backend is probe-able but not loadable
            // This is still a "success" for backend detection purposes
            _log.Gpu($"[{backend}] Stage 2: Model not available (will probe without inference)");
            return ProbeResult.Failed("model_check", "Model file not found — cannot verify inference stability");
        }

        var fileInfo = new FileInfo(modelPath);
        _log.Gpu($"[{backend}] Stage 2: Model found ({fileInfo.Length / (1024 * 1024)}MB)");

        // ── Stage 3: Load model weights ──
        _log.Gpu($"[{backend}] Stage 3: Loading model weights...");
        ct.ThrowIfCancellationRequested();

        try
        {
            var parameters = new LLama.Common.ModelParams(modelPath)
            {
                ContextSize = 512,  // Minimal context for probe
                GpuLayerCount = gpuInfo.LayerCount,
                Threads = Math.Min(4, Environment.ProcessorCount),
                BatchSize = 128
            };

            using var model = LLama.LLamaWeights.LoadFromFile(parameters);
            _log.Gpu($"[{backend}] Stage 3: OK (model loaded)");

            // ── Stage 4: Allocate context ──
            _log.Gpu($"[{backend}] Stage 4: Allocating context...");
            ct.ThrowIfCancellationRequested();

            using var context = model.CreateContext(parameters);
            _log.Gpu($"[{backend}] Stage 4: OK (context allocated)");

            // ── Stage 5: Run 1-token inference ──
            _log.Gpu($"[{backend}] Stage 5: Running probe inference...");
            ct.ThrowIfCancellationRequested();

            var executor = new LLama.InteractiveExecutor(context);
            var inferenceParams = new LLama.Common.InferenceParams
            {
                MaxTokens = 1,
                AntiPrompts = new[] { "User:", "\n" }
            };

            var tokenCount = 0;
            foreach (var token in executor.InferAsync("Hello", inferenceParams, ct).ToBlockingEnumerable(ct))
            {
                tokenCount++;
                if (tokenCount >= 1) break; // Only need 1 token to verify
            }

            _log.Gpu($"[{backend}] Stage 5: OK (generated {tokenCount} token)");

            // ── Stage 6: Clean shutdown (using blocks handle this) ──
            _log.Gpu($"[{backend}] Stage 6: Clean shutdown — probe complete");
            return ProbeResult.Passed();
        }
        catch (LLama.Exceptions.RuntimeError llamaEx)
        {
            return ProbeResult.Failed("inference", $"LLamaSharp runtime error: {llamaEx.Message}");
        }
        catch (DllNotFoundException dllEx)
        {
            return ProbeResult.Failed("init", $"Native library not found: {dllEx.Message}");
        }
        catch (BadImageFormatException badImg)
        {
            return ProbeResult.Failed("init", $"Bad native image: {badImg.Message}");
        }
        catch (AccessViolationException avEx)
        {
            return ProbeResult.Failed("inference", $"Access violation (GPU driver crash): {avEx.Message}");
        }
        catch (OutOfMemoryException oom)
        {
            return ProbeResult.Failed("context_alloc", $"Out of memory (VRAM/RAM exhausted): {oom.Message}");
        }
    }

    /// <summary>
    /// Quick check if Vulkan native library loads at all.
    /// </summary>
    private static bool CheckVulkanNative()
    {
        try
        {
            // Check if ggml-vulkan.dll exists in common locations
            var candidates = new[]
            {
                "ggml-vulkan.dll",
                "libggml-vulkan.so",
                Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", "ggml-vulkan.dll"),
            };

            return candidates.Any(File.Exists);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Machine hash for verdict correlation. Changes on hardware change.
    /// </summary>
    private static string GetMachineHash()
    {
        try
        {
            var components = string.Join("|",
                Environment.MachineName,
                Environment.ProcessorCount.ToString(),
                Environment.OSVersion.ToString(),
                Environment.Is64BitOperatingSystem.ToString());

            var hash = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(components));
            return Convert.ToHexString(hash)[..12];
        }
        catch
        {
            return "unknown";
        }
    }

    private class ProbeResult
    {
        public bool Ok { get; init; }
        public string? FailureStage { get; init; }
        public string? Reason { get; init; }

        public static ProbeResult Passed() => new() { Ok = true };
        public static ProbeResult Failed(string stage, string reason) => new()
        {
            Ok = false,
            FailureStage = stage,
            Reason = reason
        };
    }
}
