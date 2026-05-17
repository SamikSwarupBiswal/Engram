using Engram.Store.Inference;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for the inference engine: GPU detection, model management, router.
/// Does NOT test actual LLamaSharp inference (requires model download).
/// </summary>
public class InferenceEngineTests : IDisposable
{
    private readonly string _tempDir;

    public InferenceEngineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram-inference-" + Guid.NewGuid().ToString("N")[..8]);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ─── GPU Detection ───

    [Fact]
    public void GpuDetector_Detect_ReturnsInfo()
    {
        var detector = new GpuDetector();
        var info = detector.Detect();

        Assert.NotNull(info);
        Assert.NotNull(info.Backend.ToString());
        Assert.NotNull(info.Description);
    }

    [Fact]
    public void GpuDetector_CpuFallback_Works()
    {
        // On CI/WSL without GPU, should fall back to CPU
        var detector = new GpuDetector();
        var info = detector.Detect();

        // Should always return something (CPU at minimum)
        Assert.True(info.Backend == GpuBackend.Cpu || info.Backend == GpuBackend.Vulkan);
    }

    // ─── Model Manager ───

    [Fact]
    public void ModelManager_GetModelsDirectory_ReturnsValidPath()
    {
        var dir = ModelManager.GetModelsDirectory();
        Assert.NotNull(dir);
        Assert.Contains("Engram", dir);
        Assert.Contains("models", dir);
    }

    [Fact]
    public void ModelManager_ModelPath_Correct()
    {
        var path = ModelManager.GetModelPath(ModelManager.Phi4Mini);
        Assert.EndsWith("phi-4-mini-q4_k_m.gguf", path);
    }

    [Fact]
    public void ModelManager_IsModelReady_FalseWhenNotDownloaded()
    {
        var manager = new ModelManager();
        Assert.False(manager.IsModelReady(ModelManager.Phi4Mini));
    }

    [Fact]
    public void ModelManager_GetStatus_NotDownloaded()
    {
        var manager = new ModelManager();
        var status = manager.GetStatus(ModelManager.Phi4Mini);

        Assert.Equal(ModelState.NotDownloaded, status.State);
    }

    [Fact]
    public void ModelManager_Phi4MiniConfig_HasCorrectValues()
    {
        var config = ModelManager.Phi4Mini;

        Assert.Equal("Phi-4-mini", config.Name);
        Assert.Equal("phi-4-mini-q4_k_m.gguf", config.FileName);
        Assert.Contains("huggingface", config.DownloadUrl);
        Assert.True(config.SizeBytes > 2_000_000_000);
        Assert.Equal(4096, config.ContextSize);
    }

    [Fact]
    public void ModelManager_DeleteModel_ReturnsFalseWhenNotExist()
    {
        var manager = new ModelManager();
        Assert.False(manager.DeleteModel(ModelManager.Phi4Mini));
    }

    // ─── Inference Router ───

    [Fact]
    public void InferenceRouter_DefaultMode_IsEco()
    {
        var gpuDetector = new GpuDetector();
        var modelManager = new ModelManager();
        var engine = new LocalInferenceEngine(modelManager, gpuDetector);
        var router = new InferenceRouter(engine);

        Assert.Equal(PowerMode.Eco, router.PowerMode);
    }

    [Fact]
    public void InferenceRouter_SetPowerMode_Works()
    {
        var gpuDetector = new GpuDetector();
        var modelManager = new ModelManager();
        var engine = new LocalInferenceEngine(modelManager, gpuDetector);
        var router = new InferenceRouter(engine);

        router.PowerMode = PowerMode.Turbo;
        Assert.Equal(PowerMode.Turbo, router.PowerMode);

        router.PowerMode = PowerMode.Eco;
        Assert.Equal(PowerMode.Eco, router.PowerMode);
    }

    [Fact]
    public async Task InferenceRouter_EcoMode_NoModel_ReturnsError()
    {
        var gpuDetector = new GpuDetector();
        var modelManager = new ModelManager();
        var engine = new LocalInferenceEngine(modelManager, gpuDetector);
        var router = new InferenceRouter(engine);

        var result = await router.ChatCompletionAsync(new[]
        {
            new ChatMessage { Role = "user", Content = "Hello" }
        });

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("model", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InferenceRouter_TurboMode_NoCloud_ReturnsError()
    {
        var gpuDetector = new GpuDetector();
        var modelManager = new ModelManager();
        var engine = new LocalInferenceEngine(modelManager, gpuDetector);
        var router = new InferenceRouter(engine, cloudPipeline: null);

        router.PowerMode = PowerMode.Turbo;
        var result = await router.ChatCompletionAsync(new[]
        {
            new ChatMessage { Role = "user", Content = "Hello" }
        });

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    // ─── Local Inference Engine ───

    [Fact]
    public void LocalEngine_NotReady_Initially()
    {
        var gpuDetector = new GpuDetector();
        var modelManager = new ModelManager();
        var engine = new LocalInferenceEngine(modelManager, gpuDetector);

        Assert.False(engine.IsReady);
        Assert.False(engine.IsLoading);
        Assert.Null(engine.LoadedModel);
    }

    [Fact]
    public void LocalEngine_LoadModel_FailsWithoutModel()
    {
        var gpuDetector = new GpuDetector();
        var modelManager = new ModelManager();
        var engine = new LocalInferenceEngine(modelManager, gpuDetector);

        var loaded = engine.LoadModel();
        Assert.False(loaded);
        Assert.False(engine.IsReady);
    }

    [Fact]
    public async Task LocalEngine_ChatCompletion_FailsWhenNotReady()
    {
        var gpuDetector = new GpuDetector();
        var modelManager = new ModelManager();
        var engine = new LocalInferenceEngine(modelManager, gpuDetector);

        var result = await engine.ChatCompletionAsync(new[]
        {
            new ChatMessage { Role = "user", Content = "Hello" }
        });

        Assert.False(result.Success);
        Assert.Contains("model", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LocalEngine_UnloadModel_Idempotent()
    {
        var gpuDetector = new GpuDetector();
        var modelManager = new ModelManager();
        var engine = new LocalInferenceEngine(modelManager, gpuDetector);

        engine.UnloadModel(); // Should not throw
        engine.UnloadModel(); // Should not throw
        Assert.False(engine.IsReady);
    }

    // ─── ChatMessage ───

    [Fact]
    public void ChatMessage_DefaultValues()
    {
        var msg = new ChatMessage();
        Assert.Equal("user", msg.Role);
        Assert.Equal(string.Empty, msg.Content);
    }

    // ─── InferenceResult ───

    [Fact]
    public void InferenceResult_Failed_CreatesErrorResult()
    {
        var result = InferenceResult.Failed("test error");

        Assert.False(result.Success);
        Assert.Equal("test error", result.ErrorMessage);
        Assert.Equal("local", result.Provider);
        Assert.Equal(string.Empty, result.Content);
    }

    // ─── Power Mode Enum ───

    [Fact]
    public void PowerMode_HasCorrectValues()
    {
        Assert.Equal(0, (int)PowerMode.Eco);
        Assert.Equal(1, (int)PowerMode.Turbo);
    }
}
