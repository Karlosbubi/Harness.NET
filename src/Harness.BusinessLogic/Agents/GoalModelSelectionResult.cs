namespace Harness.BusinessLogic.Agents;

public sealed record GoalModelSelectionResult(
    GoalModelSelectionView? Selection,
    string? ErrorCode,
    string? Error);
