using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Engram.Store.Automation;

public class InputStabilityGuard
{
    private readonly SovereigntyMonitor _sovereigntyMonitor;
    private DateTimeOffset _lastLayoutChange = DateTimeOffset.MinValue;
    private bool _animationActive;
    private bool _dragActive;
    private bool _monitorTransitionActive;

    public InputStabilityGuard(SovereigntyMonitor sovereigntyMonitor)
    {
        _sovereigntyMonitor = sovereigntyMonitor ?? throw new ArgumentNullException(nameof(sovereigntyMonitor));
    }

    public async Task<bool> IsInputStableAsync(CancellationToken ct = default)
    {
        // 1. If human user is actively typing or moving, input is unstable
        if (_sovereigntyMonitor.DetectUserActivity())
        {
            return false;
        }

        // 2. Drag active
        if (_dragActive)
        {
            return false;
        }

        // 3. Monitor transitions
        if (_monitorTransitionActive)
        {
            return false;
        }

        // 4. Layout changes cooldown (must wait at least 500ms after layout change)
        var timeSinceLayoutChange = DateTimeOffset.UtcNow - _lastLayoutChange;
        if (timeSinceLayoutChange < TimeSpan.FromMilliseconds(500))
        {
            return false;
        }

        // 5. Animation active check
        if (_animationActive)
        {
            // Wait briefly for animation to settle
            await Task.Delay(100, ct);
            return !_animationActive;
        }

        return true;
    }

    public void RegisterLayoutChange()
    {
        _lastLayoutChange = DateTimeOffset.UtcNow;
    }

    public void RegisterAnimation(bool isActive)
    {
        _animationActive = isActive;
    }

    public void SetDragActive(bool active)
    {
        _dragActive = active;
    }

    public void SetMonitorTransitionActive(bool active)
    {
        _monitorTransitionActive = active;
    }
}
