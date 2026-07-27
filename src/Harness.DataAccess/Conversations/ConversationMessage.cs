namespace Harness.DataAccess.Conversations;

public sealed record ConversationMessage(
    long Id,
    string ConversationId,
    string Role,
    string Content,
    string Status,
    int InputTokens,
    int OutputTokens,
    DateTimeOffset CreatedAt);
