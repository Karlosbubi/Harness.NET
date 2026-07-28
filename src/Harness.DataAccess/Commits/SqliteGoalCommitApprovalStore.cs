using System.Globalization;
using Dapper;
using Harness.DataAccess.Configuration;
using Harness.DataAccess.Workflows;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.Commits;

internal sealed class SqliteGoalCommitApprovalStore(IApplicationPaths applicationPaths)
    : IGoalCommitApprovalStore
{
    public async ValueTask<StoredGoalCommitApproval?> GetForRunAsync(
        GoalWorkflowGoalId goalId,
        GoalWorkflowRunId workflowRunId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        ApprovalRow? row = await connection.QuerySingleOrDefaultAsync<ApprovalRow>(
            new CommandDefinition(SelectSql +
                " WHERE goal_id = @goalId AND workflow_run_id = @workflowRunId;",
                new { goalId = goalId.Value, workflowRunId = workflowRunId.Value },
                cancellationToken: cancellationToken));
        return row?.ToRecord();
    }

    public async ValueTask<StoredGoalCommitApproval?> GetByIdAsync(
        GoalCommitApprovalId approvalId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        ApprovalRow? row = await connection.QuerySingleOrDefaultAsync<ApprovalRow>(
            new CommandDefinition(SelectSql + " WHERE id = @id;",
                new { id = approvalId.Value }, cancellationToken: cancellationToken));
        return row?.ToRecord();
    }

    public async ValueTask<StoredGoalCommitApprovalStart> CreateAsync(
        StoredGoalCommitApproval approval,
        CancellationToken cancellationToken = default)
    {
        ValidateNew(approval);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        int inserted = await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO goal_commit_approvals (
                id, goal_id, workflow_run_id, branch, expected_head, diff_sha256,
                diff_text, changed_file_count, commit_message, author_name, author_email,
                state, decision_reason, commit_sha, requested_at, decided_at, completed_at)
            VALUES (
                @Id, @GoalId, @WorkflowRunId, @Branch, @ExpectedHead, @DiffSha256,
                @Diff, @ChangedFileCount, @CommitMessage, @AuthorName, @AuthorEmail,
                @State, NULL, NULL, @RequestedAt, NULL, NULL)
            ON CONFLICT (goal_id, workflow_run_id) DO NOTHING;
            """, Parameters(approval), cancellationToken: cancellationToken));
        StoredGoalCommitApproval stored = (await GetForRunAsync(
            approval.GoalId, approval.WorkflowRunId, cancellationToken))!;
        return new(stored, inserted == 1);
    }

    public async ValueTask<StoredGoalCommitApproval> DecideAsync(
        GoalCommitApprovalId approvalId,
        GoalCommitApprovalState expectedState,
        GoalCommitApprovalState nextState,
        GoalCommitDecisionReason? decisionReason,
        DateTimeOffset decidedAt,
        CancellationToken cancellationToken = default)
    {
        bool denied = nextState is GoalCommitApprovalState.Denied;
        if (!ValidId(approvalId?.Value) ||
            expectedState is not GoalCommitApprovalState.Pending ||
            nextState is not (GoalCommitApprovalState.Approved or GoalCommitApprovalState.Denied) ||
            (denied && string.IsNullOrWhiteSpace(decisionReason?.Value)) ||
            decisionReason?.Value.Length > 4096)
        {
            throw new ArgumentException("The commit approval decision is invalid.");
        }

        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        ApprovalRow? row = await connection.QuerySingleOrDefaultAsync<ApprovalRow>(
            new CommandDefinition("""
                UPDATE goal_commit_approvals
                SET state = @nextState, decision_reason = @decisionReason,
                    decided_at = @decidedAt
                WHERE id = @id AND state = @expectedState
                RETURNING id, goal_id AS GoalId, workflow_run_id AS WorkflowRunId,
                    branch, expected_head AS ExpectedHead, diff_sha256 AS DiffSha256,
                    diff_text AS Diff, changed_file_count AS ChangedFileCount,
                    commit_message AS CommitMessage, author_name AS AuthorName,
                    author_email AS AuthorEmail, state,
                    decision_reason AS DecisionReason, commit_sha AS CommitSha,
                    requested_at AS RequestedAt, decided_at AS DecidedAt,
                    completed_at AS CompletedAt;
                """, new
            {
                id = approvalId!.Value,
                expectedState = expectedState.ToString(),
                nextState = nextState.ToString(),
                decisionReason = string.IsNullOrWhiteSpace(decisionReason?.Value)
                    ? null
                    : decisionReason.Value.Trim(),
                decidedAt = Format(decidedAt),
            }, cancellationToken: cancellationToken));
        return row?.ToRecord() ?? throw new InvalidOperationException(
            "The commit approval changed before the decision was persisted.");
    }

    public async ValueTask<StoredGoalCommitApproval> CompleteAsync(
        GoalCommitApprovalId approvalId,
        GoalCommitApprovalState expectedState,
        GitCommitSha commitSha,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        if (!ValidId(approvalId?.Value) ||
            expectedState is not GoalCommitApprovalState.Approved ||
            commitSha is null || !ValidSha(commitSha.Value, 40))
        {
            throw new ArgumentException("The completed commit result is invalid.");
        }

        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        ApprovalRow? row = await connection.QuerySingleOrDefaultAsync<ApprovalRow>(
            new CommandDefinition("""
                UPDATE goal_commit_approvals
                SET state = 'Committed', commit_sha = @commitSha, completed_at = @completedAt
                WHERE id = @id AND state = @expectedState
                RETURNING id, goal_id AS GoalId, workflow_run_id AS WorkflowRunId,
                    branch, expected_head AS ExpectedHead, diff_sha256 AS DiffSha256,
                    diff_text AS Diff, changed_file_count AS ChangedFileCount,
                    commit_message AS CommitMessage, author_name AS AuthorName,
                    author_email AS AuthorEmail, state,
                    decision_reason AS DecisionReason, commit_sha AS CommitSha,
                    requested_at AS RequestedAt, decided_at AS DecidedAt,
                    completed_at AS CompletedAt;
                """, new
            {
                id = approvalId!.Value,
                commitSha = commitSha.Value,
                completedAt = Format(completedAt),
                expectedState = expectedState.ToString(),
            }, cancellationToken: cancellationToken));
        return row?.ToRecord() ?? throw new InvalidOperationException(
            "The approved commit changed before completion was persisted.");
    }

    private static void ValidateNew(StoredGoalCommitApproval approval)
    {
        if (!ValidId(approval.Id?.Value) || !ValidId(approval.GoalId?.Value) ||
            !ValidId(approval.WorkflowRunId?.Value) ||
            string.IsNullOrWhiteSpace(approval.Branch?.Value) ||
            !ValidSha(approval.ExpectedHead?.Value, 40) ||
            !ValidSha(approval.DiffSha256?.Value, 64) ||
            string.IsNullOrWhiteSpace(approval.Diff?.Value) ||
            approval.Diff.Value.Length > 1024 * 1024 ||
            approval.ChangedFileCount is null || approval.ChangedFileCount.Value <= 0 ||
            string.IsNullOrWhiteSpace(approval.CommitMessage?.Value) ||
            approval.CommitMessage.Value.Length > 4096 ||
            string.IsNullOrWhiteSpace(approval.AuthorName?.Value) ||
            approval.AuthorName.Value.Length > 256 ||
            string.IsNullOrWhiteSpace(approval.AuthorEmail?.Value) ||
            approval.AuthorEmail.Value.Length > 320 ||
            approval.State is not GoalCommitApprovalState.Pending ||
            approval.DecisionReason is not null || approval.CommitSha is not null ||
            approval.DecidedAt is not null || approval.CompletedAt is not null)
        {
            throw new ArgumentException("The new commit approval is invalid.");
        }
    }

    private static object Parameters(StoredGoalCommitApproval value) => new
    {
        Id = value.Id.Value,
        GoalId = value.GoalId.Value,
        WorkflowRunId = value.WorkflowRunId.Value,
        Branch = value.Branch.Value,
        ExpectedHead = value.ExpectedHead.Value,
        DiffSha256 = value.DiffSha256.Value,
        Diff = value.Diff.Value,
        ChangedFileCount = value.ChangedFileCount.Value,
        CommitMessage = value.CommitMessage.Value,
        AuthorName = value.AuthorName.Value,
        AuthorEmail = value.AuthorEmail.Value,
        State = value.State.ToString(),
        RequestedAt = Format(value.RequestedAt),
    };

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

    private static bool ValidId(string? value) =>
        Guid.TryParseExact(value, "N", out _);

    private static bool ValidSha(string? value, int length) =>
        value?.Length == length && value.All(Uri.IsHexDigit);

    private static string Format(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    private const string SelectSql = """
        SELECT id, goal_id AS GoalId, workflow_run_id AS WorkflowRunId,
               branch, expected_head AS ExpectedHead, diff_sha256 AS DiffSha256,
               diff_text AS Diff, changed_file_count AS ChangedFileCount,
               commit_message AS CommitMessage, author_name AS AuthorName,
               author_email AS AuthorEmail, state,
               decision_reason AS DecisionReason, commit_sha AS CommitSha,
               requested_at AS RequestedAt, decided_at AS DecidedAt,
               completed_at AS CompletedAt
        FROM goal_commit_approvals
        """;

    private sealed class ApprovalRow
    {
        public string Id { get; init; } = string.Empty;
        public string GoalId { get; init; } = string.Empty;
        public string WorkflowRunId { get; init; } = string.Empty;
        public string Branch { get; init; } = string.Empty;
        public string ExpectedHead { get; init; } = string.Empty;
        public string DiffSha256 { get; init; } = string.Empty;
        public string Diff { get; init; } = string.Empty;
        public int ChangedFileCount { get; init; }
        public string CommitMessage { get; init; } = string.Empty;
        public string AuthorName { get; init; } = string.Empty;
        public string AuthorEmail { get; init; } = string.Empty;
        public string State { get; init; } = string.Empty;
        public string? DecisionReason { get; init; }
        public string? CommitSha { get; init; }
        public string RequestedAt { get; init; } = string.Empty;
        public string? DecidedAt { get; init; }
        public string? CompletedAt { get; init; }

        internal StoredGoalCommitApproval ToRecord() => new(
            new(Id), new(GoalId), new(WorkflowRunId), new(Branch), new(ExpectedHead),
            new(DiffSha256), new(Diff), new(ChangedFileCount), new(CommitMessage),
            new(AuthorName), new(AuthorEmail), Enum.Parse<GoalCommitApprovalState>(State),
            DecisionReason is null ? null : new(DecisionReason),
            CommitSha is null ? null : new(CommitSha),
            DateTimeOffset.Parse(RequestedAt, CultureInfo.InvariantCulture),
            DecidedAt is null ? null : DateTimeOffset.Parse(
                DecidedAt, CultureInfo.InvariantCulture),
            CompletedAt is null ? null : DateTimeOffset.Parse(
                CompletedAt, CultureInfo.InvariantCulture));
    }
}
