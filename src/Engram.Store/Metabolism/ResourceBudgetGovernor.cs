using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Engram.Store.Metabolism;

public enum ResourceCourtesyState
{
    Normal,
    HeavyWorkload,
    GamingFullscreen,
    BatterySaver,
    ThermalStress,
    IdleOvernight
}

public class ResourceBudgetGovernor
{
    private ResourceCourtesyState _currentState = ResourceCourtesyState.Normal;
    private double _schedulingLatencyMs = 0.0;
    private readonly object _lock = new();

    public ResourceCourtesyState CurrentState
    {
        get
        {
            lock (_lock)
            {
                return _currentState;
            }
        }
        set
        {
            lock (_lock)
            {
                _currentState = value;
            }
        }
    }

    public double SchedulingLatencyMs
    {
        get
        {
            lock (_lock)
            {
                return _schedulingLatencyMs;
            }
        }
        private set
        {
            lock (_lock)
            {
                _schedulingLatencyMs = value;
            }
        }
    }

    public async Task MeasureSchedulingLatencyAsync()
    {
        // Measure real scheduling jitter (how delayed is Task.Delay)
        var sw = Stopwatch.StartNew();
        await Task.Delay(100);
        sw.Stop();
        
        var latency = Math.Max(0.0, sw.ElapsedMilliseconds - 100.0);
        
        lock (_lock)
        {
            // Exponential moving average to smooth spikes
            _schedulingLatencyMs = (_schedulingLatencyMs * 0.7) + (latency * 0.3);
        }
    }

    public ResourceCourtesyState EvaluateCourtesyState(
        bool isUserGaming, 
        bool isCreativeWorkloadActive, 
        bool isBatterySaverOn, 
        double currentCpuLoad, 
        bool isThermalThrottling,
        int currentHour)
    {
        if (isUserGaming)
        {
            CurrentState = ResourceCourtesyState.GamingFullscreen;
        }
        else if (isThermalThrottling)
        {
            CurrentState = ResourceCourtesyState.ThermalStress;
        }
        else if (isBatterySaverOn)
        {
            CurrentState = ResourceCourtesyState.BatterySaver;
        }
        else if (isCreativeWorkloadActive || currentCpuLoad > 85.0 || SchedulingLatencyMs > 40.0)
        {
            CurrentState = ResourceCourtesyState.HeavyWorkload;
        }
        else if (currentHour >= 1 && currentHour <= 5) // 1am to 5am
        {
            CurrentState = ResourceCourtesyState.IdleOvernight;
        }
        else
        {
            CurrentState = ResourceCourtesyState.Normal;
        }

        return CurrentState;
    }

    public int GetMetabolicIntervalMinutes()
    {
        return CurrentState switch
        {
            ResourceCourtesyState.GamingFullscreen => 60, // Near-silent
            ResourceCourtesyState.ThermalStress => 45,     // Throttled
            ResourceCourtesyState.BatterySaver => 30,      // Aggressive throttle
            ResourceCourtesyState.HeavyWorkload => 20,     // Defer background
            ResourceCourtesyState.IdleOvernight => 5,      // Maintenance window
            _ => 5                                         // Normal 5 min cadence
        };
    }
}
