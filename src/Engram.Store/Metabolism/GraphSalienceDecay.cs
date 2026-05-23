using System;

namespace Engram.Store.Metabolism;

/// <summary>
/// Computes salience decay constants and values using configuration-driven parameters.
/// </summary>
public class GraphSalienceDecay
{
    private readonly double _lambda;

    public GraphSalienceDecay(double halfLifeDays)
    {
        if (halfLifeDays <= 0) halfLifeDays = 30.0;
        // lambda = ln(2) / halfLife
        _lambda = Math.Log(2) / halfLifeDays;
    }

    /// <summary>
    /// Gets the decay constant lambda.
    /// </summary>
    public double Lambda => _lambda;

    /// <summary>
    /// Computes decayed salience value.
    /// </summary>
    public double CalculateDecay(double initialSalience, DateTimeOffset lastTouchedAt, DateTimeOffset now)
    {
        var days = (now - lastTouchedAt).TotalDays;
        if (days < 0) days = 0;

        return initialSalience * Math.Exp(-_lambda * days);
    }
}
