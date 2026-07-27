using System.Globalization;
using Dapper;
using Harness.DataAccess.Configuration;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.Workspaces;

internal sealed class SqliteWorkspaceStore(IApplicationPaths applicationPaths) : IWorkspaceStore
{
    public async ValueTask<RegisteredWorkspace> SaveAsync(
        WorkspaceInspection inspection,
        string entryPoint,
        CancellationToken cancellationToken = default)
    {
        string now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        string id = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(inspection.RootPath)))[..16];
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        DynamicParameters parameters = new();
        parameters.Add("id", id);
        parameters.Add("rootPath", inspection.RootPath);
        parameters.Add("name", inspection.Name);
        parameters.Add("entryPoint", entryPoint);
        parameters.Add("branch", inspection.Branch);
        parameters.Add("isDirty", inspection.IsDirty);
        parameters.Add("now", now);
        CommandDefinition command = new("""
            INSERT INTO workspaces (
                id, root_path, name, entry_point, is_trusted, branch, is_dirty, created_at, updated_at)
            VALUES (
                @id, @rootPath, @name, @entryPoint, 0, @branch, @isDirty, @now, @now)
            ON CONFLICT(root_path) DO UPDATE SET
                name = excluded.name,
                entry_point = excluded.entry_point,
                branch = excluded.branch,
                is_dirty = excluded.is_dirty,
                updated_at = excluded.updated_at
            RETURNING id, root_path AS RootPath, name, entry_point AS EntryPoint,
                      is_trusted AS IsTrusted, is_active AS IsActive,
                      branch, is_dirty AS IsDirty,
                      created_at AS CreatedAt, updated_at AS UpdatedAt;
            """, parameters, cancellationToken: cancellationToken);
        WorkspaceRow row = await connection.QuerySingleAsync<WorkspaceRow>(command);
        return row.ToRecord();
    }

    public async ValueTask<RegisteredWorkspace?> FindByPathAsync(
        string rootPath,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        CommandDefinition command = new(SelectSql + " WHERE root_path = @rootPath;",
            new { rootPath = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar) },
            cancellationToken: cancellationToken);
        WorkspaceRow? row = await connection.QuerySingleOrDefaultAsync<WorkspaceRow>(command);
        return row?.ToRecord();
    }

    public async ValueTask<IReadOnlyList<RegisteredWorkspace>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        CommandDefinition command = new(SelectSql + " ORDER BY updated_at DESC;",
            cancellationToken: cancellationToken);
        IEnumerable<WorkspaceRow> rows = await connection.QueryAsync<WorkspaceRow>(command);
        return rows.Select(row => row.ToRecord()).ToArray();
    }

    public async ValueTask<RegisteredWorkspace?> GetActiveAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        CommandDefinition command = new(SelectSql + " WHERE is_active = 1;",
            cancellationToken: cancellationToken);
        WorkspaceRow? row = await connection.QuerySingleOrDefaultAsync<WorkspaceRow>(command);
        return row?.ToRecord();
    }

    public async ValueTask<RegisteredWorkspace> SetActiveAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        string now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE workspaces SET is_active = 0 WHERE is_active = 1;",
            transaction: transaction,
            cancellationToken: cancellationToken));
        WorkspaceRow row = await connection.QuerySingleAsync<WorkspaceRow>(new CommandDefinition("""
            UPDATE workspaces
            SET is_active = 1, updated_at = @now
            WHERE id = @workspaceId
            RETURNING id, root_path AS RootPath, name, entry_point AS EntryPoint,
                      is_trusted AS IsTrusted, is_active AS IsActive,
                      branch, is_dirty AS IsDirty,
                      created_at AS CreatedAt, updated_at AS UpdatedAt;
            """, new { workspaceId, now }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return row.ToRecord();
    }

    public async ValueTask<RegisteredWorkspace> SetTrustAsync(
        string workspaceId,
        bool isTrusted,
        CancellationToken cancellationToken = default)
    {
        string now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        CommandDefinition command = new("""
            UPDATE workspaces
            SET is_trusted = @isTrusted, updated_at = @now
            WHERE id = @workspaceId
            RETURNING id, root_path AS RootPath, name, entry_point AS EntryPoint,
                      is_trusted AS IsTrusted, is_active AS IsActive,
                      branch, is_dirty AS IsDirty,
                      created_at AS CreatedAt, updated_at AS UpdatedAt;
            """, new { workspaceId, isTrusted, now }, cancellationToken: cancellationToken);
        WorkspaceRow row = await connection.QuerySingleAsync<WorkspaceRow>(command);
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

    private const string SelectSql = """
        SELECT id, root_path AS RootPath, name, entry_point AS EntryPoint,
               is_trusted AS IsTrusted, is_active AS IsActive,
               branch, is_dirty AS IsDirty,
               created_at AS CreatedAt, updated_at AS UpdatedAt
        FROM workspaces
        """;

    private sealed class WorkspaceRow
    {
        public string Id { get; init; } = string.Empty;
        public string RootPath { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string EntryPoint { get; init; } = string.Empty;
        public bool IsTrusted { get; init; }
        public bool IsActive { get; init; }
        public string Branch { get; init; } = string.Empty;
        public bool IsDirty { get; init; }
        public string CreatedAt { get; init; } = string.Empty;
        public string UpdatedAt { get; init; } = string.Empty;

        internal RegisteredWorkspace ToRecord() => new(
            Id,
            RootPath,
            Name,
            EntryPoint,
            IsTrusted,
            IsActive,
            Branch,
            IsDirty,
            DateTimeOffset.Parse(CreatedAt, CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(UpdatedAt, CultureInfo.InvariantCulture));
    }
}
