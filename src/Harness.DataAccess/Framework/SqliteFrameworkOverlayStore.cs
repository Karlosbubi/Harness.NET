using System.Globalization;
using Dapper;
using Harness.DataAccess.Configuration;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.Framework;

internal sealed class SqliteFrameworkOverlayStore(IApplicationPaths applicationPaths)
    : IFrameworkOverlayStore
{
    public async ValueTask<WorkspaceFrameworkOverlay?> GetAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        OverlayRow? row = await connection.QuerySingleOrDefaultAsync<OverlayRow>(new CommandDefinition("""
            SELECT workspace_id AS WorkspaceId, content, updated_at AS UpdatedAt
            FROM workspace_framework_overlays
            WHERE workspace_id = @workspaceId;
            """, new { workspaceId }, cancellationToken: cancellationToken));
        return row?.ToRecord();
    }

    public async ValueTask<WorkspaceFrameworkOverlay> SaveAsync(
        string workspaceId,
        string content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        string now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        OverlayRow row = await connection.QuerySingleAsync<OverlayRow>(new CommandDefinition("""
            INSERT INTO workspace_framework_overlays (workspace_id, content, updated_at)
            VALUES (@workspaceId, @content, @now)
            ON CONFLICT(workspace_id) DO UPDATE SET
                content = excluded.content,
                updated_at = excluded.updated_at
            RETURNING workspace_id AS WorkspaceId, content, updated_at AS UpdatedAt;
            """, new { workspaceId, content, now }, cancellationToken: cancellationToken));
        return row.ToRecord();
    }

    public async ValueTask DeleteAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("""
            DELETE FROM workspace_framework_overlays WHERE workspace_id = @workspaceId;
            """, new { workspaceId }, cancellationToken: cancellationToken));
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

    private sealed class OverlayRow
    {
        public string WorkspaceId { get; init; } = string.Empty;
        public string Content { get; init; } = string.Empty;
        public string UpdatedAt { get; init; } = string.Empty;

        internal WorkspaceFrameworkOverlay ToRecord() => new(
            WorkspaceId,
            Content,
            DateTimeOffset.Parse(UpdatedAt, CultureInfo.InvariantCulture));
    }
}
