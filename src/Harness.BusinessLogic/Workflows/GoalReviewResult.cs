namespace Harness.BusinessLogic.Workflows;

internal sealed record GoalReviewResult(
    GoalReviewDecision? Decision,
    string? Summary,
    string? Error);
