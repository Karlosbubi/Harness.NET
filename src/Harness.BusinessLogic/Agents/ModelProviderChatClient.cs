using System.Runtime.CompilerServices;
using Harness.DataAccess.Models;
using Microsoft.Extensions.AI;
using ProviderChatMessage = Harness.DataAccess.Models.ChatMessage;

namespace Harness.BusinessLogic.Agents;

internal sealed class ModelProviderChatClient(
    IModelProvider provider,
    AgentModel model) : IChatClient
{
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        List<ChatResponseUpdate> updates = [];
        await foreach (ChatResponseUpdate update in GetStreamingResponseAsync(
            messages,
            options,
            cancellationToken))
        {
            updates.Add(update);
        }

        string content = string.Concat(updates.Select(update => update.Text));
        return new ChatResponse(new Microsoft.Extensions.AI.ChatMessage(
            ChatRole.Assistant,
            content));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        List<ProviderChatMessage> providerMessages = [];
        if (!string.IsNullOrWhiteSpace(options?.Instructions))
        {
            providerMessages.Add(new("system", options.Instructions));
        }

        providerMessages.AddRange(messages.Select(message => new ProviderChatMessage(
            message.Role.Value,
            message.Text)));

        await foreach (ChatStreamEvent item in provider.StreamChatAsync(
            new ChatRequest(model.Value, providerMessages),
            cancellationToken))
        {
            if (item.Error is not null)
            {
                throw new InvalidOperationException(
                    $"Model provider error '{item.Error.Code}': {item.Error.Message}");
            }

            if (!string.IsNullOrEmpty(item.Content))
            {
                yield return new(ChatRole.Assistant, item.Content);
            }
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose()
    {
    }
}
