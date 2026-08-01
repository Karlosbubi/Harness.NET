using Harness.BusinessLogic.Tools;

namespace Harness.BusinessLogic.Mutations;

public enum FileEditOrigin
{
    Human,
    Model,
}

public sealed record FileEditRequest(
    string GoalId,
    ToolCorrelationId CorrelationId,
    string Path,
    string? ExpectedSha256,
    string Content,
    FileEditOrigin Origin = FileEditOrigin.Human);
