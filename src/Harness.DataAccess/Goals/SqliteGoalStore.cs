using System.Globalization;
using Dapper;
using Harness.DataAccess.Configuration;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.Goals;

internal sealed class SqliteGoalStore(IApplicationPaths applicationPaths) : IGoalStore
{
    public async ValueTask<StoredGoal> CreateAsync(
        StoredGoal goal,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        GoalRow row = await connection.QuerySingleAsync<GoalRow>(new CommandDefinition("""
            INSERT INTO goals (
                id, workspace_id, title, objective, review_cycle_limit,
                remote_budget_microusd, state, created_at, updated_at)
            VALUES (
                @Id, @WorkspaceId, @Title, @Objective, @ReviewCycleLimit,
                @RemoteBudgetMicrousd, @State, @CreatedAt, @UpdatedAt)
            RETURNING id, workspace_id AS WorkspaceId, title, objective,
                      review_cycle_limit AS ReviewCycleLimit,
                      remote_budget_microusd AS RemoteBudgetMicrousd,
                      state, created_at AS CreatedAt, updated_at AS UpdatedAt;
            """, new
        {
            goal.Id,
            goal.WorkspaceId,
            goal.Title,
            goal.Objective,
            goal.ReviewCycleLimit,
            goal.RemoteBudgetMicrousd,
            goal.State,
            CreatedAt = Format(goal.CreatedAt),
            UpdatedAt = Format(goal.UpdatedAt),
        }, cancellationToken: cancellationToken));
        return row.ToRecord();
    }

    public async ValueTask<StoredGoal?> GetAsync(
        string goalId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        GoalRow? row = await connection.QuerySingleOrDefaultAsync<GoalRow>(new CommandDefinition(
            SelectSql + " WHERE id = @goalId;",
            new { goalId },
            cancellationToken: cancellationToken));
        return row?.ToRecord();
    }

    public async ValueTask<IReadOnlyList<StoredGoal>> ListAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        IEnumerable<GoalRow> rows = await connection.QueryAsync<GoalRow>(new CommandDefinition(
            SelectSql + " WHERE workspace_id = @workspaceId ORDER BY updated_at DESC;",
            new { workspaceId },
            cancellationToken: cancellationToken));
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

    private const string SelectSql = """
        SELECT id, workspace_id AS WorkspaceId, title, objective,
               review_cycle_limit AS ReviewCycleLimit,
               remote_budget_microusd AS RemoteBudgetMicrousd,
               state, created_at AS CreatedAt, updated_at AS UpdatedAt
        FROM goals
        """;

    private sealed class GoalRow
    {
        public string Id { get; init; } = string.Empty;
        public string WorkspaceId { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Objective { get; init; } = string.Empty;
        public int ReviewCycleLimit { get; init; }
        public long? RemoteBudgetMicrousd { get; init; }
        public string State { get; init; } = string.Empty;
        public string CreatedAt { get; init; } = string.Empty;
        public string UpdatedAt { get; init; } = string.Empty;

        internal StoredGoal ToRecord() => new(
            Id,
            WorkspaceId,
            Title,
            Objective,
            ReviewCycleLimit,
            RemoteBudgetMicrousd,
            State,
            DateTimeOffset.Parse(CreatedAt, CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(UpdatedAt, CultureInfo.InvariantCulture));
    }
}
