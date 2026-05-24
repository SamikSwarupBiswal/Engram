using System;
using System.Collections.Generic;

namespace Engram.Store.Governance;

/// <summary>
/// AuthorityPressureMonitor — measures system overpresence.
/// A high pressure score occurs if the system prompts the user frequently, 
/// particularly during periods when the user is highly active (competing for focus).
/// </summary>
public class AuthorityPressureMonitor
{
    private readonly object _lock = new();
    private readonly List<DateTimeOffset> _promptTimestamps = new();
    private readonly List<DateTimeOffset> _userActiveTimestamps = new();

    public void RecordPrompt()
    {
        lock (_lock)
        {
            _promptTimestamps.Add(DateTimeOffset.UtcNow);
        }
    }

    public void RecordUserActivity()
    {
        lock (_lock)
        {
            _userActiveTimestamps.Add(DateTimeOffset.UtcNow);
        }
    }

    public double CalculatePressureScore()
    {
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow;
            var oneHourAgo = now.AddHours(-1);
            
            _promptTimestamps.RemoveAll(t => t < oneHourAgo);
            _userActiveTimestamps.RemoveAll(t => t < oneHourAgo);

            int prompts = _promptTimestamps.Count;
            int userActiveCount = _userActiveTimestamps.Count;

            // Base pressure from prompts in last hour
            double promptPressure = prompts / 5.0; // 5 prompts per hour scaled as baseline 1.0 pressure
            
            // Interaction factor: prompts happening while user is highly active increases irritation/pressure
            double overlapFactor = userActiveCount > 0 ? 0.3 : 0.0;
            
            double score = (promptPressure * 0.7) + (promptPressure * overlapFactor);
            return Math.Min(1.0, Math.Max(0.0, score));
        }
    }
}
