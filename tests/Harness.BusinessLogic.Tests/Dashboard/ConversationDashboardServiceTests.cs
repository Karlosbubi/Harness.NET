using System.Runtime.CompilerServices;
using Harness.BusinessLogic.Dashboard;
using Harness.DataAccess.Conversations;
using Harness.DataAccess.Models;

namespace Harness.BusinessLogic.Tests.Dashboard;

public sealed class ConversationDashboardServiceTests
{
    [Fact]
    public async Task Persists_user_before_streaming_and_persists_completed_response()
    {
        FakeConversationStore store = new();
        FakeModelProvider provider = new(
            () => Assert.Equal("user", Assert.Single(store.Messages).Role),
            [
                new("Hel", string.Empty, false, null, new(0, 0), null),
                new("lo", string.Empty, true, "stop", new(9, 2), null),
            ]);
        ConversationDashboardService service = CreateService(store, provider);

        List<DashboardSnapshot> snapshots = [];
        await foreach (DashboardSnapshot snapshot in service.SubmitAsync("Say hello"))
        {
            snapshots.Add(snapshot);
        }

        Assert.Equal(["user", "assistant"], store.Messages.Select(message => message.Role));
        Assert.Equal("Hello", store.Messages[1].Content);
        Assert.Equal("Complete", store.Messages[1].Status);
        Assert.Equal(9, store.Messages[1].InputTokens);
        Assert.Equal("Say hello", Assert.Single(provider.LastRequest!.Messages).Content);
        Assert.Contains(snapshots, snapshot => snapshot.Status == "Streaming");
        Assert.Equal("Ready", snapshots[^1].Status);
        Assert.Equal("Hello", snapshots[^1].Activities[^1].Summary);
    }

    [Fact]
    public async Task Persists_provider_failure_as_failed_and_does_not_replay_it()
    {
        FakeConversationStore store = new();
        FakeModelProvider failingProvider = new(
            beforeStream: null,
            [new(string.Empty, string.Empty, true, "error", new(0, 0),
                new("transport_error", "server unavailable", true))]);
        ConversationDashboardService failingService = CreateService(store, failingProvider);

        DashboardSnapshot? final = null;
        await foreach (DashboardSnapshot snapshot in failingService.SubmitAsync("First"))
        {
            final = snapshot;
        }

        Assert.Equal("Failed", store.Messages[^1].Status);
        Assert.Equal("server unavailable", store.Messages[^1].Content);
        Assert.Equal("Provider error: transport_error", final?.Status);

        FakeModelProvider succeedingProvider = new(
            beforeStream: null,
            [new("ok", string.Empty, true, "stop", new(4, 1), null)]);
        ConversationDashboardService succeedingService = CreateService(store, succeedingProvider);
        await foreach (DashboardSnapshot _ in succeedingService.SubmitAsync("Second"))
        {
        }

        Assert.DoesNotContain(
            succeedingProvider.LastRequest!.Messages,
            message => message.Content == "server unavailable");
    }

    [Fact]
    public async Task Reloads_persisted_history_into_initial_snapshot()
    {
        FakeConversationStore store = new();
        await store.GetOrCreateAsync("default", "Conversation", "model");
        await store.AppendMessageAsync(
            "default", "user", "Persisted question", "Complete", new(0, 0));
        ConversationDashboardService service = CreateService(
            store,
            new FakeModelProvider(null, []));

        DashboardSnapshot snapshot = await service.GetSnapshotAsync();

        Assert.Equal("Persisted question", Assert.Single(snapshot.Activities).Summary);
    }

    [Fact]
    public async Task Marks_a_stream_without_completion_as_failed()
    {
        FakeConversationStore store = new();
        ConversationDashboardService service = CreateService(
            store,
            new FakeModelProvider(
                beforeStream: null,
                [new("partial", string.Empty, false, null, new(0, 1), null)]));

        DashboardSnapshot? final = null;
        await foreach (DashboardSnapshot snapshot in service.SubmitAsync("Start"))
        {
            final = snapshot;
        }

        Assert.Equal("Failed", store.Messages[^1].Status);
        Assert.Equal("partial", store.Messages[^1].Content);
        Assert.Equal("Provider error: incomplete_stream", final?.Status);
    }

    [Fact]
    public async Task Discovers_capabilities_and_persists_selected_model()
    {
        FakeConversationStore store = new();
        FakeModelProvider provider = new(
            beforeStream: null,
            events: [],
            new ModelCatalog(
                [
                    new("model", "Ollama", "gemma", "8B", "Q4", ["completion"]),
                    new("tool-model", "Ollama", "gemma", "8B", "Q4", ["completion", "tools"]),
                ],
                Error: null));
        ConversationDashboardService service = CreateService(store, provider);

        DashboardSnapshot refreshed = await service.RefreshProviderAsync();
        DashboardSnapshot selected = await service.SelectModelAsync("tool-model");
        DashboardSnapshot reloaded = await service.GetSnapshotAsync();

        Assert.Equal("Ready", refreshed.Provider.Health);
        Assert.Contains("tools", refreshed.Provider.Models[1].Capabilities);
        Assert.Equal("tool-model", selected.Provider.SelectedModel);
        Assert.Equal("tool-model", reloaded.Provider.SelectedModel);
    }

    private static ConversationDashboardService CreateService(
        IConversationStore store,
        IModelProvider provider) => new(
            store,
            provider,
            new("default", "Conversation", "model", "/workspace/sample"));

    private sealed class FakeConversationStore : IConversationStore
    {
        private Conversation? conversation;
        private long nextId;

        internal List<ConversationMessage> Messages { get; } = [];

        public ValueTask<Conversation> GetOrCreateAsync(
            string conversationId,
            string title,
            string model,
            CancellationToken cancellationToken = default)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            conversation ??= new(conversationId, title, model, now, now);
            return ValueTask.FromResult(conversation);
        }

        public ValueTask<IReadOnlyList<ConversationMessage>> GetMessagesAsync(
            string conversationId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ConversationMessage>>(Messages.ToArray());

        public ValueTask<Conversation> UpdateModelAsync(
            string conversationId,
            string model,
            CancellationToken cancellationToken = default)
        {
            conversation = (conversation ?? throw new InvalidOperationException()) with
            {
                Model = model,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            return ValueTask.FromResult(conversation);
        }

        public ValueTask<ConversationMessage> AppendMessageAsync(
            string conversationId,
            string role,
            string content,
            string status,
            ProviderUsage usage,
            CancellationToken cancellationToken = default)
        {
            ConversationMessage message = new(
                ++nextId,
                conversationId,
                role,
                content,
                status,
                usage.InputTokens,
                usage.OutputTokens,
                DateTimeOffset.UtcNow);
            Messages.Add(message);
            return ValueTask.FromResult(message);
        }
    }

    private sealed class FakeModelProvider(
        Action? beforeStream,
        IReadOnlyList<ChatStreamEvent> events,
        ModelCatalog? catalog = null) : IModelProvider
    {
        internal ChatRequest? LastRequest { get; private set; }

        public ValueTask<ModelCatalog> GetModelsAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(catalog ?? new ModelCatalog([], null));

        public async IAsyncEnumerable<ChatStreamEvent> StreamChatAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            beforeStream?.Invoke();
            foreach (ChatStreamEvent item in events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
                await Task.Yield();
            }
        }

        public ValueTask<EmbeddingResult> EmbedAsync(
            EmbeddingRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new EmbeddingResult([], new(0, 0), null));
    }
}
