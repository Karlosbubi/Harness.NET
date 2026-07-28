using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Goals;

namespace Harness.Presentation.Terminal.Tests;

public sealed class GoalTextFormatterTests
{
    [Fact]
    public void Formats_goal_plan_cap_and_attributed_costs()
    {
        DateTimeOffset createdAt = DateTimeOffset.Parse("2026-07-28T10:00:00Z");
        GoalView goal = Goal(remoteBudget: new(2_000_000));
        PlanView plan = new(
            new("plan-1"),
            goal.Id,
            new(2),
            "Implement and verify.",
            PlanState.Pending,
            createdAt,
            createdAt);
        RemoteCostReport report = new(
            goal.Id,
            new(2_000_000),
            new(250_000),
            new(500_000),
            new(1_250_000),
            new(0),
            [new(
                "reservation-1",
                "OpenRouter",
                "provider/model",
                RemoteCostKind.Chat,
                new(600_000),
                new(500_000),
                RemoteCostState.Reconciled,
                createdAt,
                createdAt)]);

        string text = GoalTextFormatter.FormatDetails(goal, plan, report);

        Assert.Contains("Review-cycle limit: 3", text, StringComparison.Ordinal);
        Assert.Contains("PLAN revision 2 | Pending", text, StringComparison.Ordinal);
        Assert.Contains("Cap:        $2", text, StringComparison.Ordinal);
        Assert.Contains("Reserved:   $0.25", text, StringComparison.Ordinal);
        Assert.Contains("Reconciled: $0.5", text, StringComparison.Ordinal);
        Assert.Contains("Remaining:  $1.25", text, StringComparison.Ordinal);
        Assert.Contains("OpenRouter/provider/model", text, StringComparison.Ordinal);
        Assert.Contains("actual $0.5", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Makes_local_only_authorization_explicit()
    {
        string text = GoalTextFormatter.FormatDetails(Goal(remoteBudget: null), plan: null, cost: null);

        Assert.Contains("Remote models: not authorized", text, StringComparison.Ordinal);
        Assert.Contains("no remote-model spend is permitted", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0.000001", 1)]
    [InlineData("0.0000001", 1)]
    [InlineData("1.25", 1_250_000)]
    public void Parses_usd_caps_conservatively(string value, long expectedMicroUsd)
    {
        bool parsed = GoalTextFormatter.TryParseUsd(value, out long microUsd);

        Assert.True(parsed);
        Assert.Equal(expectedMicroUsd, microUsd);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("not money")]
    public void Rejects_invalid_usd_caps(string value) =>
        Assert.False(GoalTextFormatter.TryParseUsd(value, out _));

    private static GoalView Goal(MicroUsdAmount? remoteBudget) => new(
        new("goal-1"),
        "workspace-1",
        "Cost-transparent goal",
        "Keep remote inference bounded and visible.",
        new(3),
        remoteBudget,
        GoalState.AwaitingPlanApproval,
        DateTimeOffset.Parse("2026-07-28T10:00:00Z"),
        DateTimeOffset.Parse("2026-07-28T10:00:00Z"));
}
