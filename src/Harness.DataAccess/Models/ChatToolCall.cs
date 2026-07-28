namespace Harness.DataAccess.Models;

public sealed record ChatToolCall(
    ChatToolCallId Id,
    ChatToolName Name,
    ChatToolArgumentsJson Arguments);
