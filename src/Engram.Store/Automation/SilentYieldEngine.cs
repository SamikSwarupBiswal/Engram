using System;
using System.Threading;
using System.Threading.Tasks;

namespace Engram.Store.Automation;

public class SilentYieldEngine
{
    private readonly SovereigntyMonitor _sovereigntyMonitor;

    public SilentYieldEngine(SovereigntyMonitor sovereigntyMonitor)
    {
        _sovereigntyMonitor = sovereigntyMonitor ?? throw new ArgumentNullException(nameof(sovereigntyMonitor));
    }

    public async Task YieldSilentlyAsync(TimeSpan duration, CancellationToken ct = default)
    {
        var end = DateTimeOffset.UtcNow + duration;
        while (DateTimeOffset.UtcNow < end)
        {
            ct.ThrowIfCancellationRequested();

            // Wait a short interval
            await Task.Delay(100, ct);

            // Keep extending wait if user is still active
            if (_sovereigntyMonitor.DetectUserActivity())
            {
                end = DateTimeOffset.UtcNow + duration;
            }
        }
    }
}
