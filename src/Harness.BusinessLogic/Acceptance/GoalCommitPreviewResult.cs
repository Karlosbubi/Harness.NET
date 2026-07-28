namespace Harness.BusinessLogic.Acceptance;

public sealed record GoalCommitPreviewResult(
    GoalCommitPreview? Preview,
    string? ErrorCode,
    string? Error);
