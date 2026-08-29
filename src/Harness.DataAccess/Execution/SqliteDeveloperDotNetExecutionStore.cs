using System.Collections.Immutable;
using System.Text.Json;
using Dapper;
using Harness.DataAccess.Configuration;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.Execution;

internal sealed class SqliteDeveloperDotNetExecutionStore(
    IApplicationPaths applicationPaths) : IDeveloperDotNetExecutionStore
{
    public async ValueTask<StoredDeveloperExecution> StartAsync(
        StoredDeveloperExecutionStart execution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO developer_dotnet_executions (
                id, workspace_id, goal_id, source_description, project_path,
                operation, target_framework, configuration, declaration_id,
                test_id, test_name, test_scope, test_selection_json, state, started_at)
            VALUES (
                @id, @workspaceId, @goalId, @sourceDescription, @projectPath,
                @operation, @targetFramework, @configuration, @declarationId,
                @testId, @testName, @testScope, @testSelectionJson, 'Running', @startedAt);
            """, new
        {
            id = execution.Id.Value,
            workspaceId = execution.WorkspaceId.Value,
            goalId = execution.GoalId?.Value,
            sourceDescription = execution.SourceDescription.Value,
            projectPath = execution.ProjectPath.Value,
            operation = execution.Operation.ToString(),
            targetFramework = execution.TargetFramework?.Value,
            configuration = execution.Configuration?.Value,
            declarationId = execution.DeclarationId?.Value ?? string.Empty,
            testId = execution.TestId?.Value,
            testName = execution.TestName?.Value,
            testScope = execution.TestScope?.ToString(),
            testSelectionJson = execution.SelectedTests.IsDefaultOrEmpty
                ? null
                : JsonSerializer.Serialize(
                    execution.SelectedTests.Select(item => item.Value)),
            startedAt = execution.StartedAt.ToString("O"),
        }, cancellationToken: cancellationToken));
        return new(
            execution.Id, execution.WorkspaceId, execution.GoalId,
            execution.SourceDescription, execution.Operation, execution.ProjectPath,
            execution.TargetFramework, execution.Configuration,
            execution.DeclarationId, StoredDeveloperExecutionState.Running,
            execution.StartedAt, null, null, 0, null, null,
            execution.TestId, execution.TestName, execution.TestScope,
            execution.SelectedTests);
    }

    public async ValueTask CompleteAsync(
        StoredDeveloperExecutionCompletion completion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);
        if (completion.State is StoredDeveloperExecutionState.Running)
        {
            throw new ArgumentException("A completion must be terminal.", nameof(completion));
        }
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        DynamicParameters parameters = new();
        parameters.Add("id", completion.Id.Value);
        parameters.Add("state", completion.State.ToString());
        parameters.Add("completedAt", completion.CompletedAt.ToString("O"));
        parameters.Add("exitCode", completion.ExitCode);
        parameters.Add("durationMilliseconds", completion.DurationMilliseconds);
        parameters.Add("errorCode", completion.ErrorCode);
        parameters.Add("error", completion.Error);
        int changed = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE developer_dotnet_executions
            SET state = @state,
                completed_at = @completedAt,
                exit_code = @exitCode,
                duration_milliseconds = @durationMilliseconds,
                error_code = @errorCode,
                error = @error
            WHERE id = @id AND state = 'Running';
            """, parameters, cancellationToken: cancellationToken));
        if (changed != 1)
        {
            throw new InvalidOperationException("The developer execution is not running.");
        }
    }

    public async ValueTask<IReadOnlyList<StoredDeveloperExecution>> ListAsync(
        StoredDeveloperWorkspaceId workspaceId,
        StoredDeveloperGoalId? goalId,
        int maximumResults,
        CancellationToken cancellationToken = default)
    {
        int limit = Math.Clamp(maximumResults, 1, 200);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        IEnumerable<Row> rows = await connection.QueryAsync<Row>(new CommandDefinition("""
            SELECT id AS Id,
                   workspace_id AS WorkspaceId,
                   goal_id AS GoalId,
                   source_description AS SourceDescription,
                   operation AS Operation,
                   project_path AS ProjectPath,
                   target_framework AS TargetFramework,
                   configuration AS Configuration,
                   declaration_id AS DeclarationId,
                   test_id AS TestId,
                   test_name AS TestName,
                   test_scope AS TestScope,
                   test_selection_json AS TestSelectionJson,
                   state AS State,
                   started_at AS StartedAt,
                   completed_at AS CompletedAt,
                   exit_code AS ExitCode,
                   duration_milliseconds AS DurationMilliseconds,
                   error_code AS ErrorCode,
                   error AS Error
            FROM developer_dotnet_executions
            WHERE workspace_id = @workspaceId
              AND ((@goalId IS NULL AND goal_id IS NULL) OR goal_id = @goalId)
            ORDER BY started_at DESC
            LIMIT @limit;
            """, new { workspaceId = workspaceId.Value, goalId = goalId?.Value, limit },
            cancellationToken: cancellationToken));
        return rows.Select(row => row.ToRecord()).ToArray();
    }

    public async ValueTask<int> InterruptRunningAsync(
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE developer_dotnet_executions
            SET state = 'Interrupted',
                completed_at = @completedAt,
                error_code = 'application_restarted',
                error = 'Harness.NET restarted before this project run completed.'
            WHERE state = 'Running';
            """, new { completedAt = completedAt.ToString("O") },
            cancellationToken: cancellationToken));
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

    private sealed class Row
    {
        public string Id { get; init; } = string.Empty;
        public string WorkspaceId { get; init; } = string.Empty;
        public string? GoalId { get; init; }
        public string SourceDescription { get; init; } = string.Empty;
        public string Operation { get; init; } = string.Empty;
        public string ProjectPath { get; init; } = string.Empty;
        public string? TargetFramework { get; init; }
        public string? Configuration { get; init; }
        public string DeclarationId { get; init; } = string.Empty;
        public string? TestId { get; init; }
        public string? TestName { get; init; }
        public string? TestScope { get; init; }
        public string? TestSelectionJson { get; init; }
        public string State { get; init; } = string.Empty;
        public string StartedAt { get; init; } = string.Empty;
        public string? CompletedAt { get; init; }
        public long? ExitCode { get; init; }
        public long DurationMilliseconds { get; init; }
        public string? ErrorCode { get; init; }
        public string? Error { get; init; }

        internal StoredDeveloperExecution ToRecord() => new(
            new(Id), new(WorkspaceId), GoalId is null ? null : new(GoalId),
            new(SourceDescription), Enum.Parse<StoredDeveloperExecutionOperation>(Operation),
            new(ProjectPath), TargetFramework is null ? null : new(TargetFramework),
            Configuration is null ? null : new(Configuration),
            string.IsNullOrEmpty(DeclarationId) ? null : new(DeclarationId),
            Enum.Parse<StoredDeveloperExecutionState>(State),
            DateTimeOffset.Parse(StartedAt),
            CompletedAt is null ? null : DateTimeOffset.Parse(CompletedAt),
            ExitCode is null ? null : checked((int)ExitCode.Value),
            DurationMilliseconds, ErrorCode, Error,
            TestId is null ? null : new(TestId),
            TestName is null ? null : new(TestName),
            TestScope is null ? null : Enum.Parse<StoredDeveloperTestScope>(TestScope),
            TestSelectionJson is null
                ? []
                : (JsonSerializer.Deserialize<string[]>(TestSelectionJson) ?? [])
                    .Select(item => new StoredDeveloperTestName(item)).ToImmutableArray());
    }
}
