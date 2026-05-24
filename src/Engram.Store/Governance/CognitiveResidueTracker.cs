using System;

namespace Engram.Store.Governance;

/// <summary>
/// CognitiveResidueTracker — measures the user's post-use mental burden.
/// Burden accumulates if the user frequently dismisses/cancels tasks, 
/// or switches context rapidly after a system interruption.
/// </summary>
public class CognitiveResidueTracker
{
    private readonly object _lock = new();
    private double _residueScore = 0.0;
    private DateTimeOffset _lastInterruptionAt = DateTimeOffset.MinValue;

    public double CurrentScore
    {
        get
        {
            lock (_lock) return GetCognitiveResidueScore();
        }
    }

    public void RecordInterruption()
    {
        lock (_lock)
        {
            _lastInterruptionAt = DateTimeOffset.UtcNow;
            _residueScore = Math.Min(1.0, _residueScore + 0.15);
        }
    }

    public void RecordUserResponse(bool dismissedOrCancelled)
    {
        lock (_lock)
        {
            if (dismissedOrCancelled)
            {
                _residueScore = Math.Min(1.0, _residueScore + 0.2);
            }
            else
            {
                _residueScore = Math.Max(0.0, _residueScore - 0.1);
            }
        }
    }

    public void RecordContextSwitchAfterInterruption()
    {
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow;
            if (_lastInterruptionAt != DateTimeOffset.MinValue && (now - _lastInterruptionAt).TotalSeconds < 30)
            {
                // Switching focus within 30 seconds of an intervention signals distraction/friction
                _residueScore = Math.Min(1.0, _residueScore + 0.25);
            }
        }
    }

    private double GetCognitiveResidueScore()
    {
        var now = DateTimeOffset.UtcNow;
        if (_lastInterruptionAt != DateTimeOffset.MinValue)
        {
            double hours = (now - _lastInterruptionAt).TotalHours;
            double decay = hours * 0.1; // decay 0.1 per hour of silence
            return Math.Max(0.0, _residueScore - decay);
        }
        return Math.Max(0.0, _residueScore);
    }
}
