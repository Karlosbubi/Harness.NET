using System.Globalization;
using Dapper;
using Harness.DataAccess.Configuration;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.Workflows;

internal sealed class SqliteGoalWorkflowStore(IApplicationPaths applicationPaths)
    : IGoalWorkflowStore
{
    public async ValueTask<StoredGoalWorkflowSnapshot?> GetLatestAsync(
        GoalWorkflowGoalId goalId,
        CancellationToken cancellationToken = default)
    {
        ValidateGoalId(goalId);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        GoalWorkflowRunRow? run = await connection.QuerySingleOrDefaultAsync<GoalWorkflowRunRow>(
            new CommandDefinition(RunSelectSql + " WHERE goal_id = @goalId " +
                "ORDER BY updated_at DESC, id DESC LIMIT 1;",
                new { goalId = goalId.Value }, cancellationToken: cancellationToken));
        return run is null
            ? null
            : await ReadSnapshotAsync(connection, transaction: null, run, cancellationToken);
    }

    public async ValueTask<StoredGoalWorkflowSnapshot> StartAsync(
        StoredGoalWorkflowRun run,
        StoredGoalWorkflowCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        ValidateRun(run);
        ValidateCheckpoint(checkpoint, allowUnsequenced: false);
        if (run.State is not GoalWorkflowRunState.Running ||
            checkpoint.RunId != run.Id || checkpoint.Sequence != 1 ||
            checkpoint.Kind is not GoalWorkflowCheckpointKind.Started)
        {
            throw new ArgumentException("A goal workflow must start at its Started checkpoint.");
        }

        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO goal_workflow_runs (
                id, goal_id, state, review_cycle, created_at, updated_at)
            VALUES (@Id, @GoalId, @State, @ReviewCycle, @CreatedAt, @UpdatedAt);
            """, new
        {
            Id = run.Id.Value,
            GoalId = run.GoalId.Value,
            State = run.State.ToString(),
            ReviewCycle = run.ReviewCycle.Value,
            CreatedAt = Format(run.CreatedAt),
            UpdatedAt = Format(run.UpdatedAt),
        }, transaction, cancellationToken: cancellationToken));
        await InsertCheckpointAsync(connection, transaction, checkpoint, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(run, [checkpoint]);
    }

    public async ValueTask<StoredGoalWorkflowSnapshot> AppendAsync(
        StoredGoalWorkflowCheckpoint checkpoint,
        GoalWorkflowCheckpointKind expectedCheckpoint,
        GoalWorkflowRunState expectedState,
        GoalWorkflowRunState nextState,
        CancellationToken cancellationToken = default,
        GoalWorkflowReviewCycle? nextReviewCycle = null)
    {
        ValidateCheckpoint(checkpoint, allowUnsequenced: true);
        if (!Enum.IsDefined(expectedCheckpoint) || !Enum.IsDefined(expectedState) ||
            !Enum.IsDefined(nextState) ||
            (nextReviewCycle is not null && nextReviewCycle.Value < 1) ||
            !IsValidTransition(expectedCheckpoint, checkpoint.Kind, expectedState, nextState))
        {
            throw new ArgumentException("The goal workflow transition is invalid.");
        }

        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        GoalWorkflowRunRow? run = await connection.QuerySingleOrDefaultAsync<GoalWorkflowRunRow>(
            new CommandDefinition("""
                UPDATE goal_workflow_runs
                SET state = @nextState,
                    review_cycle = COALESCE(@nextReviewCycle, review_cycle),
                    updated_at = @updatedAt
                WHERE id = @runId
                  AND state = @expectedState
                  AND (@nextReviewCycle IS NULL OR @nextReviewCycle = review_cycle + 1)
                  AND (SELECT kind FROM goal_workflow_checkpoints
                       WHERE run_id = @runId ORDER BY sequence DESC LIMIT 1) = @expectedCheckpoint
                RETURNING id, goal_id AS GoalId, state, review_cycle AS ReviewCycle,
                          created_at AS CreatedAt, updated_at AS UpdatedAt;
                """, new
            {
                runId = checkpoint.RunId.Value,
                expectedState = expectedState.ToString(),
                expectedCheckpoint = expectedCheckpoint.ToString(),
                nextState = nextState.ToString(),
                nextReviewCycle = nextReviewCycle?.Value,
                updatedAt = Format(checkpoint.CreatedAt),
            }, transaction, cancellationToken: cancellationToken));
        if (run is null)
        {
            throw new InvalidOperationException(
                "The goal workflow changed before its next checkpoint was persisted.");
        }

        int sequence = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COALESCE(MAX(sequence), 0) + 1
            FROM goal_workflow_checkpoints WHERE run_id = @runId;
            """, new { runId = checkpoint.RunId.Value }, transaction,
            cancellationToken: cancellationToken));
        StoredGoalWorkflowCheckpoint sequenced = checkpoint with { Sequence = sequence };
        await InsertCheckpointAsync(connection, transaction, sequenced, cancellationToken);
        StoredGoalWorkflowSnapshot snapshot = await ReadSnapshotAsync(
            connection, transaction, run, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return snapshot;
    }

    private static bool IsValidTransition(
        GoalWorkflowCheckpointKind expected,
        GoalWorkflowCheckpointKind next,
        GoalWorkflowRunState expectedState,
        GoalWorkflowRunState nextState) => (expected, next, expectedState, nextState) switch
    {
        (GoalWorkflowCheckpointKind.Started,
            GoalWorkflowCheckpointKind.LeadCallStarted,
            GoalWorkflowRunState.Running,
            GoalWorkflowRunState.Running) => true,
        (GoalWorkflowCheckpointKind.LeadCallStarted,
            GoalWorkflowCheckpointKind.PlanProposed,
            GoalWorkflowRunState.Running,
            GoalWorkflowRunState.AwaitingPlanApproval) => true,
        (GoalWorkflowCheckpointKind.PlanProposed,
            GoalWorkflowCheckpointKind.PlanApproved,
            GoalWorkflowRunState.AwaitingPlanApproval,
            GoalWorkflowRunState.Running) => true,
        (GoalWorkflowCheckpointKind.PlanApproved,
            GoalWorkflowCheckpointKind.ImplementerCallStarted,
            GoalWorkflowRunState.Running,
            GoalWorkflowRunState.Running) => true,
        (GoalWorkflowCheckpointKind.ImplementerCallStarted,
            GoalWorkflowCheckpointKind.ImplementationProduced,
            GoalWorkflowRunState.Running,
            GoalWorkflowRunState.Running) => true,
        (GoalWorkflowCheckpointKind.ImplementationProduced,
            GoalWorkflowCheckpointKind.ReviewerCallStarted,
            GoalWorkflowRunState.Running,
            GoalWorkflowRunState.Running) => true,
        (GoalWorkflowCheckpointKind.ReviewerCallStarted,
            GoalWorkflowCheckpointKind.ReviewCompleted,
            GoalWorkflowRunState.Running,
            GoalWorkflowRunState.Running) => true,
        (GoalWorkflowCheckpointKind.ReviewerCallStarted,
            GoalWorkflowCheckpointKind.ReviewCompleted,
            GoalWorkflowRunState.Running,
            GoalWorkflowRunState.AwaitingAcceptance) => true,
        (GoalWorkflowCheckpointKind.ReviewerCallStarted,
            GoalWorkflowCheckpointKind.ReviewCompleted,
            GoalWorkflowRunState.Running,
            GoalWorkflowRunState.NeedsDirection) => true,
        (GoalWorkflowCheckpointKind.ReviewCompleted,
            GoalWorkflowCheckpointKind.ImplementerCallStarted,
            GoalWorkflowRunState.Running,
            GoalWorkflowRunState.Running) => true,
        (GoalWorkflowCheckpointKind.ReviewCompleted,
            GoalWorkflowCheckpointKind.Accepted,
            GoalWorkflowRunState.AwaitingAcceptance,
            GoalWorkflowRunState.Completed) => true,
        (GoalWorkflowCheckpointKind.LeadCallStarted or
            GoalWorkflowCheckpointKind.ImplementerCallStarted or
            GoalWorkflowCheckpointKind.ReviewerCallStarted,
            GoalWorkflowCheckpointKind.UserDirectionRequired,
            GoalWorkflowRunState.Running,
            GoalWorkflowRunState.NeedsDirection) => true,
        (GoalWorkflowCheckpointKind.PlanProposed,
            GoalWorkflowCheckpointKind.UserDirectionRequired,
            GoalWorkflowRunState.AwaitingPlanApproval,
            GoalWorkflowRunState.NeedsDirection) => true,
        _ => false,
    };

    private static async ValueTask InsertCheckpointAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        StoredGoalWorkflowCheckpoint checkpoint,
        CancellationToken cancellationToken) =>
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO goal_workflow_checkpoints (
                id, run_id, sequence, kind, actor, summary,
                evidence_title, evidence_content, created_at)
            VALUES (
                @Id, @RunId, @Sequence, @Kind, @Actor, @Summary,
                @EvidenceTitle, @EvidenceContent, @CreatedAt);
            """, new
        {
            checkpoint.Id,
            RunId = checkpoint.RunId.Value,
            checkpoint.Sequence,
            Kind = checkpoint.Kind.ToString(),
            Actor = checkpoint.Actor.ToString(),
            Summary = checkpoint.Summary.Value,
            EvidenceTitle = checkpoint.EvidenceTitle?.Value,
            EvidenceContent = checkpoint.EvidenceContent?.Value,
            CreatedAt = Format(checkpoint.CreatedAt),
        }, transaction, cancellationToken: cancellationToken));

    private static async ValueTask<StoredGoalWorkflowSnapshot> ReadSnapshotAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        GoalWorkflowRunRow run,
        CancellationToken cancellationToken)
    {
        IEnumerable<GoalWorkflowCheckpointRow> rows =
            await connection.QueryAsync<GoalWorkflowCheckpointRow>(new CommandDefinition("""
                SELECT id, run_id AS RunId, sequence, kind, actor, summary,
                       evidence_title AS EvidenceTitle, evidence_content AS EvidenceContent,
                       created_at AS CreatedAt
                FROM goal_workflow_checkpoints
                WHERE run_id = @runId ORDER BY sequence;
                """, new { runId = run.Id }, transaction, cancellationToken: cancellationToken));
        return new(run.ToRecord(), rows.Select(row => row.ToRecord()).ToArray());
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

    private static void ValidateRun(StoredGoalWorkflowRun run)
    {
        ValidateGoalId(run.GoalId);
        if (run.Id is null || !Guid.TryParseExact(run.Id.Value, "N", out _) ||
            !Enum.IsDefined(run.State) || run.State is not GoalWorkflowRunState.Running ||
            run.ReviewCycle is null || run.ReviewCycle.Value < 0 ||
            run.UpdatedAt < run.CreatedAt)
        {
            throw new ArgumentException("The goal workflow run is invalid.");
        }
    }

    private static void ValidateGoalId(GoalWorkflowGoalId goalId)
    {
        if (goalId is null || !Guid.TryParseExact(goalId.Value, "N", out _))
        {
            throw new ArgumentException("The goal workflow goal identifier is invalid.");
        }
    }

    private static void ValidateCheckpoint(
        StoredGoalWorkflowCheckpoint checkpoint,
        bool allowUnsequenced)
    {
        bool hasEvidence = checkpoint.EvidenceTitle is not null;
        if (!Guid.TryParseExact(checkpoint.Id, "N", out _) || checkpoint.RunId is null ||
            !Guid.TryParseExact(checkpoint.RunId.Value, "N", out _) ||
            checkpoint.Sequence < (allowUnsequenced ? 0 : 1) ||
            !Enum.IsDefined(checkpoint.Kind) || !Enum.IsDefined(checkpoint.Actor) ||
            string.IsNullOrWhiteSpace(checkpoint.Summary?.Value) ||
            checkpoint.Summary.Value.Length > 4096 ||
            (checkpoint.EvidenceTitle is null) != (checkpoint.EvidenceContent is null) ||
            (hasEvidence &&
                (string.IsNullOrWhiteSpace(checkpoint.EvidenceTitle!.Value) ||
                 checkpoint.EvidenceTitle.Value.Length > 256 ||
                 string.IsNullOrWhiteSpace(checkpoint.EvidenceContent!.Value) ||
                 checkpoint.EvidenceContent.Value.Length > 256 * 1024)))
        {
            throw new ArgumentException("The goal workflow checkpoint is invalid.");
        }
    }

    private static string Format(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    private const string RunSelectSql = """
        SELECT id, goal_id AS GoalId, state, review_cycle AS ReviewCycle,
               created_at AS CreatedAt, updated_at AS UpdatedAt
        FROM goal_workflow_runs
        """;

    private sealed class GoalWorkflowRunRow
    {
        public string Id { get; init; } = string.Empty;
        public string GoalId { get; init; } = string.Empty;
        public string State { get; init; } = string.Empty;
        public int ReviewCycle { get; init; }
        public string CreatedAt { get; init; } = string.Empty;
        public string UpdatedAt { get; init; } = string.Empty;

        internal StoredGoalWorkflowRun ToRecord() => new(
            new(Id), new(GoalId), Enum.Parse<GoalWorkflowRunState>(State), new(ReviewCycle),
            DateTimeOffset.Parse(CreatedAt, CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(UpdatedAt, CultureInfo.InvariantCulture));
    }

    private sealed class GoalWorkflowCheckpointRow
    {
        public string Id { get; init; } = string.Empty;
        public string RunId { get; init; } = string.Empty;
        public int Sequence { get; init; }
        public string Kind { get; init; } = string.Empty;
        public string Actor { get; init; } = string.Empty;
        public string Summary { get; init; } = string.Empty;
        public string? EvidenceTitle { get; init; }
        public string? EvidenceContent { get; init; }
        public string CreatedAt { get; init; } = string.Empty;

        internal StoredGoalWorkflowCheckpoint ToRecord() => new(
            Id, new(RunId), Sequence, Enum.Parse<GoalWorkflowCheckpointKind>(Kind),
            Enum.Parse<WorkflowActor>(Actor), new(Summary),
            EvidenceTitle is null ? null : new(EvidenceTitle),
            EvidenceContent is null ? null : new(EvidenceContent),
            DateTimeOffset.Parse(CreatedAt, CultureInfo.InvariantCulture));
    }
}
