namespace Harness.BusinessLogic.Goals;

public sealed record PlanView(
    string Id,
    string GoalId,
    int Revision,
    string Content,
    string State,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
