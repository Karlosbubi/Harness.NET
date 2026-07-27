using System.Globalization;
using Dapper;
using Harness.DataAccess.Configuration;
using Harness.DataAccess.Tools;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.Approvals;

internal sealed class SqliteCapabilityApprovalStore(IApplicationPaths applicationPaths)
    : ICapabilityApprovalStore
{
    public async ValueTask<StoredCapabilityApprovalStart> StartAsync(
        StoredCapabilityApproval approval,
        CancellationToken cancellationToken = default)
    {
        ValidateStart(approval);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        int inserted = await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO capability_approvals (
                id, goal_id, correlation_id, capability, target, rationale, state,
                decision_reason, requested_at, decided_at)
            VALUES (
                @Id, @GoalId, @CorrelationId, @Capability, @Target, @Rationale, @State,
                @DecisionReason, @RequestedAt, @DecidedAt)
            ON CONFLICT (goal_id, correlation_id, capability) DO NOTHING;
            """, new
        {
            Id = approval.Id.Value,
            approval.GoalId,
            CorrelationId = approval.CorrelationId.Value,
            Capability = approval.Capability.ToString(),
            approval.Target,
            approval.Rationale,
            State = approval.State.ToString(),
            approval.DecisionReason,
            RequestedAt = Format(approval.RequestedAt),
            DecidedAt = approval.DecidedAt is null ? null : Format(approval.DecidedAt.Value),
        }, cancellationToken: cancellationToken));
        StoredCapabilityApproval stored = (await GetAsync(
            approval.GoalId,
            approval.CorrelationId,
            approval.Capability,
            cancellationToken))!;
        return new(stored, inserted == 1);
    }

    public async ValueTask<StoredCapabilityApproval> DecideAsync(
        CapabilityApprovalId approvalId,
        CapabilityApprovalState expectedState,
        CapabilityApprovalState nextState,
        string? decisionReason,
        DateTimeOffset decidedAt,
        CancellationToken cancellationToken = default)
    {
        ValidateDecision(approvalId, expectedState, nextState, decisionReason);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        ApprovalRow? row = await connection.QuerySingleOrDefaultAsync<ApprovalRow>(
            new CommandDefinition("""
                UPDATE capability_approvals
                SET state = @nextState,
                    decision_reason = @decisionReason,
                    decided_at = @decidedAt
                WHERE id = @approvalId AND state = @expectedState
                RETURNING id, goal_id AS GoalId, correlation_id AS CorrelationId,
                          capability, target, rationale, state,
                          decision_reason AS DecisionReason,
                          requested_at AS RequestedAt, decided_at AS DecidedAt;
                """, new
            {
                approvalId = approvalId.Value,
                expectedState = expectedState.ToString(),
                nextState = nextState.ToString(),
                decisionReason,
                decidedAt = Format(decidedAt),
            }, cancellationToken: cancellationToken));
        return row?.ToRecord() ?? throw new InvalidOperationException(
            "The capability approval state changed before the decision was saved.");
    }

    public async ValueTask<StoredCapabilityApproval?> GetByIdAsync(
        CapabilityApprovalId approvalId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        ApprovalRow? row = await connection.QuerySingleOrDefaultAsync<ApprovalRow>(
            new CommandDefinition(
                SelectSql + " WHERE id = @approvalId;",
                new { approvalId = approvalId.Value },
                cancellationToken: cancellationToken));
        return row?.ToRecord();
    }

    public async ValueTask<StoredCapabilityApproval?> GetAsync(
        string goalId,
        ToolCorrelationId correlationId,
        CapabilityKind capability,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        ApprovalRow? row = await connection.QuerySingleOrDefaultAsync<ApprovalRow>(
            new CommandDefinition(
                SelectSql + """
                     WHERE goal_id = @goalId
                       AND correlation_id = @correlationId
                       AND capability = @capability;
                    """,
                new
                {
                    goalId,
                    correlationId = correlationId.Value,
                    capability = capability.ToString(),
                },
                cancellationToken: cancellationToken));
        return row?.ToRecord();
    }

    public async ValueTask<IReadOnlyList<StoredCapabilityApproval>> ListAsync(
        string goalId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        IEnumerable<ApprovalRow> rows = await connection.QueryAsync<ApprovalRow>(
            new CommandDefinition(SelectSql + """
                 WHERE goal_id = @goalId
                 ORDER BY requested_at, id;
                """, new { goalId }, cancellationToken: cancellationToken));
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

    private static void ValidateStart(StoredCapabilityApproval approval)
    {
        if (approval.Id is null ||
            approval.CorrelationId is null ||
            !Guid.TryParseExact(approval.Id.Value, "N", out _) ||
            string.IsNullOrWhiteSpace(approval.GoalId) ||
            string.IsNullOrWhiteSpace(approval.CorrelationId.Value) ||
            approval.CorrelationId.Value.Length > 128 ||
            !Enum.IsDefined(approval.Capability) ||
            string.IsNullOrWhiteSpace(approval.Target) ||
            approval.Target.Length > 4 * 1024 ||
            Path.IsPathRooted(approval.Target) ||
            approval.Target.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => segment.Equals("..", StringComparison.Ordinal)) ||
            string.IsNullOrWhiteSpace(approval.Rationale) ||
            approval.Rationale.Length > 2 * 1024 ||
            approval.State is not CapabilityApprovalState.Pending ||
            approval.DecisionReason is not null ||
            approval.DecidedAt is not null)
        {
            throw new ArgumentException("The capability approval request contains invalid semantic values.");
        }
    }

    private static void ValidateDecision(
        CapabilityApprovalId approvalId,
        CapabilityApprovalState expectedState,
        CapabilityApprovalState nextState,
        string? decisionReason)
    {
        bool validNextState = nextState is CapabilityApprovalState.Approved or
            CapabilityApprovalState.Denied;
        if (approvalId is null ||
            !Guid.TryParseExact(approvalId.Value, "N", out _) ||
            expectedState is not CapabilityApprovalState.Pending ||
            !validNextState ||
            decisionReason?.Length > 4 * 1024 ||
            (nextState is CapabilityApprovalState.Denied &&
             string.IsNullOrWhiteSpace(decisionReason)))
        {
            throw new ArgumentException("The capability approval decision contains invalid semantic values.");
        }
    }

    private static string Format(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    private const string SelectSql = """
        SELECT id, goal_id AS GoalId, correlation_id AS CorrelationId,
               capability, target, rationale, state,
               decision_reason AS DecisionReason,
               requested_at AS RequestedAt, decided_at AS DecidedAt
        FROM capability_approvals
        """;

    private sealed class ApprovalRow
    {
        public string Id { get; init; } = string.Empty;
        public string GoalId { get; init; } = string.Empty;
        public string CorrelationId { get; init; } = string.Empty;
        public string Capability { get; init; } = string.Empty;
        public string Target { get; init; } = string.Empty;
        public string Rationale { get; init; } = string.Empty;
        public string State { get; init; } = string.Empty;
        public string? DecisionReason { get; init; }
        public string RequestedAt { get; init; } = string.Empty;
        public string? DecidedAt { get; init; }

        internal StoredCapabilityApproval ToRecord() => new(
            new(Id),
            GoalId,
            new(CorrelationId),
            Enum.Parse<CapabilityKind>(Capability),
            Target,
            Rationale,
            Enum.Parse<CapabilityApprovalState>(State),
            DecisionReason,
            DateTimeOffset.Parse(RequestedAt, CultureInfo.InvariantCulture),
            DecidedAt is null
                ? null
                : DateTimeOffset.Parse(DecidedAt, CultureInfo.InvariantCulture));
    }
}
