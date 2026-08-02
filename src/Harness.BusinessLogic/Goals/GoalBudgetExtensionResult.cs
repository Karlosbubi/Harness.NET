namespace Harness.BusinessLogic.Goals;

public sealed record GoalBudgetExtensionResult(
    GoalView? Goal,
    GoalBudgetExtensionView? Extension,
    string? ErrorCode,
    string? Error);
