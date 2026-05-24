using System;

namespace Engram.Store.Governance;

/// <summary>
/// RecoveryComfortAnalyzer — measures failure annoyance.
/// Rates how comfortably the system recovers from errors. Silent recoveries increase comfort, 
/// while failures that lead to user frustration (dismissals/overrides) penalize the score.
/// </summary>
public class RecoveryComfortAnalyzer
{
    private readonly object _lock = new();
    private int _failuresCount = 0;
    private int _silentRecoveriesCount = 0;
    private int _frustratedFailuresCount = 0;

    public void RecordFailure(bool recoveredSilently)
    {
        lock (_lock)
        {
            _failuresCount++;
            if (recoveredSilently)
            {
                _silentRecoveriesCount++;
            }
        }
    }

    public void RecordUserFrustrationAfterFailure()
    {
        lock (_lock)
        {
            _frustratedFailuresCount++;
        }
    }

    public double CalculateRecoveryComfortScore()
    {
        lock (_lock)
        {
            if (_failuresCount == 0) return 1.0;
            
            double silentRatio = (double)_silentRecoveriesCount / _failuresCount;
            double frustrationFactor = (double)_frustratedFailuresCount / _failuresCount;
            
            double score = (silentRatio * 0.7) + ((1.0 - frustrationFactor) * 0.3);
            return Math.Min(1.0, Math.Max(0.0, score));
        }
    }
}
