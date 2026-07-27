using Harness.BusinessLogic.Tools;

namespace Harness.BusinessLogic.Mutations;

public sealed record FileEditView(
    string GoalId,
    ToolCorrelationId CorrelationId,
    string Path,
    string? PreviousSha256,
    string? NewSha256,
    int BytesWritten,
    bool WasCreated,
    string? ErrorCode,
    string? Error);
