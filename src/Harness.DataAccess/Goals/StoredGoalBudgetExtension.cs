namespace Harness.DataAccess.Goals;

public sealed record StoredGoalBudgetExtension(
    string Id,
    string GoalId,
    long? PreviousBudgetMicrousd,
    long NewBudgetMicrousd,
    string Reason,
    DateTimeOffset ApprovedAt);
