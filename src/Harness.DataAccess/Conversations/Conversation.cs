namespace Harness.DataAccess.Conversations;

public sealed record Conversation(
    string Id,
    string Title,
    string Model,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
