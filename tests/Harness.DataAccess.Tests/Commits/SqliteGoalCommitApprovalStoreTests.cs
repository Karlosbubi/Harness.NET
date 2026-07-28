using Dapper;
using Harness.DataAccess.Commits;
using Harness.DataAccess.Configuration;
using Harness.DataAccess.Persistence;
using Harness.DataAccess.Workflows;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.Tests.Commits;

public sealed class SqliteGoalCommitApprovalStoreTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "harness-commit-store-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Persists_exact_approval_decision_and_commit_result()
    {
        (SqliteGoalCommitApprovalStore store, string goalId, string runId) =
            await CreateStoreAsync();
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-28T21:00:00Z");
        StoredGoalCommitApproval approval = Approval(goalId, runId, now);

        StoredGoalCommitApprovalStart started = await store.CreateAsync(approval);
        StoredGoalCommitApproval approved = await store.DecideAsync(
            approval.Id, GoalCommitApprovalState.Pending, GoalCommitApprovalState.Approved,
            decisionReason: null, now.AddMinutes(1));
        StoredGoalCommitApproval completed = await store.CompleteAsync(
            approval.Id, GoalCommitApprovalState.Approved, new(new string('c', 40)),
            now.AddMinutes(2));

        Assert.True(started.WasCreated);
        Assert.Equal(GoalCommitApprovalState.Approved, approved.State);
        Assert.Equal(GoalCommitApprovalState.Committed, completed.State);
        Assert.Equal(new string('c', 40), completed.CommitSha?.Value);
        Assert.Equal(new string('d', 64), completed.DiffSha256.Value);
    }

    [Fact]
    public async Task Duplicate_run_approval_returns_the_original_fingerprint()
    {
        (SqliteGoalCommitApprovalStore store, string goalId, string runId) =
            await CreateStoreAsync();
        StoredGoalCommitApproval approval = Approval(goalId, runId, DateTimeOffset.UtcNow);
        await store.CreateAsync(approval);

        StoredGoalCommitApprovalStart duplicate = await store.CreateAsync(approval with
        {
            Id = new(Guid.NewGuid().ToString("N")),
            DiffSha256 = new(new string('e', 64)),
        });

        Assert.False(duplicate.WasCreated);
        Assert.Equal(approval.Id, duplicate.Approval.Id);
        Assert.Equal(new string('d', 64), duplicate.Approval.DiffSha256.Value);
    }

    private async ValueTask<(SqliteGoalCommitApprovalStore Store, string GoalId, string RunId)>
        CreateStoreAsync()
    {
        ApplicationPaths paths = new(
            Path.Combine(root, "config"), Path.Combine(root, "data"),
            Path.Combine(root, "state"), Path.Combine(root, "cache"),
            Path.Combine(root, "data", "harness.db"), Path.Combine(root, "state", "logs"),
            Path.Combine(root, "state", "worktrees"));
        StubApplicationPaths applicationPaths = new(paths);
        await new SqliteDatabaseInitializer(applicationPaths).InitializeAsync();
        string workspaceId = Guid.NewGuid().ToString("N");
        string goalId = Guid.NewGuid().ToString("N");
        string runId = Guid.NewGuid().ToString("N");
        string now = DateTimeOffset.UtcNow.ToString("O");
        await using SqliteConnection connection = new($"Data Source={paths.DatabasePath}");
        await connection.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO workspaces (
                id, root_path, name, entry_point, is_trusted, is_active,
                branch, is_dirty, created_at, updated_at)
            VALUES (@workspaceId, '/repo', 'repo', '/repo/Harness.slnx', 1, 1,
                    'main', 0, @now, @now);
            INSERT INTO goals (
                id, workspace_id, title, objective, review_cycle_limit,
                remote_budget_microusd, state, created_at, updated_at)
            VALUES (@goalId, @workspaceId, 'Goal', 'Objective', 2, NULL,
                    'Approved', @now, @now);
            INSERT INTO goal_workflow_runs (
                id, goal_id, state, review_cycle, created_at, updated_at)
            VALUES (@runId, @goalId, 'AwaitingAcceptance', 1, @now, @now);
            """, new { workspaceId, goalId, runId, now });
        return (new(applicationPaths), goalId, runId);
    }

    private static StoredGoalCommitApproval Approval(
        string goalId,
        string runId,
        DateTimeOffset now) => new(
        new(Guid.NewGuid().ToString("N")), new(goalId), new(runId), new("harness/goal"),
        new(new string('a', 40)), new(new string('d', 64)), new("diff"), new(1),
        new("Commit\n\nHarness-Diff-SHA256: " + new string('d', 64)),
        new("User"), new("user@example.test"), GoalCommitApprovalState.Pending,
        DecisionReason: null, CommitSha: null, now, DecidedAt: null, CompletedAt: null);

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class StubApplicationPaths(ApplicationPaths current) : IApplicationPaths
    {
        public ApplicationPaths Current { get; } = current;
    }
}
