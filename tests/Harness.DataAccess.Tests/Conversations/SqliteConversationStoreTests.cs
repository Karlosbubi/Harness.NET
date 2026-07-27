using Harness.DataAccess.Configuration;
using Harness.DataAccess.Conversations;
using Harness.DataAccess.Models;
using Harness.DataAccess.Persistence;

namespace Harness.DataAccess.Tests.Conversations;

public sealed class SqliteConversationStoreTests : IDisposable
{
    private readonly string testDirectory = Path.Combine(
        Path.GetTempPath(),
        "harness-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Creates_conversation_and_round_trips_ordered_messages()
    {
        ApplicationPaths paths = CreatePaths();
        StubApplicationPaths applicationPaths = new(paths);
        await new SqliteDatabaseInitializer(applicationPaths).InitializeAsync();
        SqliteConversationStore store = new(applicationPaths);

        Conversation first = await store.GetOrCreateAsync(
            "default",
            "Local conversation",
            "gemma4:latest");
        Conversation second = await store.GetOrCreateAsync(
            "default",
            "Ignored replacement",
            "other-model");
        await store.AppendMessageAsync(
            first.Id,
            "user",
            "Inspect this repository",
            "Complete",
            new(0, 0));
        await store.AppendMessageAsync(
            first.Id,
            "assistant",
            "I will inspect it first.",
            "Complete",
            new(12, 6));
        Conversation updated = await store.UpdateModelAsync(first.Id, "selected-model");

        IReadOnlyList<ConversationMessage> messages = await store.GetMessagesAsync(first.Id);

        Assert.Equal("Local conversation", second.Title);
        Assert.Equal("gemma4:latest", second.Model);
        Assert.Equal(["user", "assistant"], messages.Select(message => message.Role));
        Assert.Equal(12, messages[1].InputTokens);
        Assert.Equal(6, messages[1].OutputTokens);
        Assert.Equal("selected-model", updated.Model);
    }

    public void Dispose()
    {
        if (Directory.Exists(testDirectory))
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    private ApplicationPaths CreatePaths() => new(
        Path.Combine(testDirectory, "config"),
        Path.Combine(testDirectory, "data"),
        Path.Combine(testDirectory, "state"),
        Path.Combine(testDirectory, "cache"),
        Path.Combine(testDirectory, "data", "harness.db"),
        Path.Combine(testDirectory, "state", "logs"),
        Path.Combine(testDirectory, "state", "worktrees"));

    private sealed class StubApplicationPaths(ApplicationPaths current) : IApplicationPaths
    {
        public ApplicationPaths Current { get; } = current;
    }
}
