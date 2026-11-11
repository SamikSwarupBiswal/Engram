using Engram.Store.Inference;

namespace Engram.Store.Tests;

public sealed class InferenceLifecycleCloudStartupTests
{
    [Fact]
    public void CloudOnlyStartup_IsReadyWithoutLoadingLocalModel()
    {
        using var lifecycle = new InferenceLifecycleManager();

        lifecycle.StartInitialization(preferCloudOnly: true, cloudProvider: "gemini-flash/test-model");

        var health = lifecycle.GetHealth();
        Assert.Equal("Ready", health.State);
        Assert.True(health.IsReady);
        Assert.True(health.CanAcceptRequests);
        Assert.False(health.ModelLoaded);
        Assert.Equal("Cloud", health.Backend);
        Assert.Equal("gemini-flash/test-model", health.ModelName);
        Assert.Equal("cloud-only", health.Metadata["startupMode"]);
        Assert.Equal("false", health.Metadata["localModelPreloaded"]);
    }

    [Fact]
    public void DisabledLocalPreload_WithoutCloud_FailsClosedWithoutLoadingModel()
    {
        using var lifecycle = new InferenceLifecycleManager();

        lifecycle.StartInitialization(disableLocalPreload: true);

        var health = lifecycle.GetHealth();
        Assert.Equal("Error", health.State);
        Assert.False(health.IsReady);
        Assert.False(health.CanAcceptRequests);
        Assert.False(health.ModelLoaded);
        Assert.Equal("local-preload-disabled", health.Metadata["startupMode"]);
        Assert.Equal("false", health.Metadata["localModelPreloaded"]);
        Assert.Contains("no cloud provider", health.Error, StringComparison.OrdinalIgnoreCase);
    }
}
