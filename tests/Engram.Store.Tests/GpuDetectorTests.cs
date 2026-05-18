using Engram.Store.Inference;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for GpuDetector.
/// Validates GPU detection, fallback behavior, and layer count calculation.
/// Note: GPU detection is hardware-dependent, so tests verify logic paths.
/// </summary>
public class GpuDetectorTests : IDisposable
{
    public void Dispose() { }

    // ─── Constructor ───

    [Fact]
    public void Constructor_DoesNotThrow()
    {
        var detector = new GpuDetector();
        Assert.NotNull(detector);
    }

    // ─── Detect ───

    [Fact]
    public void Detect_NeverThrows()
    {
        var detector = new GpuDetector();
        var result = detector.Detect();
        Assert.NotNull(result);
    }

    [Fact]
    public void Detect_ReturnsValidGpuInfo()
    {
        var detector = new GpuDetector();
        var result = detector.Detect();

        Assert.NotNull(result.DeviceName);
        Assert.NotNull(result.Description);
        Assert.True(result.VramMb >= 0);
        Assert.True(result.LayerCount >= 0);
    }

    [Fact]
    public void Detect_BackendIsCpuOrVulkan()
    {
        var detector = new GpuDetector();
        var result = detector.Detect();

        Assert.True(result.Backend == GpuBackend.Cpu || result.Backend == GpuBackend.Vulkan);
    }

    // ─── GpuInfo Model ───

    [Fact]
    public void GpuInfo_DefaultValues_AreCorrect()
    {
        var info = new GpuInfo();
        Assert.Equal(GpuBackend.Cpu, info.Backend);
        Assert.Equal("CPU", info.DeviceName);
        Assert.Equal(0, info.VramMb);
        Assert.Equal(0, info.LayerCount);
        Assert.Equal("CPU+SIMD", info.Description);
    }

    [Fact]
    public void GpuInfo_WithValues_PreservesData()
    {
        var info = new GpuInfo
        {
            Backend = GpuBackend.Vulkan,
            DeviceName = "RTX 4090",
            VramMb = 24000,
            LayerCount = 32,
            Description = "Vulkan: RTX 4090 (24000MB)"
        };

        Assert.Equal(GpuBackend.Vulkan, info.Backend);
        Assert.Equal("RTX 4090", info.DeviceName);
        Assert.Equal(24000, info.VramMb);
        Assert.Equal(32, info.LayerCount);
    }

    // ─── VulkanDevice Model ───

    [Fact]
    public void VulkanDevice_DefaultValues_AreCorrect()
    {
        var device = new VulkanDevice();
        Assert.Equal(string.Empty, device.Name);
        Assert.Equal(0, device.VramMb);
        Assert.Equal(0, device.Index);
    }

    [Fact]
    public void VulkanDevice_WithValues_PreservesData()
    {
        var device = new VulkanDevice
        {
            Name = "NVIDIA RTX 3080",
            VramMb = 10000,
            Index = 0
        };

        Assert.Equal("NVIDIA RTX 3080", device.Name);
        Assert.Equal(10000, device.VramMb);
        Assert.Equal(0, device.Index);
    }

    // ─── GpuBackend Enum ───

    [Fact]
    public void GpuBackend_HasExpectedValues()
    {
        Assert.Equal(0, (int)GpuBackend.Cpu);
        Assert.Equal(1, (int)GpuBackend.Vulkan);
    }
}
