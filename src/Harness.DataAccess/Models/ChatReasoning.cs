namespace Harness.DataAccess.Models;

public sealed record ChatReasoning(
    ChatReasoningText Text,
    ChatReasoningDetailsJson? Details = null);
