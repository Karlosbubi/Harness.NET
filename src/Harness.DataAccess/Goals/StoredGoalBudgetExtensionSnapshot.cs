namespace Harness.DataAccess.Goals;

public sealed record StoredGoalBudgetExtensionSnapshot(
    StoredGoal Goal,
    StoredGoalBudgetExtension Extension);
