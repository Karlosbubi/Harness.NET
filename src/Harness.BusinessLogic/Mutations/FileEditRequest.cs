using Harness.BusinessLogic.Tools;

namespace Harness.BusinessLogic.Mutations;

public sealed record FileEditRequest(
    string GoalId,
    ToolCorrelationId CorrelationId,
    string Path,
    string? ExpectedSha256,
    string Content);
