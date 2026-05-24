using System;
using System.Threading;
using System.Threading.Tasks;

namespace Engram.Store.Automation;

public class CooperativeCursorProtocol
{
    private readonly SovereigntyMonitor _sovereigntyMonitor;
    private bool _cursorSovereigntyHeld;

    public bool CursorSovereigntyHeld => _cursorSovereigntyHeld;

    public CooperativeCursorProtocol(SovereigntyMonitor sovereigntyMonitor)
    {
        _sovereigntyMonitor = sovereigntyMonitor ?? throw new ArgumentNullException(nameof(sovereigntyMonitor));
    }

    public async Task<bool> RequestCursorSovereigntyAsync(CancellationToken ct = default)
    {
        // If human is actively moving the cursor, yield sovereignty
        if (_sovereigntyMonitor.DetectUserActivity())
        {
            YieldToHuman();
            return false;
        }

        _cursorSovereigntyHeld = true;
        return true;
    }

    public void YieldToHuman()
    {
        _cursorSovereigntyHeld = false;
    }
}
