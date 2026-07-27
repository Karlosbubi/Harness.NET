using System.Globalization;
using Dapper;
using Harness.DataAccess.Configuration;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.Evidence;

internal sealed class SqliteToolEvidenceStore(IApplicationPaths applicationPaths)
    : IToolEvidenceStore
{
    public async ValueTask<StoredToolCallStart> StartAsync(
        StoredToolCall toolCall,
        CancellationToken cancellationToken = default)
    {
        ValidateStart(toolCall);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        int inserted = await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO tool_calls (
                id, goal_id, correlation_id, tool_name, request_json, state,
                result_json, started_at, completed_at)
            VALUES (
                @Id, @GoalId, @CorrelationId, @ToolName, @RequestJson, @State,
                @ResultJson, @StartedAt, @CompletedAt)
            ON CONFLICT (goal_id, correlation_id) DO NOTHING;
            """, new
        {
            Id = toolCall.Id.Value,
            toolCall.GoalId,
            CorrelationId = toolCall.CorrelationId.Value,
            ToolName = toolCall.Tool.ToString(),
            toolCall.RequestJson,
            State = toolCall.State.ToString(),
            toolCall.ResultJson,
            StartedAt = Format(toolCall.StartedAt),
            CompletedAt = toolCall.CompletedAt is null ? null : Format(toolCall.CompletedAt.Value),
        }, cancellationToken: cancellationToken));
        ToolCallRow row = await ReadByCorrelationAsync(
            connection,
            toolCall.GoalId,
            toolCall.CorrelationId,
            cancellationToken);
        return new(row.ToRecord(), inserted == 1);
    }

    public async ValueTask<StoredToolCall> CompleteAsync(
        ToolCallId toolCallId,
        ToolCallState expectedState,
        ToolCallState nextState,
        string resultJson,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        if (toolCallId is null ||
            !Guid.TryParseExact(toolCallId.Value, "N", out _) ||
            expectedState is not ToolCallState.Running ||
            !Enum.IsDefined(nextState) ||
            nextState is ToolCallState.Running ||
            string.IsNullOrWhiteSpace(resultJson))
        {
            throw new ArgumentException("The tool completion contains invalid semantic values.");
        }

        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        ToolCallRow? row = await connection.QuerySingleOrDefaultAsync<ToolCallRow>(
            new CommandDefinition("""
                UPDATE tool_calls
                SET state = @nextState,
                    result_json = @resultJson,
                    completed_at = @completedAt
                WHERE id = @toolCallId AND state = @expectedState
                RETURNING id, goal_id AS GoalId, correlation_id AS CorrelationId,
                          tool_name AS ToolName, request_json AS RequestJson, state,
                          result_json AS ResultJson, started_at AS StartedAt,
                          completed_at AS CompletedAt;
                """, new
            {
                toolCallId = toolCallId.Value,
                expectedState = expectedState.ToString(),
                nextState = nextState.ToString(),
                resultJson,
                completedAt = Format(completedAt),
            }, cancellationToken: cancellationToken));
        return row?.ToRecord() ?? throw new InvalidOperationException(
            "The tool call state changed before its evidence was completed.");
    }

    public async ValueTask<IReadOnlyList<StoredToolCall>> ListAsync(
        string goalId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        IEnumerable<ToolCallRow> rows = await connection.QueryAsync<ToolCallRow>(
            new CommandDefinition(SelectSql + """
                 WHERE goal_id = @goalId
                 ORDER BY started_at, id;
                """, new { goalId }, cancellationToken: cancellationToken));
        return rows.Select(row => row.ToRecord()).ToArray();
    }

    private static async ValueTask<ToolCallRow> ReadByCorrelationAsync(
        SqliteConnection connection,
        string goalId,
        ToolCorrelationId correlationId,
        CancellationToken cancellationToken) =>
        await connection.QuerySingleAsync<ToolCallRow>(new CommandDefinition(
            SelectSql + " WHERE goal_id = @goalId AND correlation_id = @correlationId;",
            new { goalId, correlationId = correlationId.Value },
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

    private static void ValidateStart(StoredToolCall toolCall)
    {
        if (toolCall.Id is null ||
            toolCall.CorrelationId is null ||
            !Guid.TryParseExact(toolCall.Id.Value, "N", out _) ||
            string.IsNullOrWhiteSpace(toolCall.CorrelationId.Value) ||
            toolCall.CorrelationId.Value.Length > 128 ||
            !Enum.IsDefined(toolCall.Tool) ||
            toolCall.State is not ToolCallState.Running ||
            string.IsNullOrWhiteSpace(toolCall.RequestJson) ||
            toolCall.ResultJson is not null ||
            toolCall.CompletedAt is not null)
        {
            throw new ArgumentException("The tool request contains invalid semantic values.");
        }
    }

    private const string SelectSql = """
        SELECT id, goal_id AS GoalId, correlation_id AS CorrelationId,
               tool_name AS ToolName, request_json AS RequestJson, state,
               result_json AS ResultJson, started_at AS StartedAt,
               completed_at AS CompletedAt
        FROM tool_calls
        """;

    private sealed class ToolCallRow
    {
        public string Id { get; init; } = string.Empty;
        public string GoalId { get; init; } = string.Empty;
        public string CorrelationId { get; init; } = string.Empty;
        public string ToolName { get; init; } = string.Empty;
        public string RequestJson { get; init; } = string.Empty;
        public string State { get; init; } = string.Empty;
        public string? ResultJson { get; init; }
        public string StartedAt { get; init; } = string.Empty;
        public string? CompletedAt { get; init; }

        internal StoredToolCall ToRecord() => new(
            new(Id),
            GoalId,
            new(CorrelationId),
            Enum.Parse<ToolKind>(ToolName),
            RequestJson,
            Enum.Parse<ToolCallState>(State),
            ResultJson,
            DateTimeOffset.Parse(StartedAt, CultureInfo.InvariantCulture),
            CompletedAt is null
                ? null
                : DateTimeOffset.Parse(CompletedAt, CultureInfo.InvariantCulture));
    }
}
