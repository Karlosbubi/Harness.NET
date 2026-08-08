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
                SELECT role, provider, model, updated_at AS UpdatedAt
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
                INSERT INTO agent_role_defaults (role, provider, model, updated_at)
                VALUES (@Role, @Provider, @Model, @UpdatedAt)
                ON CONFLICT (role) DO UPDATE SET
                    provider = excluded.provider,
                    model = excluded.model,
                    updated_at = excluded.updated_at
                RETURNING role, provider, model, updated_at AS UpdatedAt;
                """, new
            {
                Role = value.Role.ToString(),
                Provider = value.Provider.Value,
                Model = value.Model.Value,
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

        public string UpdatedAt { get; init; } = string.Empty;

        internal StoredAgentRoleDefault ToRecord() => new(
            Enum.Parse<AgentDefaultRole>(Role),
            new(Provider),
            new(Model),
            DateTimeOffset.Parse(UpdatedAt, CultureInfo.InvariantCulture));
    }
}
