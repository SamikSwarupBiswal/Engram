using Engram.Store.Salience;
using Engram.Store.Wiki;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for salience scoring with power law decay.
/// Production requirement: decay formula, archival threshold, batch operations.
/// </summary>
public class SalienceScorerTests
{
    [Fact]
    public void Compute_FreshNode_ReturnsFullSalience()
    {
        var scorer = new SalienceScorer();
        var node = new WikiNode { Salience = 1.0, LastTouchedAt = DateTimeOffset.UtcNow };

        var salience = scorer.Compute(node);

        Assert.True(salience > 0.95); // Nearly 1.0 for just-touched
    }

    [Fact]
    public void Compute_30DaysOld_ReturnsHalf()
    {
        var scorer = new SalienceScorer();
        var node = new WikiNode { Salience = 1.0, LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-30) };

        var salience = scorer.Compute(node);

        Assert.True(salience > 0.45 && salience < 0.55); // ~50% after 30 days
    }

    [Fact]
    public void Compute_60DaysOld_ReturnsQuarter()
    {
        var scorer = new SalienceScorer();
        var node = new WikiNode { Salience = 1.0, LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-60) };

        var salience = scorer.Compute(node);

        Assert.True(salience > 0.20 && salience < 0.30); // ~25% after 60 days
    }

    [Fact]
    public void Compute_DecreasesMonotonically()
    {
        var scorer = new SalienceScorer();
        var scores = new List<double>();

        for (int days = 0; days <= 90; days += 10)
        {
            var node = new WikiNode { Salience = 1.0, LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-days) };
            scores.Add(scorer.Compute(node));
        }

        // Each score should be less than the previous
        for (int i = 1; i < scores.Count; i++)
            Assert.True(scores[i] < scores[i - 1]);
    }

    [Fact]
    public void Compute_CustomLambda_DecaysFaster()
    {
        var fastScorer = new SalienceScorer(lambda: 0.05); // Faster decay
        var normalScorer = new SalienceScorer(); // Default 0.023

        var node = new WikiNode { Salience = 1.0, LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-30) };

        var fastScore = fastScorer.Compute(node);
        var normalScore = normalScorer.Compute(node);

        Assert.True(fastScore < normalScore);
    }

    [Fact]
    public void ShouldArchive_BelowThreshold_ReturnsTrue()
    {
        var scorer = new SalienceScorer();
        var node = new WikiNode { Salience = 1.0, LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-200) };

        Assert.True(scorer.ShouldArchive(node, threshold: 0.1));
    }

    [Fact]
    public void ShouldArchive_AboveThreshold_ReturnsFalse()
    {
        var scorer = new SalienceScorer();
        var node = new WikiNode { Salience = 1.0, LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-5) };

        Assert.False(scorer.ShouldArchive(node, threshold: 0.1));
    }

    [Fact]
    public void ComputeBatch_ReturnsAllNodes()
    {
        var scorer = new SalienceScorer();
        var nodes = new List<WikiNode>
        {
            new() { Salience = 1.0, LastTouchedAt = DateTimeOffset.UtcNow },
            new() { Salience = 1.0, LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-30) }
        };

        var batch = scorer.ComputeBatch(nodes);

        Assert.Equal(2, batch.Count);
    }

    [Fact]
    public void GetStaleNodes_ReturnsLowestFirst()
    {
        var scorer = new SalienceScorer();
        var nodes = new List<WikiNode>
        {
            new() { NodeId = "fresh", Salience = 1.0, LastTouchedAt = DateTimeOffset.UtcNow },
            new() { NodeId = "stale", Salience = 1.0, LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-90) }
        };

        var stale = scorer.GetStaleNodes(nodes, count: 1);

        Assert.Single(stale);
        Assert.Equal("stale", stale[0].Node.NodeId);
    }

    [Fact]
    public void DaysUntilThreshold_CorrectCalculation()
    {
        var scorer = new SalienceScorer();

        // How long until 1.0 decays to 0.5?
        var days = scorer.DaysUntilThreshold(1.0, 0.5);

        Assert.True(days > 29 && days < 31); // ~30 days
    }

    [Fact]
    public void DaysUntilThreshold_AlreadyBelow_ReturnsZero()
    {
        var scorer = new SalienceScorer();

        var days = scorer.DaysUntilThreshold(0.05, 0.1);

        Assert.Equal(0, days);
    }

    [Fact]
    public void DefaultLambda_ProducesExpectedHalfLife()
    {
        Assert.Equal(0.023, SalienceScorer.DefaultLambda);

        // Verify half-life is ~30 days
        var halfLife = Math.Log(2) / SalienceScorer.DefaultLambda;
        Assert.True(halfLife > 29 && halfLife < 31);
    }
}
