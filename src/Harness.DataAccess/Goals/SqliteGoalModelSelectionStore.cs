using System.Globalization;
using Dapper;
using Harness.DataAccess.Configuration;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.Goals;

internal sealed class SqliteGoalModelSelectionStore(IApplicationPaths applicationPaths)
    : IGoalModelSelectionStore
{
    public async ValueTask<StoredGoalModelSelection> SaveAsync(
        StoredGoalModelSelection selection,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        SelectionRow row = await connection.QuerySingleAsync<SelectionRow>(new CommandDefinition("""
            INSERT INTO goal_model_selections (
                goal_id, role, provider, model, selected_at)
            VALUES (@GoalId, @Role, @Provider, @Model, @SelectedAt)
            ON CONFLICT (goal_id, role) DO UPDATE SET
                provider = excluded.provider,
                model = excluded.model,
                selected_at = excluded.selected_at
            RETURNING goal_id AS GoalId, role, provider, model, selected_at AS SelectedAt;
            """, new
        {
            selection.GoalId,
            selection.Role,
            selection.Provider,
            selection.Model,
            SelectedAt = Format(selection.SelectedAt),
        }, cancellationToken: cancellationToken));
        return row.ToRecord();
    }

    public async ValueTask<IReadOnlyList<StoredGoalModelSelection>> ListAsync(
        string goalId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        IEnumerable<SelectionRow> rows = await connection.QueryAsync<SelectionRow>(
            new CommandDefinition("""
                SELECT goal_id AS GoalId, role, provider, model, selected_at AS SelectedAt
                FROM goal_model_selections
                WHERE goal_id = @goalId
                ORDER BY CASE role WHEN 'Lead' THEN 0 WHEN 'Implementer' THEN 1 ELSE 2 END;
                """, new { goalId }, cancellationToken: cancellationToken));
        return rows.Select(row => row.ToRecord()).ToArray();
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

    private static string Format(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    private sealed class SelectionRow
    {
        public string GoalId { get; init; } = string.Empty;

        public string Role { get; init; } = string.Empty;

        public string Provider { get; init; } = string.Empty;

        public string Model { get; init; } = string.Empty;

        public string SelectedAt { get; init; } = string.Empty;

        internal StoredGoalModelSelection ToRecord() => new(
            GoalId,
            Role,
            Provider,
            Model,
            DateTimeOffset.Parse(SelectedAt, CultureInfo.InvariantCulture));
    }
}
