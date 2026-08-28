using System.Globalization;
using Dapper;
using Harness.DataAccess.Configuration;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.Agents;

internal sealed class SqliteAgentRoleDefaultStore(IApplicationPaths applicationPaths)
    : IAgentRoleDefaultStore
{
    public async ValueTask<IReadOnlyList<StoredAgentRoleDefault>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        IEnumerable<RoleDefaultRow> rows = await connection.QueryAsync<RoleDefaultRow>(
            new CommandDefinition("""
                SELECT role, provider, model,
                       reasoning_policy AS ReasoningPolicy,
                       updated_at AS UpdatedAt
                FROM agent_role_defaults
                ORDER BY CASE role WHEN 'Lead' THEN 0 WHEN 'Implementer' THEN 1 ELSE 2 END;
                """, cancellationToken: cancellationToken));
        return rows.Select(row => row.ToRecord()).ToArray();
    }

    public async ValueTask<StoredAgentRoleDefault> SaveAsync(
        StoredAgentRoleDefault value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        RoleDefaultRow row = await connection.QuerySingleAsync<RoleDefaultRow>(
            new CommandDefinition("""
                INSERT INTO agent_role_defaults (
                    role, provider, model, reasoning_policy, updated_at)
                VALUES (@Role, @Provider, @Model, @ReasoningPolicy, @UpdatedAt)
                ON CONFLICT (role) DO UPDATE SET
                    provider = excluded.provider,
                    model = excluded.model,
                    reasoning_policy = excluded.reasoning_policy,
                    updated_at = excluded.updated_at
                RETURNING role, provider, model,
                          reasoning_policy AS ReasoningPolicy,
                          updated_at AS UpdatedAt;
                """, new
            {
                Role = value.Role.ToString(),
                Provider = value.Provider.Value,
                Model = value.Model.Value,
                ReasoningPolicy = value.ReasoningPolicy.ToString(),
                UpdatedAt = value.UpdatedAt.ToString("O", CultureInfo.InvariantCulture),
            }, cancellationToken: cancellationToken));
        return row.ToRecord();
    }

    private async ValueTask<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = applicationPaths.Current.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private sealed class RoleDefaultRow
    {
        public string Role { get; init; } = string.Empty;

        public string Provider { get; init; } = string.Empty;

        public string Model { get; init; } = string.Empty;

        public string ReasoningPolicy { get; init; } = string.Empty;

        public string UpdatedAt { get; init; } = string.Empty;

        internal StoredAgentRoleDefault ToRecord() => new(
            Enum.Parse<AgentDefaultRole>(Role),
            new(Provider),
            new(Model),
            Enum.Parse<AgentDefaultReasoningPolicy>(ReasoningPolicy),
            DateTimeOffset.Parse(UpdatedAt, CultureInfo.InvariantCulture));
    }
}
