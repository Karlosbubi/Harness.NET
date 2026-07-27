namespace Harness.BusinessLogic.Goals;

public sealed record GoalResult(
    GoalView? Goal,
    string? ErrorCode,
    string? Error);
