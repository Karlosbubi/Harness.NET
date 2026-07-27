namespace Harness.BusinessLogic.Goals;

public sealed record PlanProposalRequest(
    string GoalId,
    string Content);
