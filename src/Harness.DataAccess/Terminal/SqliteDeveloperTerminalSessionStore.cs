using Dapper;
using Harness.DataAccess.Configuration;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.Terminal;

internal sealed class SqliteDeveloperTerminalSessionStore(IApplicationPaths applicationPaths)
    : IDeveloperTerminalSessionStore
{
    private const int MaximumRetainedPerContext = 20;
    private readonly SemaphoreSlim reconciliationGate = new(1, 1);
    private int reconciled;

    public async ValueTask<StoredTerminalSession> StartAsync(
        StoredTerminalSessionStart session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        Validate(session);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteTransaction transaction = connection.BeginTransaction();
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO developer_terminal_sessions (
                id, workspace_id, goal_id, source_scope, source_branch, source_description,
                working_directory, shell_name, environment_profile, content_policy,
                columns, rows, state, started_at)
            VALUES (
                @id, @workspaceId, @goalId, @sourceScope, @sourceBranch, @sourceDescription,
                @workingDirectory, @shellName, @environmentProfile, @contentPolicy,
                @columns, @rows, 'Running', @startedAt);
            """, Parameters(session), transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition("""
            DELETE FROM developer_terminal_sessions
            WHERE id IN (
                SELECT id
                FROM developer_terminal_sessions
                WHERE workspace_id = @workspaceId
                  AND ((@goalId IS NULL AND goal_id IS NULL) OR goal_id = @goalId)
                ORDER BY started_at DESC, id DESC
                LIMIT -1 OFFSET @retain);
            """, new
        {
            workspaceId = session.WorkspaceId.Value,
            goalId = session.GoalId?.Value,
            retain = MaximumRetainedPerContext,
        }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return new(
            session.Id, session.WorkspaceId, session.GoalId, session.SourceScope, session.SourceBranch,
            session.SourceDescription, session.WorkingDirectory, session.Shell,
            session.EnvironmentProfile, session.ContentPolicy, session.Dimensions,
            StoredTerminalSessionState.Running, session.StartedAt, null, null, null, null);
    }

    public async ValueTask CompleteAsync(
        StoredTerminalSessionCompletion completion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);
        if (completion.State == StoredTerminalSessionState.Running)
        {
            throw new ArgumentException("A terminal completion must be terminal.",
                nameof(completion));
        }

        ValidateCompletion(completion);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        int changed = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE developer_terminal_sessions
            SET state = @state,
                completed_at = @completedAt,
                exit_code = @exitCode,
                error_code = @errorCode,
                error = @error
            WHERE id = @id AND state = 'Running';
            """, new
        {
            id = completion.Id.Value,
            state = completion.State.ToString(),
            completedAt = completion.CompletedAt.ToString("O"),
            exitCode = completion.ExitCode,
            errorCode = completion.ErrorCode,
            error = completion.Error,
        }, cancellationToken: cancellationToken));
        if (changed != 1)
        {
            throw new InvalidOperationException("The terminal session is not running.");
        }
    }

    public async ValueTask UpdateDimensionsAsync(
        StoredTerminalSessionId sessionId,
        StoredTerminalDimensions dimensions,
        CancellationToken cancellationToken = default)
    {
        PortaDeveloperTerminalConnectionFactory.ValidateDimensions(dimensions);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        int changed = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE developer_terminal_sessions
            SET columns = @columns, rows = @rows
            WHERE id = @id AND state = 'Running';
            """, new
        {
            id = sessionId.Value,
            columns = dimensions.Columns,
            rows = dimensions.Rows,
        }, cancellationToken: cancellationToken));
        if (changed != 1)
        {
            throw new InvalidOperationException("The terminal session is not running.");
        }
    }

    public async ValueTask<StoredTerminalSession?> GetAsync(
        StoredTerminalSessionId sessionId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        Row? row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            SelectSql + " WHERE id = @id;",
            new { id = sessionId.Value },
            cancellationToken: cancellationToken));
        return row?.ToRecord();
    }

    public async ValueTask<IReadOnlyList<StoredTerminalSession>> ListAsync(
        StoredTerminalWorkspaceId workspaceId,
        StoredTerminalGoalId? goalId,
        int maximumResults,
        CancellationToken cancellationToken = default)
    {
        int limit = Math.Clamp(maximumResults, 1, MaximumRetainedPerContext);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        Row[] rows = (await connection.QueryAsync<Row>(new CommandDefinition(
            SelectSql + """
             WHERE workspace_id = @workspaceId
               AND ((@goalId IS NULL AND goal_id IS NULL) OR goal_id = @goalId)
             ORDER BY started_at DESC, id DESC
             LIMIT @limit;
            """,
            new { workspaceId = workspaceId.Value, goalId = goalId?.Value, limit },
            cancellationToken: cancellationToken))).ToArray();
        return rows.Select(row => row.ToRecord()).ToArray();
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
                UPDATE developer_terminal_sessions
                SET state = 'Interrupted',
                    completed_at = @completedAt,
                    error_code = 'application_restarted',
                    error = 'Harness.NET restarted before this terminal session completed.'
                WHERE state = 'Running' AND started_at < @startedBefore;
                """, new
            {
                completedAt = completedAt.ToString("O"),
                startedBefore = startedBefore.ToString("O"),
            }, cancellationToken: cancellationToken));
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

    private static object Parameters(StoredTerminalSessionStart session) => new
    {
        id = session.Id.Value,
        workspaceId = session.WorkspaceId.Value,
        goalId = session.GoalId?.Value,
        sourceScope = session.SourceScope.ToString(),
        sourceBranch = session.SourceBranch?.Value,
        sourceDescription = session.SourceDescription.Value,
        workingDirectory = session.WorkingDirectory.Value,
        shellName = session.Shell.Value,
        environmentProfile = session.EnvironmentProfile.ToString(),
        contentPolicy = session.ContentPolicy.ToString(),
        columns = session.Dimensions.Columns,
        rows = session.Dimensions.Rows,
        startedAt = session.StartedAt.ToString("O"),
    };

    private static void Validate(StoredTerminalSessionStart session)
    {
        if (string.IsNullOrWhiteSpace(session.Id.Value) || session.Id.Value.Length > 80 ||
            string.IsNullOrWhiteSpace(session.WorkspaceId.Value) ||
            session.WorkspaceId.Value.Length > 128 ||
            session.GoalId is { Value.Length: > 128 } ||
            session.SourceBranch is { Value.Length: > 256 } ||
            string.IsNullOrWhiteSpace(session.SourceDescription.Value) ||
            session.SourceDescription.Value.Length > 512 ||
            session.WorkingDirectory.Value != "." ||
            string.IsNullOrWhiteSpace(session.Shell.Value) || session.Shell.Value.Length > 128 ||
            session.Shell.Value.Contains('/') || session.Shell.Value.Contains('\\') ||
            session.Dimensions.Columns is < 20 or > 400 ||
            session.Dimensions.Rows is < 5 or > 200 ||
            !Enum.IsDefined(session.SourceScope) ||
            !Enum.IsDefined(session.EnvironmentProfile) ||
            !Enum.IsDefined(session.ContentPolicy))
        {
            throw new ArgumentException("The terminal lifecycle metadata is invalid.", nameof(session));
        }
    }

    private static void ValidateCompletion(StoredTerminalSessionCompletion completion)
    {
        if (string.IsNullOrWhiteSpace(completion.Id.Value) || completion.Id.Value.Length > 80 ||
            !Enum.IsDefined(completion.State) ||
            completion.ErrorCode?.Length > 128 || completion.Error?.Length > 512)
        {
            throw new ArgumentException("The terminal completion metadata is invalid.",
                nameof(completion));
        }
    }

    private const string SelectSql = """
        SELECT id AS Id,
               workspace_id AS WorkspaceId,
               goal_id AS GoalId,
               source_scope AS SourceScope,
               source_branch AS SourceBranch,
               source_description AS SourceDescription,
               working_directory AS WorkingDirectory,
               shell_name AS ShellName,
               environment_profile AS EnvironmentProfile,
               content_policy AS ContentPolicy,
               columns AS Columns,
               rows AS Rows,
               state AS State,
               started_at AS StartedAt,
               completed_at AS CompletedAt,
               exit_code AS ExitCode,
               error_code AS ErrorCode,
               error AS Error
        FROM developer_terminal_sessions
        """;

    private sealed class Row
    {
        public string Id { get; init; } = string.Empty;
        public string WorkspaceId { get; init; } = string.Empty;
        public string? GoalId { get; init; }
        public string SourceScope { get; init; } = string.Empty;
        public string? SourceBranch { get; init; }
        public string SourceDescription { get; init; } = string.Empty;
        public string WorkingDirectory { get; init; } = string.Empty;
        public string ShellName { get; init; } = string.Empty;
        public string EnvironmentProfile { get; init; } = string.Empty;
        public string ContentPolicy { get; init; } = string.Empty;
        public long Columns { get; init; }
        public long Rows { get; init; }
        public string State { get; init; } = string.Empty;
        public string StartedAt { get; init; } = string.Empty;
        public string? CompletedAt { get; init; }
        public long? ExitCode { get; init; }
        public string? ErrorCode { get; init; }
        public string? Error { get; init; }

        internal StoredTerminalSession ToRecord() => new(
            new(Id), new(WorkspaceId), GoalId is null ? null : new(GoalId),
            Enum.Parse<StoredTerminalSourceScope>(SourceScope),
            SourceBranch is null ? null : new(SourceBranch),
            new(SourceDescription), new(WorkingDirectory), new(ShellName),
            Enum.Parse<StoredTerminalEnvironmentProfile>(EnvironmentProfile),
            Enum.Parse<StoredTerminalContentPolicy>(ContentPolicy),
            new(checked((int)Columns), checked((int)Rows)),
            Enum.Parse<StoredTerminalSessionState>(State),
            DateTimeOffset.Parse(StartedAt),
            CompletedAt is null ? null : DateTimeOffset.Parse(CompletedAt),
            ExitCode is null ? null : checked((int)ExitCode.Value),
            ErrorCode, Error);
    }
}
