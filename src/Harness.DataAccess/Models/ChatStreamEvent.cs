namespace Harness.DataAccess.Models;

public sealed record ChatStreamEvent(
    string Content,
    string Thinking,
    bool Done,
    string? DoneReason,
    ProviderUsage Usage,
    ProviderError? Error,
    IReadOnlyList<ChatToolCall>? ToolCalls = null,
    ChatReasoningDetailsJson? ReasoningDetails = null);
