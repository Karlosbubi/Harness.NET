namespace Harness.DataAccess.Evidence;

public sealed record StoredToolCall(
    ToolCallId Id,
    string GoalId,
    ToolCorrelationId CorrelationId,
    ToolKind Tool,
    string RequestJson,
    ToolCallState State,
    string? ResultJson,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);
