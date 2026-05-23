using System;
using System.Threading.RateLimiting;

namespace Engram.Store.Governance;

/// <summary>
/// Controls pacing and frequency of proactive interventions to prevent human cognitive fatigue.
/// Wraps a System.Threading.RateLimiting.TokenBucketRateLimiter.
/// </summary>
public class PacingController : IDisposable
{
    private readonly TokenBucketRateLimiter _limiter;
    private readonly object _lock = new();

    public PacingController(int maxDailyInterventions = 5)
    {
        _limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = maxDailyInterventions,
            QueueLimit = 0,
            ReplenishmentPeriod = TimeSpan.FromDays(1),
            TokensPerPeriod = maxDailyInterventions,
            AutoReplenishment = true
        });
    }

    /// <summary>
    /// Attempts to acquire an intervention token. Returns true if successful.
    /// </summary>
    public bool TryAcquireIntervention()
    {
        lock (_lock)
        {
            var lease = _limiter.AttemptAcquire(1);
            return lease.IsAcquired;
        }
    }

    /// <summary>
    /// Get the current available intervention tokens.
    /// </summary>
    public int GetAvailableTokens()
    {
        lock (_lock)
        {
            var stats = _limiter.GetStatistics();
            return stats != null ? (int)stats.CurrentAvailablePermits : 0;
        }
    }

    public void Dispose()
    {
        _limiter.Dispose();
        GC.SuppressFinalize(this);
    }
}
