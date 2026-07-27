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

    public async ValueTask<StoredPlan?> GetCurrentPlanAsync(
        string goalId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        PlanRow? row = await connection.QuerySingleOrDefaultAsync<PlanRow>(new CommandDefinition("""
            SELECT id, goal_id AS GoalId, revision, content, state,
                   created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM goal_plans
            WHERE goal_id = @goalId
            ORDER BY revision DESC
            LIMIT 1;
            """, new { goalId }, cancellationToken: cancellationToken));
        return row?.ToRecord();
    }

    public async ValueTask<StoredPlanSnapshot> SavePlanAsync(
        StoredPlan plan,
        string expectedGoalState,
        string nextGoalState,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        int updated = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE goals
            SET state = @nextGoalState, updated_at = @updatedAt
            WHERE id = @goalId AND state = @expectedGoalState;
            """, new
        {
            goalId = plan.GoalId,
            expectedGoalState,
            nextGoalState,
            updatedAt = Format(plan.UpdatedAt),
        }, transaction, cancellationToken: cancellationToken));
        if (updated != 1)
        {
            throw new InvalidOperationException("The goal state changed before the plan was saved.");
        }

        PlanRow planRow = await connection.QuerySingleAsync<PlanRow>(new CommandDefinition("""
            INSERT INTO goal_plans (id, goal_id, revision, content, state, created_at, updated_at)
            VALUES (@Id, @GoalId, @Revision, @Content, @State, @CreatedAt, @UpdatedAt)
            RETURNING id, goal_id AS GoalId, revision, content, state,
                      created_at AS CreatedAt, updated_at AS UpdatedAt;
            """, new
        {
            plan.Id,
            plan.GoalId,
            plan.Revision,
            plan.Content,
            plan.State,
            CreatedAt = Format(plan.CreatedAt),
            UpdatedAt = Format(plan.UpdatedAt),
        }, transaction, cancellationToken: cancellationToken));
        GoalRow goalRow = await ReadGoalAsync(connection, transaction, plan.GoalId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(goalRow.ToRecord(), planRow.ToRecord(), Approval: null);
    }

    public async ValueTask<StoredPlanSnapshot> DecidePlanAsync(
        StoredApproval approval,
        string expectedGoalState,
        string expectedPlanState,
        string nextGoalState,
        string nextPlanState,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        string decidedAt = Format(approval.DecidedAt);
        int goalUpdated = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE goals
            SET state = @nextGoalState, updated_at = @decidedAt
            WHERE id = @goalId AND state = @expectedGoalState;
            """, new
        {
            goalId = approval.GoalId,
            expectedGoalState,
            nextGoalState,
            decidedAt,
        }, transaction, cancellationToken: cancellationToken));
        int planUpdated = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE goal_plans
            SET state = @nextPlanState, updated_at = @decidedAt
            WHERE id = @planId AND goal_id = @goalId AND state = @expectedPlanState;
            """, new
        {
            planId = approval.PlanId,
            goalId = approval.GoalId,
            expectedPlanState,
            nextPlanState,
            decidedAt,
        }, transaction, cancellationToken: cancellationToken));
        if (goalUpdated != 1 || planUpdated != 1)
        {
            throw new InvalidOperationException("The goal or plan state changed before the decision was saved.");
        }

        ApprovalRow approvalRow = await connection.QuerySingleAsync<ApprovalRow>(new CommandDefinition("""
            INSERT INTO approvals (id, goal_id, plan_id, kind, decision, reason, decided_at)
            VALUES (@Id, @GoalId, @PlanId, @Kind, @Decision, @Reason, @DecidedAt)
            RETURNING id, goal_id AS GoalId, plan_id AS PlanId, kind, decision, reason,
                      decided_at AS DecidedAt;
            """, new
        {
            approval.Id,
            approval.GoalId,
            approval.PlanId,
            approval.Kind,
            approval.Decision,
            approval.Reason,
            DecidedAt = decidedAt,
        }, transaction, cancellationToken: cancellationToken));
        GoalRow goalRow = await ReadGoalAsync(connection, transaction, approval.GoalId, cancellationToken);
        PlanRow planRow = await connection.QuerySingleAsync<PlanRow>(new CommandDefinition("""
            SELECT id, goal_id AS GoalId, revision, content, state,
                   created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM goal_plans WHERE id = @planId;
            """, new { planId = approval.PlanId }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return new(goalRow.ToRecord(), planRow.ToRecord(), approvalRow.ToRecord());
    }

    private static async ValueTask<GoalRow> ReadGoalAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string goalId,
        CancellationToken cancellationToken) =>
        await connection.QuerySingleAsync<GoalRow>(new CommandDefinition(
            SelectSql + " WHERE id = @goalId;",
            new { goalId },
            transaction,
            cancellationToken: cancellationToken));

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

    private sealed class PlanRow
    {
        public string Id { get; init; } = string.Empty;
        public string GoalId { get; init; } = string.Empty;
        public int Revision { get; init; }
        public string Content { get; init; } = string.Empty;
        public string State { get; init; } = string.Empty;
        public string CreatedAt { get; init; } = string.Empty;
        public string UpdatedAt { get; init; } = string.Empty;

        internal StoredPlan ToRecord() => new(
            Id,
            GoalId,
            Revision,
            Content,
            State,
            DateTimeOffset.Parse(CreatedAt, CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(UpdatedAt, CultureInfo.InvariantCulture));
    }

    private sealed class ApprovalRow
    {
        public string Id { get; init; } = string.Empty;
        public string GoalId { get; init; } = string.Empty;
        public string PlanId { get; init; } = string.Empty;
        public string Kind { get; init; } = string.Empty;
        public string Decision { get; init; } = string.Empty;
        public string? Reason { get; init; }
        public string DecidedAt { get; init; } = string.Empty;

        internal StoredApproval ToRecord() => new(
            Id,
            GoalId,
            PlanId,
            Kind,
            Decision,
            Reason,
            DateTimeOffset.Parse(DecidedAt, CultureInfo.InvariantCulture));
    }
}
