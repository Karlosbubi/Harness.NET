namespace Harness.BusinessLogic.Goals;

public sealed record PlanView(
    PlanId Id,
    GoalId GoalId,
    PlanRevision Revision,
    string Content,
    PlanState State,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
