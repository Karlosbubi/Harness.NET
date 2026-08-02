using System.Globalization;
using Dapper;
using Harness.DataAccess.Configuration;
using Harness.DataAccess.Worktrees;
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

    public async ValueTask<StoredGoal?> UpdateDraftSettingsAsync(
        string goalId,
        DateTimeOffset expectedUpdatedAt,
        int reviewCycleLimit,
        long? remoteBudgetMicrousd,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        GoalRow? row = await connection.QuerySingleOrDefaultAsync<GoalRow>(new CommandDefinition("""
            UPDATE goals
            SET review_cycle_limit = @reviewCycleLimit,
                remote_budget_microusd = @remoteBudgetMicrousd,
                updated_at = @updatedAt
            WHERE id = @goalId
              AND state = 'Draft'
              AND updated_at = @expectedUpdatedAt
            RETURNING id, workspace_id AS WorkspaceId, title, objective,
                      review_cycle_limit AS ReviewCycleLimit,
                      remote_budget_microusd AS RemoteBudgetMicrousd,
                      state, created_at AS CreatedAt, updated_at AS UpdatedAt;
            """, new
        {
            goalId,
            expectedUpdatedAt = Format(expectedUpdatedAt),
            reviewCycleLimit,
            remoteBudgetMicrousd,
            updatedAt = Format(updatedAt),
        }, cancellationToken: cancellationToken));
        return row?.ToRecord();
    }

    public async ValueTask<StoredGoalBudgetExtensionSnapshot?> ExtendRemoteBudgetAsync(
        string extensionId,
        string goalId,
        long? expectedBudgetMicrousd,
        long newBudgetMicrousd,
        string reason,
        DateTimeOffset approvedAt,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        GoalRow? row = await connection.QuerySingleOrDefaultAsync<GoalRow>(new CommandDefinition("""
            UPDATE goals
            SET remote_budget_microusd = @newBudgetMicrousd,
                updated_at = @approvedAt
            WHERE id = @goalId
              AND ((remote_budget_microusd IS NULL AND @expectedBudgetMicrousd IS NULL)
                   OR remote_budget_microusd = @expectedBudgetMicrousd)
              AND COALESCE(remote_budget_microusd, 0) < @newBudgetMicrousd
            RETURNING id, workspace_id AS WorkspaceId, title, objective,
                      review_cycle_limit AS ReviewCycleLimit,
                      remote_budget_microusd AS RemoteBudgetMicrousd,
                      state, created_at AS CreatedAt, updated_at AS UpdatedAt;
            """, new
        {
            extensionId,
            goalId,
            expectedBudgetMicrousd,
            newBudgetMicrousd,
            reason,
            approvedAt = Format(approvedAt),
        }, transaction, cancellationToken: cancellationToken));
        if (row is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO goal_budget_extensions (
                id, goal_id, previous_budget_microusd, new_budget_microusd,
                reason, approved_at)
            VALUES (
                @extensionId, @goalId, @expectedBudgetMicrousd, @newBudgetMicrousd,
                @reason, @approvedAt);
            """, new
        {
            extensionId,
            goalId,
            expectedBudgetMicrousd,
            newBudgetMicrousd,
            reason,
            approvedAt = Format(approvedAt),
        }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return new(
            row.ToRecord(),
            new(extensionId, goalId, expectedBudgetMicrousd, newBudgetMicrousd,
                reason, approvedAt));
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
        return new(goalRow.ToRecord(), planRow.ToRecord(), Approval: null, Worktree: null);
    }

    public async ValueTask<StoredPlanSnapshot> DecidePlanAsync(
        StoredApproval approval,
        StoredGoalWorktree? worktree,
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
        WorktreeRow? worktreeRow = null;
        if (worktree is not null)
        {
            worktreeRow = await connection.QuerySingleAsync<WorktreeRow>(new CommandDefinition("""
                INSERT INTO goal_worktrees (
                    goal_id, workspace_id, branch, path, base_commit, state, created_at)
                VALUES (
                    @GoalId, @WorkspaceId, @Branch, @Path, @BaseCommit, @State, @CreatedAt)
                RETURNING goal_id AS GoalId, workspace_id AS WorkspaceId, branch, path,
                          base_commit AS BaseCommit, state, created_at AS CreatedAt;
                """, new
            {
                worktree.GoalId,
                worktree.WorkspaceId,
                worktree.Branch,
                worktree.Path,
                worktree.BaseCommit,
                worktree.State,
                CreatedAt = Format(worktree.CreatedAt),
            }, transaction, cancellationToken: cancellationToken));
        }

        GoalRow goalRow = await ReadGoalAsync(connection, transaction, approval.GoalId, cancellationToken);
        PlanRow planRow = await connection.QuerySingleAsync<PlanRow>(new CommandDefinition("""
            SELECT id, goal_id AS GoalId, revision, content, state,
                   created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM goal_plans WHERE id = @planId;
            """, new { planId = approval.PlanId }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return new(
            goalRow.ToRecord(),
            planRow.ToRecord(),
            approvalRow.ToRecord(),
            worktreeRow?.ToRecord());
    }

    public async ValueTask<StoredGoalWorktree?> GetWorktreeAsync(
        string goalId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        WorktreeRow? row = await connection.QuerySingleOrDefaultAsync<WorktreeRow>(new CommandDefinition("""
            SELECT goal_id AS GoalId, workspace_id AS WorkspaceId, branch, path,
                   base_commit AS BaseCommit, state, created_at AS CreatedAt
            FROM goal_worktrees WHERE goal_id = @goalId;
            """, new { goalId }, cancellationToken: cancellationToken));
        return row?.ToRecord();
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

    private sealed class WorktreeRow
    {
        public string GoalId { get; init; } = string.Empty;
        public string WorkspaceId { get; init; } = string.Empty;
        public string Branch { get; init; } = string.Empty;
        public string Path { get; init; } = string.Empty;
        public string BaseCommit { get; init; } = string.Empty;
        public string State { get; init; } = string.Empty;
        public string CreatedAt { get; init; } = string.Empty;

        internal StoredGoalWorktree ToRecord() => new(
            GoalId,
            WorkspaceId,
            Branch,
            Path,
            BaseCommit,
            State,
            DateTimeOffset.Parse(CreatedAt, CultureInfo.InvariantCulture));
    }
}
