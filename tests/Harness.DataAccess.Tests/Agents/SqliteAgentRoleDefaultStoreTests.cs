using Harness.DataAccess.Agents;
using Harness.DataAccess.Configuration;
using Harness.DataAccess.Persistence;

namespace Harness.DataAccess.Tests.Agents;

public sealed class SqliteAgentRoleDefaultStoreTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "harness-agent-default-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Persists_one_replaceable_default_per_role()
    {
        StubApplicationPaths paths = new(CreatePaths());
        await new SqliteDatabaseInitializer(paths).InitializeAsync();
        SqliteAgentRoleDefaultStore store = new(paths);
        DateTimeOffset firstAt = DateTimeOffset.Parse("2026-07-29T10:00:00Z");
        DateTimeOffset secondAt = firstAt.AddMinutes(1);

        await store.SaveAsync(new(
            AgentDefaultRole.Lead,
            new("Ollama"),
            new("local"),
            new(2048),
            firstAt));
        StoredAgentRoleDefault replaced = await store.SaveAsync(new(
            AgentDefaultRole.Lead,
            new("OpenRouter"),
            new("remote"),
            new(4096),
            secondAt));
        await store.SaveAsync(new(
            AgentDefaultRole.Reviewer,
            new("Ollama"),
            new("review"),
            new(1024),
            secondAt));
        IReadOnlyList<StoredAgentRoleDefault> values = await store.ListAsync();

        Assert.Equal("OpenRouter", replaced.Provider.Value);
        Assert.Equal(4096, replaced.MaximumOutputTokens.Value);
        Assert.Equal(secondAt, replaced.UpdatedAt);
        Assert.Equal(
            [AgentDefaultRole.Lead, AgentDefaultRole.Reviewer],
            values.Select(item => item.Role).ToArray());
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private ApplicationPaths CreatePaths() => new(
        Path.Combine(root, "config"),
        Path.Combine(root, "data"),
        Path.Combine(root, "state"),
        Path.Combine(root, "cache"),
        Path.Combine(root, "data", "harness.db"),
        Path.Combine(root, "state", "logs"),
        Path.Combine(root, "state", "worktrees"));

    private sealed class StubApplicationPaths(ApplicationPaths current) : IApplicationPaths
    {
        public ApplicationPaths Current { get; } = current;
    }
}
