namespace Harness.BusinessLogic.Goals;

public sealed record PlanDecisionRequest(
    string GoalId,
    string PlanId,
    string Decision,
    string? Reason);
