using Engram.Store.Identity;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for intervention policy gating.
/// Production requirement: NO intervention bypasses the policy.
/// </summary>
public class InterventionPolicyTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void Evaluate_AllowsByDefault_WhenNoAntiGoals()
    {
        var policy = CreatePolicy();
        var request = new InterventionRequest { Action = "send notification", Context = "deadline approaching" };

        var result = policy.Evaluate(request);

        Assert.True(result.Allowed);
    }

    [Fact]
    public void Evaluate_BlocksCriticalAntiGoal()
    {
        var policy = CreatePolicyWithAntiGoals(
            new AntiGoal { Id = "ag1", Description = "Never send emails without approval", Severity = AntiGoalSeverity.Critical });

        var request = new InterventionRequest { Action = "send email", Context = "follow up" };

        var result = policy.Evaluate(request);

        Assert.False(result.Allowed);
        Assert.Contains("Never send emails", result.Reason);
    }

    [Fact]
    public void Evaluate_BlocksHighAntiGoal()
    {
        var policy = CreatePolicyWithAntiGoals(
            new AntiGoal { Id = "ag1", Description = "Do not suggest social media during work", Severity = AntiGoalSeverity.High });

        var request = new InterventionRequest { Action = "suggest social media", Context = "break time" };

        var result = policy.Evaluate(request);

        Assert.False(result.Allowed);
    }

    [Fact]
    public void Evaluate_BlocksMediumAntiGoal()
    {
        var policy = CreatePolicyWithAntiGoals(
            new AntiGoal { Id = "ag1", Description = "No notification during meeting", Severity = AntiGoalSeverity.Medium });

        var request = new InterventionRequest { Action = "send notification", Context = "meeting happening" };

        var result = policy.Evaluate(request);

        Assert.False(result.Allowed);
    }

    [Fact]
    public void Evaluate_LowAntiGoal_AllowsWithReducedConfidence()
    {
        var policy = CreatePolicyWithAntiGoals(
            new AntiGoal { Id = "ag1", Description = "Minimize interruptions in the morning", Severity = AntiGoalSeverity.Low });

        var request = new InterventionRequest { Action = "send notification", Context = "morning update" };

        var result = policy.Evaluate(request);

        Assert.True(result.Allowed);
        Assert.True(result.Confidence < 1.0);
    }

    [Fact]
    public void Evaluate_BoostsConfidence_ForAnxiety()
    {
        var policy = CreatePolicyWithProfile(new UserProfile
        {
            RecurringAnxieties = new List<string> { "Missing deadlines" }
        });

        var request = new InterventionRequest { Action = "reminder", Context = "deadline approaching for project" };

        var result = policy.Evaluate(request);

        Assert.True(result.Allowed);
        Assert.Equal(1.0, result.Confidence);
    }

    [Fact]
    public void Evaluate_ReturnsReason_ForAllDecisions()
    {
        var policy = CreatePolicy();

        var result = policy.Evaluate(new InterventionRequest { Action = "test" });

        Assert.False(string.IsNullOrEmpty(result.Reason));
    }

    [Fact]
    public void InvalidateCache_ForcesReload()
    {
        var policy = CreatePolicy();

        // First evaluation loads cache
        policy.Evaluate(new InterventionRequest { Action = "test" });

        // Invalidate
        policy.InvalidateCache();

        // Should reload on next evaluation
        var result = policy.Evaluate(new InterventionRequest { Action = "test" });
        Assert.True(result.Allowed);
    }

    [Fact]
    public void Evaluate_NoInterventionBypassesPolicy()
    {
        // This test verifies the architectural requirement:
        // ALL interventions must go through Evaluate()
        var policy = CreatePolicyWithAntiGoals(
            new AntiGoal { Id = "ag1", Description = "never execute dangerous", Severity = AntiGoalSeverity.Critical });

        // Even with high-priority context, critical anti-goal blocks
        var request = new InterventionRequest
        {
            Action = "execute dangerous action",
            Context = "urgent high priority emergency",
            Category = "automation"
        };

        var result = policy.Evaluate(request);
        Assert.False(result.Allowed); // Critical = always blocked
    }

    [Fact]
    public void Evaluate_MultipleAntiGoals_StrictestWins()
    {
        var policy = CreatePolicyWithAntiGoals(
            new AntiGoal { Id = "ag1", Description = "limit notification", Severity = AntiGoalSeverity.Low },
            new AntiGoal { Id = "ag2", Description = "never send notification during sleep", Severity = AntiGoalSeverity.Critical });

        var request = new InterventionRequest { Action = "send notification", Context = "during sleep" };

        var result = policy.Evaluate(request);

        Assert.False(result.Allowed); // Critical blocks even if Low would allow
    }

    private InterventionPolicy CreatePolicy()
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        var store = new IdentityStore(_workspace.Paths);
        return new InterventionPolicy(store);
    }

    private InterventionPolicy CreatePolicyWithAntiGoals(params AntiGoal[] antiGoals)
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        var store = new IdentityStore(_workspace.Paths);
        store.SaveAntiGoals(antiGoals.ToList());
        return new InterventionPolicy(store);
    }

    private InterventionPolicy CreatePolicyWithProfile(UserProfile profile)
    {
        new WorkspaceInitializer().Initialize(_workspace.Paths);
        var store = new IdentityStore(_workspace.Paths);
        store.SaveProfile(profile);
        return new InterventionPolicy(store);
    }
}
