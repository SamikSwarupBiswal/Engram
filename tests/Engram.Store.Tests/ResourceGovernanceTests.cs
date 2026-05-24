using System;
using System.Threading.Tasks;
using Engram.Store.Metabolism;
using Xunit;

namespace Engram.Store.Tests;

public class ResourceGovernanceTests
{
    [Fact]
    public void ResourceBudgetGovernor_TransitionsThroughCourtesyStates()
    {
        var governor = new ResourceBudgetGovernor();

        // 1. Gaming / Fullscreen
        var state = governor.EvaluateCourtesyState(
            isUserGaming: true,
            isCreativeWorkloadActive: false,
            isBatterySaverOn: false,
            currentCpuLoad: 10.0,
            isThermalThrottling: false,
            currentHour: 12
        );
        Assert.Equal(ResourceCourtesyState.GamingFullscreen, state);
        Assert.Equal(60, governor.GetMetabolicIntervalMinutes());

        // 2. Thermal Stress
        state = governor.EvaluateCourtesyState(
            isUserGaming: false,
            isCreativeWorkloadActive: false,
            isBatterySaverOn: false,
            currentCpuLoad: 10.0,
            isThermalThrottling: true,
            currentHour: 12
        );
        Assert.Equal(ResourceCourtesyState.ThermalStress, state);
        Assert.Equal(45, governor.GetMetabolicIntervalMinutes());

        // 3. Battery Saver
        state = governor.EvaluateCourtesyState(
            isUserGaming: false,
            isCreativeWorkloadActive: false,
            isBatterySaverOn: true,
            currentCpuLoad: 10.0,
            isThermalThrottling: false,
            currentHour: 12
        );
        Assert.Equal(ResourceCourtesyState.BatterySaver, state);
        Assert.Equal(30, governor.GetMetabolicIntervalMinutes());

        // 4. Heavy Workload (Creative Workload or CPU > 85%)
        state = governor.EvaluateCourtesyState(
            isUserGaming: false,
            isCreativeWorkloadActive: true,
            isBatterySaverOn: false,
            currentCpuLoad: 10.0,
            isThermalThrottling: false,
            currentHour: 12
        );
        Assert.Equal(ResourceCourtesyState.HeavyWorkload, state);
        Assert.Equal(20, governor.GetMetabolicIntervalMinutes());

        state = governor.EvaluateCourtesyState(
            isUserGaming: false,
            isCreativeWorkloadActive: false,
            isBatterySaverOn: false,
            currentCpuLoad: 90.0,
            isThermalThrottling: false,
            currentHour: 12
        );
        Assert.Equal(ResourceCourtesyState.HeavyWorkload, state);

        // 5. Idle Overnight (1am - 5am)
        state = governor.EvaluateCourtesyState(
            isUserGaming: false,
            isCreativeWorkloadActive: false,
            isBatterySaverOn: false,
            currentCpuLoad: 10.0,
            isThermalThrottling: false,
            currentHour: 2 // 2 AM
        );
        Assert.Equal(ResourceCourtesyState.IdleOvernight, state);
        Assert.Equal(5, governor.GetMetabolicIntervalMinutes());

        // 6. Normal
        state = governor.EvaluateCourtesyState(
            isUserGaming: false,
            isCreativeWorkloadActive: false,
            isBatterySaverOn: false,
            currentCpuLoad: 10.0,
            isThermalThrottling: false,
            currentHour: 15 // 3 PM
        );
        Assert.Equal(ResourceCourtesyState.Normal, state);
        Assert.Equal(5, governor.GetMetabolicIntervalMinutes());
    }

    [Fact]
    public async Task ResourceBudgetGovernor_MeasuresSchedulingLatency()
    {
        var governor = new ResourceBudgetGovernor();
        
        Assert.Equal(0.0, governor.SchedulingLatencyMs);

        await governor.MeasureSchedulingLatencyAsync();

        // The EMA should be updated and scheduling latency should be non-negative
        Assert.True(governor.SchedulingLatencyMs >= 0.0);
    }

    [Fact]
    public void ThermalProtectionLayer_InfersThrottling_BasedOnLatency()
    {
        var thermal = new ThermalProtectionLayer();

        // Initially normal
        Assert.False(thermal.IsThrottlingDetected);

        // Feed some fast inference times (45ms baseline)
        thermal.RecordInferenceStats(100, 4500.0); // 45ms per token
        Assert.False(thermal.IsThrottlingDetected);

        // Feed thermal-stress level inference time (consistently > baseline * 2.2 -> > 99ms per token)
        for (int i = 0; i < 5; i++)
        {
            thermal.RecordInferenceStats(100, 11000.0); // 110ms per token
        }

        Assert.True(thermal.IsThrottlingDetected);

        // Reset should restore baseline
        thermal.Reset();
        Assert.False(thermal.IsThrottlingDetected);
        Assert.Equal(45.0, thermal.CurrentMsPerToken);
    }
}
