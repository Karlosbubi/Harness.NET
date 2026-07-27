using System.Globalization;
using Dapper;
using Harness.DataAccess.Configuration;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.Workflows;

internal sealed class SqliteWorkflowCheckpointStore(IApplicationPaths applicationPaths)
    : IWorkflowCheckpointStore
{
    public async ValueTask<StoredWorkflowSnapshot?> GetLatestAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        WorkflowRunRow? run = await connection.QuerySingleOrDefaultAsync<WorkflowRunRow>(
            new CommandDefinition(RunSelectSql + " ORDER BY updated_at DESC, id DESC LIMIT 1;",
                cancellationToken: cancellationToken));
        return run is null
            ? null
            : await ReadSnapshotAsync(connection, transaction: null, run, cancellationToken);
    }

    public async ValueTask<StoredWorkflowSnapshot> StartAsync(
        StoredWorkflowRun run,
        StoredWorkflowCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        ValidateRun(run);
        ValidateCheckpoint(checkpoint, allowUnsequenced: false);
        if (run.State is not WorkflowRunState.Running ||
            checkpoint.RunId != run.Id ||
            checkpoint.Sequence != 1 ||
            checkpoint.Kind is not WorkflowCheckpointKind.Started)
        {
            throw new ArgumentException("A workflow must start at its Started checkpoint.");
        }

        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO workflow_runs (id, state, created_at, updated_at)
            VALUES (@Id, @State, @CreatedAt, @UpdatedAt);
            """, new
        {
            Id = run.Id.Value,
            State = run.State.ToString(),
            CreatedAt = Format(run.CreatedAt),
            UpdatedAt = Format(run.UpdatedAt),
        }, transaction, cancellationToken: cancellationToken));
        await InsertCheckpointAsync(connection, transaction, checkpoint, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(run, [checkpoint]);
    }

    public async ValueTask<StoredWorkflowSnapshot> AppendAsync(
        StoredWorkflowCheckpoint checkpoint,
        WorkflowCheckpointKind expectedCheckpoint,
        WorkflowRunState expectedState,
        WorkflowRunState nextState,
        CancellationToken cancellationToken = default)
    {
        ValidateCheckpoint(checkpoint, allowUnsequenced: true);
        if (!Enum.IsDefined(expectedCheckpoint) ||
            !Enum.IsDefined(expectedState) ||
            !Enum.IsDefined(nextState) ||
            !IsValidTransition(
                expectedCheckpoint,
                checkpoint.Kind,
                expectedState,
                nextState))
        {
            throw new ArgumentException("The workflow transition contains an unknown semantic value.");
        }

        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        WorkflowRunRow? run = await connection.QuerySingleOrDefaultAsync<WorkflowRunRow>(
            new CommandDefinition("""
                UPDATE workflow_runs
                SET state = @nextState, updated_at = @updatedAt
                WHERE id = @runId
                  AND state = @expectedState
                  AND (SELECT kind FROM workflow_checkpoints
                       WHERE run_id = @runId ORDER BY sequence DESC LIMIT 1) = @expectedCheckpoint
                RETURNING id, state, created_at AS CreatedAt, updated_at AS UpdatedAt;
                """, new
            {
                runId = checkpoint.RunId.Value,
                expectedState = expectedState.ToString(),
                expectedCheckpoint = expectedCheckpoint.ToString(),
                nextState = nextState.ToString(),
                updatedAt = Format(checkpoint.CreatedAt),
            }, transaction, cancellationToken: cancellationToken));
        if (run is null)
        {
            throw new InvalidOperationException(
                "The workflow changed before its next checkpoint was persisted.");
        }

        int nextSequence = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COALESCE(MAX(sequence), 0) + 1
            FROM workflow_checkpoints WHERE run_id = @runId;
            """, new { runId = checkpoint.RunId.Value }, transaction,
            cancellationToken: cancellationToken));
        StoredWorkflowCheckpoint sequenced = checkpoint with { Sequence = nextSequence };
        await InsertCheckpointAsync(connection, transaction, sequenced, cancellationToken);
        StoredWorkflowSnapshot snapshot = await ReadSnapshotAsync(
            connection,
            transaction,
            run,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return snapshot;
    }

    private static async ValueTask InsertCheckpointAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        StoredWorkflowCheckpoint checkpoint,
        CancellationToken cancellationToken) =>
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO workflow_checkpoints (
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

    private static async ValueTask<StoredWorkflowSnapshot> ReadSnapshotAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        WorkflowRunRow run,
        CancellationToken cancellationToken)
    {
        IEnumerable<WorkflowCheckpointRow> checkpointRows =
            await connection.QueryAsync<WorkflowCheckpointRow>(new CommandDefinition("""
                SELECT id, run_id AS RunId, sequence, kind, actor, summary,
                       evidence_title AS EvidenceTitle, evidence_content AS EvidenceContent,
                       created_at AS CreatedAt
                FROM workflow_checkpoints
                WHERE run_id = @runId
                ORDER BY sequence;
                """, new { runId = run.Id }, transaction,
                cancellationToken: cancellationToken));
        return new(run.ToRecord(), checkpointRows.Select(row => row.ToRecord()).ToArray());
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

    private static void ValidateRun(StoredWorkflowRun run)
    {
        if (run.Id is null ||
            !Guid.TryParseExact(run.Id.Value, "N", out _) ||
            !Enum.IsDefined(run.State) ||
            run.UpdatedAt < run.CreatedAt)
        {
            throw new ArgumentException("The workflow run contains invalid semantic values.");
        }
    }

    private static void ValidateCheckpoint(
        StoredWorkflowCheckpoint checkpoint,
        bool allowUnsequenced)
    {
        bool hasEvidence = checkpoint.EvidenceTitle is not null &&
                           checkpoint.EvidenceContent is not null;
        if (!Guid.TryParseExact(checkpoint.Id, "N", out _) ||
            checkpoint.RunId is null ||
            !Guid.TryParseExact(checkpoint.RunId.Value, "N", out _) ||
            checkpoint.Sequence < (allowUnsequenced ? 0 : 1) ||
            !Enum.IsDefined(checkpoint.Kind) ||
            !Enum.IsDefined(checkpoint.Actor) ||
            string.IsNullOrWhiteSpace(checkpoint.Summary?.Value) ||
            checkpoint.Summary.Value.Length > 4096 ||
            (checkpoint.EvidenceTitle is null) != (checkpoint.EvidenceContent is null) ||
            (hasEvidence && (string.IsNullOrWhiteSpace(checkpoint.EvidenceTitle!.Value) ||
                             checkpoint.EvidenceTitle.Value.Length > 256 ||
                             string.IsNullOrWhiteSpace(checkpoint.EvidenceContent!.Value) ||
                             checkpoint.EvidenceContent.Value.Length > 64 * 1024)))
        {
            throw new ArgumentException("The workflow checkpoint contains invalid semantic values.");
        }
    }

    private static string Format(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    private static bool IsValidTransition(
        WorkflowCheckpointKind expectedCheckpoint,
        WorkflowCheckpointKind nextCheckpoint,
        WorkflowRunState expectedState,
        WorkflowRunState nextState) =>
        (expectedCheckpoint, nextCheckpoint, expectedState, nextState) switch
        {
            (WorkflowCheckpointKind.Started,
                WorkflowCheckpointKind.PlanProposed,
                WorkflowRunState.Running,
                WorkflowRunState.Paused) => true,
            (WorkflowCheckpointKind.PlanProposed,
                WorkflowCheckpointKind.ImplementationProduced,
                WorkflowRunState.Paused,
                WorkflowRunState.Running) => true,
            (WorkflowCheckpointKind.ImplementationProduced,
                WorkflowCheckpointKind.ReviewCompleted,
                WorkflowRunState.Running,
                WorkflowRunState.Completed) => true,
            _ => false,
        };

    private const string RunSelectSql = """
        SELECT id, state, created_at AS CreatedAt, updated_at AS UpdatedAt
        FROM workflow_runs
        """;

    private sealed class WorkflowRunRow
    {
        public string Id { get; init; } = string.Empty;
        public string State { get; init; } = string.Empty;
        public string CreatedAt { get; init; } = string.Empty;
        public string UpdatedAt { get; init; } = string.Empty;

        internal StoredWorkflowRun ToRecord() => new(
            new(Id),
            Enum.Parse<WorkflowRunState>(State),
            DateTimeOffset.Parse(CreatedAt, CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(UpdatedAt, CultureInfo.InvariantCulture));
    }

    private sealed class WorkflowCheckpointRow
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

        internal StoredWorkflowCheckpoint ToRecord() => new(
            Id,
            new(RunId),
            Sequence,
            Enum.Parse<WorkflowCheckpointKind>(Kind),
            Enum.Parse<WorkflowActor>(Actor),
            new(Summary),
            EvidenceTitle is null ? null : new(EvidenceTitle),
            EvidenceContent is null ? null : new(EvidenceContent),
            DateTimeOffset.Parse(CreatedAt, CultureInfo.InvariantCulture));
    }
}
