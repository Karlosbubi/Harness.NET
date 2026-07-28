namespace Harness.BusinessLogic.Goals;

public sealed record PlanDecisionRequest(
    GoalId GoalId,
    PlanId PlanId,
    PlanDecision Decision,
    string? Reason);
