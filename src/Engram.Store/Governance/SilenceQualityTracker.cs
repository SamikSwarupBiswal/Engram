using System;

namespace Engram.Store.Governance;

/// <summary>
/// Measures the quality and sustainability of the system's quiet presence.
/// Silence quality is higher if interventions are highly justified (lead to user action/acknowledgement)
/// and quiet intervals are long.
/// </summary>
public class SilenceQualityTracker
{
    private readonly object _lock = new();
    private DateTimeOffset _lastInterruptionAt = DateTimeOffset.UtcNow;
    private double _totalSilenceDurationSeconds = 0.0;
    private int _totalInterventions = 0;
    private int _justifiedInterventions = 0;

    public void RecordInterruption()
    {
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow;
            var silence = (now - _lastInterruptionAt).TotalSeconds;
            if (silence > 0)
            {
                _totalSilenceDurationSeconds += silence;
            }
            _lastInterruptionAt = now;
            _totalInterventions++;
        }
    }

    public void RecordInterventionJustification(bool justified)
    {
        lock (_lock)
        {
            if (justified)
            {
                _justifiedInterventions++;
            }
        }
    }

    public double GetJustificationRatio()
    {
        lock (_lock)
        {
            if (_totalInterventions == 0) return 1.0;
            return (double)_justifiedInterventions / _totalInterventions;
        }
    }

    public double CalculateSilenceQualityScore()
    {
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow;
            var currentSilence = (now - _lastInterruptionAt).TotalSeconds;
            var totalSilence = _totalSilenceDurationSeconds + (currentSilence > 0 ? currentSilence : 0);
            
            double hours = totalSilence / 3600.0;
            double justification = GetJustificationRatio();
            
            // Scaled quality index (higher hours and higher justification = higher score)
            double silenceIndex = 1.0 - (1.0 / (1.0 + hours * 10.0)); // scales quickly
            return Math.Min(1.0, (justification * 0.6) + (silenceIndex * 0.4));
        }
    }
}
