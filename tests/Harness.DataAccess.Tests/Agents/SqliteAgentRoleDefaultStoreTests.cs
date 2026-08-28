using Harness.DataAccess.Agents;
using Harness.DataAccess.Configuration;
using Harness.DataAccess.Persistence;
using Microsoft.Data.Sqlite;

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
            AgentDefaultReasoningPolicy.Disabled,
            firstAt));
        StoredAgentRoleDefault replaced = await store.SaveAsync(new(
            AgentDefaultRole.Lead,
            new("OpenRouter"),
            new("remote"),
            AgentDefaultReasoningPolicy.ProviderDefault,
            secondAt));
        await store.SaveAsync(new(
            AgentDefaultRole.Reviewer,
            new("Ollama"),
            new("review"),
            AgentDefaultReasoningPolicy.Disabled,
            secondAt));
        IReadOnlyList<StoredAgentRoleDefault> values = await store.ListAsync();

        Assert.Equal("OpenRouter", replaced.Provider.Value);
        Assert.Equal(AgentDefaultReasoningPolicy.ProviderDefault, replaced.ReasoningPolicy);
        Assert.Equal(secondAt, replaced.UpdatedAt);
        Assert.Equal(
            [AgentDefaultRole.Lead, AgentDefaultRole.Reviewer],
            values.Select(item => item.Role).ToArray());
    }

    [Fact]
    public async Task Migration_preserves_existing_routes_with_provider_default_reasoning()
    {
        StubApplicationPaths paths = new(CreatePaths());
        await new SqliteDatabaseInitializer(paths).InitializeAsync();
        await using (SqliteConnection connection = new($"Data Source={paths.Current.DatabasePath}"))
        {
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                DROP TABLE agent_role_defaults;
                CREATE TABLE agent_role_defaults (
                    role TEXT PRIMARY KEY CHECK (role IN ('Lead', 'Implementer', 'Reviewer')),
                    provider TEXT NOT NULL,
                    model TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                ) STRICT;
                INSERT INTO agent_role_defaults (role, provider, model, updated_at)
                VALUES ('Lead', 'Ollama', 'existing', '2026-08-28T00:00:00Z');
                DELETE FROM SchemaVersions
                WHERE ScriptName LIKE '%031_AgentReasoningPolicy.sql';
                UPDATE application_metadata SET value = '30' WHERE key = 'schema_version';
                """;
            await command.ExecuteNonQueryAsync();
        }

        DatabaseInitializationResult migrated =
            await new SqliteDatabaseInitializer(paths).InitializeAsync();
        StoredAgentRoleDefault value = Assert.Single(
            await new SqliteAgentRoleDefaultStore(paths).ListAsync());

        Assert.Equal(31, migrated.SchemaVersion.Value);
        Assert.Equal(AgentDefaultReasoningPolicy.ProviderDefault, value.ReasoningPolicy);
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
