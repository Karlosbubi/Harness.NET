using Harness.DataAccess.Models;

namespace Harness.DataAccess.Conversations;

public interface IConversationStore
{
    ValueTask<Conversation> GetOrCreateAsync(
        string conversationId,
        string title,
        string model,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ConversationMessage>> GetMessagesAsync(
        string conversationId,
        CancellationToken cancellationToken = default);

    ValueTask<Conversation> UpdateModelAsync(
        string conversationId,
        string model,
        CancellationToken cancellationToken = default);

    ValueTask<ConversationMessage> AppendMessageAsync(
        string conversationId,
        string role,
        string content,
        string status,
        ProviderUsage usage,
        CancellationToken cancellationToken = default);
}
