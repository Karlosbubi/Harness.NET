using System.Globalization;
using Dapper;
using Harness.DataAccess.Configuration;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.Workflows;

internal sealed class SqliteGoalWorkflowTaskStore(IApplicationPaths applicationPaths)
    : IGoalWorkflowTaskStore
{
    public async ValueTask<IReadOnlyList<StoredGoalWorkflowTask>> CreateAsync(
        GoalWorkflowRunId runId,
        IReadOnlyList<StoredGoalWorkflowTask> tasks,
        CancellationToken cancellationToken = default)
    {
        ValidateRunId(runId);
        if (tasks is null || tasks.Count is < 1 or > 12)
        {
            throw new ArgumentException("A delegation must contain 1-12 bounded tasks.");
        }

        for (int index = 0; index < tasks.Count; index++)
        {
            ValidateNew(tasks[index], runId, index + 1);
        }

        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        foreach (StoredGoalWorkflowTask task in tasks)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO goal_workflow_tasks (
                    id, run_id, sequence, title, objective, file_areas, acceptance_criteria,
                    state, report, created_at, started_at, completed_at)
                VALUES (
                    @Id, @RunId, @Sequence, @Title, @Objective, @FileAreas, @AcceptanceCriteria,
                    'Pending', NULL, @CreatedAt, NULL, NULL);
                """, new
            {
                Id = task.Id.Value,
                RunId = task.RunId.Value,
                Sequence = task.Sequence.Value,
                Title = task.Title.Value,
                Objective = task.Objective.Value,
                FileAreas = task.FileAreas.Value,
                AcceptanceCriteria = task.AcceptanceCriteria.Value,
                CreatedAt = Format(task.CreatedAt),
            }, transaction, cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return tasks;
    }

    public async ValueTask<IReadOnlyList<StoredGoalWorkflowTask>> ListAsync(
        GoalWorkflowRunId runId,
        CancellationToken cancellationToken = default)
    {
        ValidateRunId(runId);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        IEnumerable<TaskRow> rows = await connection.QueryAsync<TaskRow>(new CommandDefinition(
            SelectSql + " WHERE run_id = @runId ORDER BY sequence;",
            new { runId = runId.Value }, cancellationToken: cancellationToken));
        return rows.Select(row => row.ToRecord()).ToArray();
    }

    public ValueTask<StoredGoalWorkflowTask> StartAsync(
        GoalWorkflowTaskId taskId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken = default) => TransitionAsync(
            taskId,
            GoalWorkflowTaskState.Pending,
            GoalWorkflowTaskState.InProgress,
            report: null,
            startedAt,
            cancellationToken);

    public ValueTask<StoredGoalWorkflowTask> CompleteAsync(
        GoalWorkflowTaskId taskId,
        GoalWorkflowTaskReport report,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        if (report is null || string.IsNullOrWhiteSpace(report.Value) ||
            report.Value.Length > 256 * 1024)
        {
            throw new ArgumentException("A bounded task report is required.");
        }

        return TransitionAsync(
            taskId,
            GoalWorkflowTaskState.InProgress,
            GoalWorkflowTaskState.Completed,
            report,
            completedAt,
            cancellationToken);
    }

    private async ValueTask<StoredGoalWorkflowTask> TransitionAsync(
        GoalWorkflowTaskId taskId,
        GoalWorkflowTaskState expectedState,
        GoalWorkflowTaskState nextState,
        GoalWorkflowTaskReport? report,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        ValidateTaskId(taskId);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        TaskRow? row = await connection.QuerySingleOrDefaultAsync<TaskRow>(new CommandDefinition("""
            UPDATE goal_workflow_tasks
            SET state = @nextState,
                report = @report,
                started_at = CASE WHEN @nextState = 'InProgress' THEN @occurredAt ELSE started_at END,
                completed_at = CASE WHEN @nextState = 'Completed' THEN @occurredAt ELSE NULL END
            WHERE id = @id AND state = @expectedState
            RETURNING id, run_id AS RunId, sequence, title, objective, file_areas AS FileAreas,
                acceptance_criteria AS AcceptanceCriteria, state, report,
                created_at AS CreatedAt, started_at AS StartedAt,
                completed_at AS CompletedAt;
            """, new
        {
            id = taskId.Value,
            expectedState = expectedState.ToString(),
            nextState = nextState.ToString(),
            report = report?.Value,
            occurredAt = Format(occurredAt),
        }, cancellationToken: cancellationToken));
        return row?.ToRecord() ?? throw new InvalidOperationException(
            "The delegated task changed before its transition was persisted.");
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

    private static void ValidateNew(
        StoredGoalWorkflowTask task,
        GoalWorkflowRunId runId,
        int expectedSequence)
    {
        if (task is null || task.RunId != runId ||
            task.Sequence?.Value != expectedSequence ||
            task.State is not GoalWorkflowTaskState.Pending || task.Report is not null ||
            task.StartedAt is not null || task.CompletedAt is not null ||
            string.IsNullOrWhiteSpace(task.Title?.Value) || task.Title.Value.Length > 256 ||
            string.IsNullOrWhiteSpace(task.Objective?.Value) ||
            task.Objective.Value.Length > 8_192 ||
            string.IsNullOrWhiteSpace(task.FileAreas?.Value) ||
            task.FileAreas.Value.Length > 4_096 ||
            string.IsNullOrWhiteSpace(task.AcceptanceCriteria?.Value) ||
            task.AcceptanceCriteria.Value.Length > 8_192)
        {
            throw new ArgumentException("The delegated task is invalid.");
        }

        ValidateTaskId(task.Id);
    }

    private static void ValidateRunId(GoalWorkflowRunId runId)
    {
        if (runId is null || !Guid.TryParseExact(runId.Value, "N", out _))
        {
            throw new ArgumentException("The workflow run identifier is invalid.");
        }
    }

    private static void ValidateTaskId(GoalWorkflowTaskId taskId)
    {
        if (taskId is null || !Guid.TryParseExact(taskId.Value, "N", out _))
        {
            throw new ArgumentException("The workflow task identifier is invalid.");
        }
    }

    private static string Format(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    private const string SelectSql = """
        SELECT id, run_id AS RunId, sequence, title, objective, file_areas AS FileAreas,
               acceptance_criteria AS AcceptanceCriteria, state, report,
               created_at AS CreatedAt, started_at AS StartedAt,
               completed_at AS CompletedAt
        FROM goal_workflow_tasks
        """;

    private sealed class TaskRow
    {
        public string Id { get; init; } = string.Empty;
        public string RunId { get; init; } = string.Empty;
        public int Sequence { get; init; }
        public string Title { get; init; } = string.Empty;
        public string Objective { get; init; } = string.Empty;
        public string FileAreas { get; init; } = string.Empty;
        public string AcceptanceCriteria { get; init; } = string.Empty;
        public string State { get; init; } = string.Empty;
        public string? Report { get; init; }
        public string CreatedAt { get; init; } = string.Empty;
        public string? StartedAt { get; init; }
        public string? CompletedAt { get; init; }

        internal StoredGoalWorkflowTask ToRecord() => new(
            new(Id), new(RunId), new(Sequence), new(Title), new(Objective),
            new(FileAreas), new(AcceptanceCriteria),
            Enum.Parse<GoalWorkflowTaskState>(State),
            Report is null ? null : new(Report),
            DateTimeOffset.Parse(CreatedAt, CultureInfo.InvariantCulture),
            StartedAt is null ? null : DateTimeOffset.Parse(
                StartedAt, CultureInfo.InvariantCulture),
            CompletedAt is null ? null : DateTimeOffset.Parse(
                CompletedAt, CultureInfo.InvariantCulture));
    }
}
