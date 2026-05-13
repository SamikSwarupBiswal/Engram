namespace Engram.Store.Salience;

/// <summary>
/// Computes salience scores using power law decay.
/// S(t) = S0 * e^(-lambda * t)
/// where t = days since last interaction, lambda = decay constant.
/// </summary>
public class SalienceScorer
{
    private readonly double _lambda;

    /// <summary>
    /// Default lambda: 0.023 per day = 50% salience after 30 days.
    /// </summary>
    public const double DefaultLambda = 0.023;

    public SalienceScorer(double lambda = DefaultLambda)
    {
        _lambda = lambda;
    }

    /// <summary>
    /// Compute current salience for a node based on its last_touched_at.
    /// </summary>
    public double Compute(Wiki.WikiNode node)
    {
        return Compute(node.Salience, node.LastTouchedAt);
    }

    /// <summary>
    /// Compute salience from initial value and last touch time.
    /// </summary>
    public double Compute(double initialSalience, DateTimeOffset lastTouchedAt)
    {
        var daysSinceTouch = (DateTimeOffset.UtcNow - lastTouchedAt).TotalDays;
        if (daysSinceTouch < 0) daysSinceTouch = 0;

        return initialSalience * Math.Exp(-_lambda * daysSinceTouch);
    }

    /// <summary>
    /// Check if a node should be archived (salience below threshold).
    /// </summary>
    public bool ShouldArchive(Wiki.WikiNode node, double threshold = 0.1)
    {
        return Compute(node) < threshold;
    }

    /// <summary>
    /// Compute salience for a batch of nodes.
    /// </summary>
    public IReadOnlyList<(Wiki.WikiNode Node, double Salience)> ComputeBatch(IEnumerable<Wiki.WikiNode> nodes)
    {
        return nodes.Select(n => (n, Compute(n))).ToList();
    }

    /// <summary>
    /// Get nodes sorted by salience (lowest first — most stale).
    /// </summary>
    public IReadOnlyList<(Wiki.WikiNode Node, double Salience)> GetStaleNodes(
        IEnumerable<Wiki.WikiNode> nodes, int count = 10)
    {
        return ComputeBatch(nodes)
            .OrderBy(x => x.Salience)
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// Days until a node reaches a given salience threshold.
    /// </summary>
    public double DaysUntilThreshold(double currentSalience, double targetThreshold)
    {
        if (currentSalience <= 0 || targetThreshold <= 0) return 0;
        if (targetThreshold >= currentSalience) return 0;

        return -Math.Log(targetThreshold / currentSalience) / _lambda;
    }
}
