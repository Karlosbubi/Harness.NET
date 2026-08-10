namespace Harness.DataAccess.Models;

public sealed record ChatToolResult(
    ChatToolCallId CallId,
    ChatToolResultJson Result,
    ChatToolName? ToolName = null);
