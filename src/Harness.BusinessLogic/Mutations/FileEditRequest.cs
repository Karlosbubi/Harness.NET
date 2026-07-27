namespace Harness.BusinessLogic.Mutations;

public sealed record FileEditRequest(
    string GoalId,
    string CorrelationId,
    string Path,
    string? ExpectedSha256,
    string Content);
