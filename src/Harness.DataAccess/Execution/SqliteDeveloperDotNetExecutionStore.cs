using System.Collections.Immutable;
using System.Text.Json;
using Dapper;
using Harness.DataAccess.Configuration;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.Execution;

internal sealed class SqliteDeveloperDotNetExecutionStore(
    IApplicationPaths applicationPaths) : IDeveloperDotNetExecutionStore
{
    private readonly SemaphoreSlim reconciliationGate = new(1, 1);
    private int reconciled;

    public async ValueTask<StoredDeveloperExecution> StartAsync(
        StoredDeveloperExecutionStart execution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO developer_dotnet_executions (
                id, workspace_id, goal_id, source_description, project_path,
                operation, run_mode, debug_mode, target_framework, configuration, declaration_id,
                test_id, test_name, test_scope, test_selection_json, state, started_at)
            VALUES (
                @id, @workspaceId, @goalId, @sourceDescription, @projectPath,
                @operation, @runMode, @debugMode, @targetFramework, @configuration, @declarationId,
                @testId, @testName, @testScope, @testSelectionJson, 'Running', @startedAt);
            """, new
        {
            id = execution.Id.Value,
            workspaceId = execution.WorkspaceId.Value,
            goalId = execution.GoalId?.Value,
            sourceDescription = execution.SourceDescription.Value,
            projectPath = execution.ProjectPath.Value,
            operation = execution.Operation is StoredDeveloperExecutionOperation.HotReload or
                StoredDeveloperExecutionOperation.Debug
                ? execution.Operation is StoredDeveloperExecutionOperation.Debug &&
                  execution.TestId is not null
                    ? StoredDeveloperExecutionOperation.Test.ToString()
                    : StoredDeveloperExecutionOperation.Run.ToString()
                : execution.Operation.ToString(),
            runMode = execution.Operation is StoredDeveloperExecutionOperation.HotReload
                ? "HotReload"
                : "Standard",
            debugMode = execution.Operation is StoredDeveloperExecutionOperation.Debug
                ? execution.TestId is null ? "Project" : "Test"
                : "None",
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
        await using SqliteTransaction transaction = connection.BeginTransaction();
        DynamicParameters parameters = new();
        parameters.Add("id", completion.Id.Value);
        parameters.Add("state", completion.State.ToString());
        parameters.Add("completedAt", completion.CompletedAt.ToString("O"));
        parameters.Add("exitCode", completion.ExitCode);
        parameters.Add("durationMilliseconds", completion.DurationMilliseconds);
        parameters.Add("errorCode", completion.ErrorCode);
        parameters.Add("error", completion.Error);
        parameters.Add("testCasesTruncated", completion.AreTestCasesTruncated ? 1 : 0);
        int changed = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE developer_dotnet_executions
            SET state = @state,
                completed_at = @completedAt,
                exit_code = @exitCode,
                duration_milliseconds = @durationMilliseconds,
                error_code = @errorCode,
                error = @error,
                test_cases_truncated = @testCasesTruncated
            WHERE id = @id AND state = 'Running';
            """, parameters, transaction, cancellationToken: cancellationToken));
        if (changed != 1)
        {
            throw new InvalidOperationException("The developer execution is not running.");
        }
        if (!completion.TestCases.IsDefaultOrEmpty)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO developer_dotnet_test_case_results (
                    execution_id, ordinal, fully_qualified_name,
                    outcome, duration_milliseconds)
                VALUES (
                    @executionId, @ordinal, @fullyQualifiedName,
                    @outcome, @durationMilliseconds);
                """, completion.TestCases.Select((item, ordinal) => new
                {
                    executionId = completion.Id.Value,
                    ordinal,
                    fullyQualifiedName = item.FullyQualifiedName.Value,
                    outcome = item.Outcome.ToString(),
                    durationMilliseconds = item.DurationMilliseconds,
                }), transaction, cancellationToken: cancellationToken));
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async ValueTask<IReadOnlyList<StoredDeveloperExecution>> ListAsync(
        StoredDeveloperWorkspaceId workspaceId,
        StoredDeveloperGoalId? goalId,
        int maximumResults,
        CancellationToken cancellationToken = default)
    {
        int limit = Math.Clamp(maximumResults, 1, 200);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        Row[] rows = (await connection.QueryAsync<Row>(new CommandDefinition("""
            SELECT id AS Id,
                   workspace_id AS WorkspaceId,
                   goal_id AS GoalId,
                   source_description AS SourceDescription,
                   operation AS Operation,
                   run_mode AS RunMode,
                   debug_mode AS DebugMode,
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
                   test_cases_truncated AS TestCasesTruncated,
                   error_code AS ErrorCode,
                   error AS Error
            FROM developer_dotnet_executions
            WHERE workspace_id = @workspaceId
              AND ((@goalId IS NULL AND goal_id IS NULL) OR goal_id = @goalId)
            ORDER BY started_at DESC
            LIMIT @limit;
            """, new { workspaceId = workspaceId.Value, goalId = goalId?.Value, limit },
            cancellationToken: cancellationToken))).ToArray();
        if (rows.Length == 0) return [];
        CaseRow[] cases = (await connection.QueryAsync<CaseRow>(new CommandDefinition("""
            SELECT execution_id AS ExecutionId,
                   ordinal AS Ordinal,
                   fully_qualified_name AS FullyQualifiedName,
                   outcome AS Outcome,
                   duration_milliseconds AS DurationMilliseconds
            FROM developer_dotnet_test_case_results
            WHERE execution_id IN @ids
            ORDER BY execution_id, ordinal;
            """, new { ids = rows.Select(row => row.Id).ToArray() },
            cancellationToken: cancellationToken))).ToArray();
        Dictionary<string, ImmutableArray<StoredDeveloperTestCaseResult>> byExecution = cases
            .GroupBy(item => item.ExecutionId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key,
                group => group.Select(item => item.ToRecord()).ToImmutableArray(),
                StringComparer.Ordinal);
        return rows.Select(row => row.ToRecord(
            byExecution.GetValueOrDefault(row.Id, []))).ToArray();
    }

    public async ValueTask<int> InterruptRunningAsync(
        DateTimeOffset completedAt,
        DateTimeOffset startedBefore,
        CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref reconciled) != 0) return 0;
        await reconciliationGate.WaitAsync(cancellationToken);
        try
        {
            if (reconciled != 0) return 0;
            await using SqliteConnection connection = await OpenAsync(cancellationToken);
            int changed = await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE developer_dotnet_executions
                SET state = 'Interrupted',
                    completed_at = @completedAt,
                    error_code = 'application_restarted',
                    error = 'Harness.NET restarted before this project operation completed.'
                WHERE state = 'Running' AND started_at < @startedBefore;
                """, new
            {
                completedAt = completedAt.ToString("O"),
                startedBefore = startedBefore.ToString("O"),
            },
                cancellationToken: cancellationToken));
            Volatile.Write(ref reconciled, 1);
            return changed;
        }
        finally
        {
            reconciliationGate.Release();
        }
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
        public string RunMode { get; init; } = string.Empty;
        public string DebugMode { get; init; } = string.Empty;
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
        public long TestCasesTruncated { get; init; }
        public string? ErrorCode { get; init; }
        public string? Error { get; init; }

        internal StoredDeveloperExecution ToRecord(
            ImmutableArray<StoredDeveloperTestCaseResult> testCases) => new(
            new(Id), new(WorkspaceId), GoalId is null ? null : new(GoalId),
            new(SourceDescription), !DebugMode.Equals("None", StringComparison.Ordinal)
                ? StoredDeveloperExecutionOperation.Debug
                : RunMode.Equals("HotReload", StringComparison.Ordinal)
                ? StoredDeveloperExecutionOperation.HotReload
                : Enum.Parse<StoredDeveloperExecutionOperation>(Operation),
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
                    .Select(item => new StoredDeveloperTestName(item)).ToImmutableArray(),
            testCases,
            TestCasesTruncated != 0);
    }

    private sealed class CaseRow
    {
        public string ExecutionId { get; init; } = string.Empty;
        public long Ordinal { get; init; }
        public string FullyQualifiedName { get; init; } = string.Empty;
        public string Outcome { get; init; } = string.Empty;
        public long DurationMilliseconds { get; init; }

        internal StoredDeveloperTestCaseResult ToRecord() => new(
            new(FullyQualifiedName), Enum.Parse<StoredDeveloperTestOutcome>(Outcome),
            DurationMilliseconds);
    }
}
