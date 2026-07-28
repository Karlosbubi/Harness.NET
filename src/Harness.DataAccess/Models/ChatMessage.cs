namespace Harness.DataAccess.Models;

public sealed record ChatMessage(
    ChatRole Role,
    string Content,
    IReadOnlyList<ChatToolCall>? ToolCalls = null,
    ChatToolResult? ToolResult = null);
