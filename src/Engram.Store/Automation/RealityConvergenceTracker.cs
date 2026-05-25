using System;
using System.Threading;
using System.Threading.Tasks;

namespace Engram.Store.Automation;

/// <summary>
/// Tracks temporal reality convergence across multiple divergent checks.
/// </summary>
public class RealityConvergenceTracker
{
    private readonly TimeSpan _pollInterval;

    public RealityConvergenceTracker(TimeSpan? pollInterval = null)
    {
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(100);
    }

    /// <summary>
    /// Repeatedly checks a condition over a stabilization window.
    /// If the condition remains true for the entirety of a quiet window, it is considered converged.
    /// If the total timeout expires before consistency is reached, it returns false.
    /// </summary>
    public async Task<bool> TrackConvergenceAsync(
        Func<Task<bool>> checkCondition,
        TimeSpan totalTimeout,
        TimeSpan quietWindow,
        CancellationToken ct)
    {
        var startTime = DateTimeOffset.UtcNow;
        var quietStart = DateTimeOffset.UtcNow;
        bool lastState = false;

        while (DateTimeOffset.UtcNow - startTime < totalTimeout)
        {
            ct.ThrowIfCancellationRequested();

            bool currentState;
            try
            {
                currentState = await checkCondition();
            }
            catch
            {
                currentState = false;
            }

            if (currentState != lastState || !currentState)
            {
                // Reset quiet window timer if state changes or is false
                quietStart = DateTimeOffset.UtcNow;
                lastState = currentState;
            }
            else if (DateTimeOffset.UtcNow - quietStart >= quietWindow)
            {
                // Has remained stable and true for the entire quiet window
                return true;
            }

            await Task.Delay(_pollInterval, ct);
        }

        return false;
    }
}
