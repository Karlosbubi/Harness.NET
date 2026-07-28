using System.Runtime.CompilerServices;
using System.Text;
using Harness.BusinessLogic.Workspaces;
using Harness.DataAccess.Conversations;
using Harness.DataAccess.Models;

namespace Harness.BusinessLogic.Dashboard;

internal sealed class ConversationDashboardService(
    IConversationStore conversationStore,
    IModelProvider modelProvider,
    IWorkspaceService workspaceService,
    ConversationOptions options) : IDashboardService
{
    private ProviderSnapshot providerSnapshot = new(
        "Ollama",
        "Not checked",
        options.Model,
        [],
        Error: null);

    public async ValueTask<DashboardSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        Conversation conversation = await conversationStore.GetOrCreateAsync(
            options.ConversationId,
            options.Title,
            options.Model,
            cancellationToken);
        IReadOnlyList<ConversationMessage> messages = await conversationStore
            .GetMessagesAsync(conversation.Id, cancellationToken);
        WorkspaceSummary workspace = await GetWorkspaceSummaryAsync(cancellationToken);
        return CreateSnapshot(conversation, messages, workspace, transient: null, "Ready");
    }

    public async ValueTask<DashboardSnapshot> RefreshProviderAsync(
        CancellationToken cancellationToken = default)
    {
        Conversation conversation = await GetConversationAsync(cancellationToken);
        await DiscoverModelsAsync(conversation.Model, cancellationToken);
        IReadOnlyList<ConversationMessage> messages = await conversationStore
            .GetMessagesAsync(conversation.Id, cancellationToken);
        WorkspaceSummary workspace = await GetWorkspaceSummaryAsync(cancellationToken);
        string status = providerSnapshot.Error is null
            ? $"Provider ready: {providerSnapshot.Models.Count} model(s)"
            : $"Provider unavailable: {providerSnapshot.Error}";
        return CreateSnapshot(conversation, messages, workspace, transient: null, status);
    }

    public async ValueTask<DashboardSnapshot> SelectModelAsync(
        string model,
        CancellationToken cancellationToken = default)
    {
        Conversation conversation = await GetConversationAsync(cancellationToken);
        if (providerSnapshot.Models.Count == 0)
        {
            await DiscoverModelsAsync(conversation.Model, cancellationToken);
        }

        ProviderModel? selected = providerSnapshot.Models.FirstOrDefault(
            available => string.Equals(available.Id, model, StringComparison.Ordinal));
        IReadOnlyList<ConversationMessage> messages = await conversationStore
            .GetMessagesAsync(conversation.Id, cancellationToken);
        WorkspaceSummary workspace = await GetWorkspaceSummaryAsync(cancellationToken);
        if (selected is null)
        {
            return CreateSnapshot(
                conversation,
                messages,
                workspace,
                transient: null,
                $"Model unavailable: {model}");
        }

        conversation = await conversationStore.UpdateModelAsync(
            conversation.Id,
            selected.Id,
            cancellationToken);
        providerSnapshot = providerSnapshot with { SelectedModel = selected.Id };
        return CreateSnapshot(
            conversation,
            messages,
            workspace,
            transient: null,
            $"Selected model: {selected.Id}");
    }

    public async IAsyncEnumerable<DashboardSnapshot> SubmitAsync(
        string instruction,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instruction))
        {
            yield break;
        }

        Conversation conversation = await conversationStore.GetOrCreateAsync(
            options.ConversationId,
            options.Title,
            options.Model,
            cancellationToken);
        WorkspaceSummary workspace = await GetWorkspaceSummaryAsync(cancellationToken);
        await conversationStore.AppendMessageAsync(
            conversation.Id,
            "user",
            instruction.Trim(),
            "Complete",
            new Harness.DataAccess.Models.ProviderUsage(0, 0),
            cancellationToken);
        IReadOnlyList<ConversationMessage> messages = await conversationStore
            .GetMessagesAsync(conversation.Id, cancellationToken);

        yield return CreateSnapshot(
            conversation,
            messages,
            workspace,
            transient: null,
            "Waiting for model");

        ChatRequest request = new(
            conversation.Model,
            messages
                .Where(message => message.Status == "Complete")
                .Select(message => new ChatMessage(
                    message.Role switch
                    {
                        "system" => ChatRole.System,
                        "user" => ChatRole.User,
                        "assistant" => ChatRole.Assistant,
                        "tool" => ChatRole.Tool,
                        _ => throw new InvalidOperationException(
                            $"Unsupported persisted chat role '{message.Role}'."),
                    },
                    message.Content))
                .ToArray());
        StringBuilder content = new();
        StringBuilder thinking = new();
        Harness.DataAccess.Models.ProviderUsage usage = new(0, 0);
        ProviderError? error = null;
        bool completed = false;

        await foreach (ChatStreamEvent chunk in modelProvider
                           .StreamChatAsync(request, cancellationToken)
                           .WithCancellation(cancellationToken))
        {
            content.Append(chunk.Content);
            thinking.Append(chunk.Thinking);
            if (chunk.Usage.InputTokens > 0 || chunk.Usage.OutputTokens > 0)
            {
                usage = chunk.Usage;
            }

            error = chunk.Error;
            completed = chunk.Done;
            string state = error is not null
                ? "Provider failed"
                : chunk.Done ? "Completing" : "Streaming";
            yield return CreateSnapshot(
                conversation,
                messages,
                workspace,
                CreateTransientActivity(content, thinking, error),
                state,
                usage);

            if (error is not null)
            {
                break;
            }
        }

        if (!completed && error is null)
        {
            error = new(
                "incomplete_stream",
                "The provider stream ended before a completion event.",
                IsTransient: true);
        }

        string persistedContent = content.Length > 0
            ? content.ToString()
            : error?.Message ?? "The model returned no content.";
        await conversationStore.AppendMessageAsync(
            conversation.Id,
            "assistant",
            persistedContent,
            error is null ? "Complete" : "Failed",
            new Harness.DataAccess.Models.ProviderUsage(
                usage.InputTokens,
                usage.OutputTokens),
            cancellationToken);
        messages = await conversationStore.GetMessagesAsync(conversation.Id, cancellationToken);

        yield return CreateSnapshot(
            conversation,
            messages,
            workspace,
            transient: null,
            error is null ? "Ready" : $"Provider error: {error.Code}");
    }

    private DashboardSnapshot CreateSnapshot(
        Conversation conversation,
        IReadOnlyList<ConversationMessage> messages,
        WorkspaceSummary workspace,
        ActivityItem? transient,
        string status,
        Harness.DataAccess.Models.ProviderUsage? currentUsage = null)
    {
        ActivityItem[] persistedActivities = messages
            .Select(message => new ActivityItem(
                ActorName(message.Role),
                message.Content,
                message.Status))
            .ToArray();
        IReadOnlyList<ActivityItem> activities = transient is null
            ? persistedActivities
            : [.. persistedActivities, transient];
        int inputTokens = messages.Sum(message => message.InputTokens) +
                          (currentUsage?.InputTokens ?? 0);
        int outputTokens = messages.Sum(message => message.OutputTokens) +
                           (currentUsage?.OutputTokens ?? 0);

        return new(
            workspace,
            conversation.Title,
            activities,
            [$"Model: {conversation.Model}", $"Messages: {messages.Count}"],
            "No repository changes",
            [new("Conversation", $"Persisted as {conversation.Id}")],
            providerSnapshot with { SelectedModel = conversation.Model },
            status,
            $"Local model | {inputTokens} input | {outputTokens} output tokens");
    }

    private async ValueTask<WorkspaceSummary> GetWorkspaceSummaryAsync(
        CancellationToken cancellationToken)
    {
        WorkspaceView? active = await workspaceService.GetActiveAsync(cancellationToken);
        return active is null
            ? new(
                Path.GetFileName(options.WorkspacePath.TrimEnd(Path.DirectorySeparatorChar)),
                options.WorkspacePath,
                "unregistered",
                "Not trusted")
            : new(
                active.Name,
                active.RootPath,
                active.Branch + (active.IsDirty ? " (dirty)" : ""),
                active.IsTrusted ? "Trusted" : "Not trusted");
    }

    private ValueTask<Conversation> GetConversationAsync(CancellationToken cancellationToken) =>
        conversationStore.GetOrCreateAsync(
            options.ConversationId,
            options.Title,
            options.Model,
            cancellationToken);

    private async ValueTask DiscoverModelsAsync(
        string selectedModel,
        CancellationToken cancellationToken)
    {
        ModelCatalog catalog = await modelProvider.GetModelsAsync(cancellationToken);
        ProviderModel[] models = catalog.Models
            .Select(model => new ProviderModel(
                model.Id,
                model.Family,
                model.ParameterSize,
                model.Quantization,
                model.Capabilities))
            .ToArray();
        string health = catalog.Error is not null
            ? "Unavailable"
            : models.Any(model => model.Id == selectedModel) ? "Ready" : "Model unavailable";
        providerSnapshot = new(
            catalog.Models.FirstOrDefault()?.Provider ?? "Ollama",
            health,
            selectedModel,
            models,
            catalog.Error?.Message);
    }

    private static ActivityItem CreateTransientActivity(
        StringBuilder content,
        StringBuilder thinking,
        ProviderError? error)
    {
        string text = content.Length > 0
            ? content.ToString()
            : thinking.Length > 0 ? thinking.ToString() : error?.Message ?? string.Empty;
        string status = error is not null
            ? "Failed"
            : content.Length > 0 ? "Streaming" : "Thinking";
        return new("Assistant", text, status);
    }

    private static string ActorName(string role) => role switch
    {
        "user" => "You",
        "assistant" => "Assistant",
        "system" => "System",
        _ => role,
    };
}
