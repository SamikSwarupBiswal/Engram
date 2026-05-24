using System;
using System.Collections.Concurrent;

namespace Engram.Store.Automation;

public class InteractionDebounceEngine
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _actionHistory = new();
    private readonly TimeSpan _debounceThreshold;

    public InteractionDebounceEngine(TimeSpan? debounceThreshold = null)
    {
        _debounceThreshold = debounceThreshold ?? TimeSpan.FromMilliseconds(500);
    }

    public bool RecordActionAndCheckDebounce(AutomationAction action)
    {
        if (action == null) return false;

        // Do not debounce non-interactive or wait/screenshot/navigate actions
        if (action.Type == ActionType.Wait || action.Type == ActionType.Screenshot || action.Type == ActionType.Navigate)
        {
            return false;
        }

        // Construct unique signature for the action
        var actionSignature = $"{action.Type}_{action.Target?.Selector ?? ""}_{action.Target?.X ?? 0}_{action.Target?.Y ?? 0}_{action.Value ?? ""}";

        var now = DateTimeOffset.UtcNow;
        bool isDebounced = false;

        _actionHistory.AddOrUpdate(actionSignature, 
            now, 
            (key, lastExecuted) => 
            {
                if (now - lastExecuted < _debounceThreshold)
                {
                    isDebounced = true;
                    return lastExecuted; // Don't update timestamp on debounced action
                }
                return now;
            });

        return isDebounced;
    }

    public void ClearHistory()
    {
        _actionHistory.Clear();
    }
}
