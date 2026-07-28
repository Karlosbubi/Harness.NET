namespace Harness.BusinessLogic.Goals;

public sealed record PlanProposalRequest(
    GoalId GoalId,
    string Content);
