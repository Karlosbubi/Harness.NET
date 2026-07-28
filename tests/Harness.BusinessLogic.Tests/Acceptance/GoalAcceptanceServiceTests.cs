using Harness.BusinessLogic.Acceptance;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Workflows;
using Harness.DataAccess.Commits;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Workflows;
using Harness.DataAccess.Workspaces;
using Harness.DataAccess.Worktrees;
using StoredApprovalId = Harness.DataAccess.Commits.GoalCommitApprovalId;
using StoredApprovalState = Harness.DataAccess.Commits.GoalCommitApprovalState;
using ViewApprovalState = Harness.BusinessLogic.Acceptance.GoalCommitApprovalState;
using StoredKind = Harness.DataAccess.Workflows.GoalWorkflowCheckpointKind;
using StoredRunId = Harness.DataAccess.Workflows.GoalWorkflowRunId;
using StoredState = Harness.DataAccess.Workflows.GoalWorkflowRunState;

namespace Harness.BusinessLogic.Tests.Acceptance;

public sealed class GoalAcceptanceServiceTests
{
    [Fact]
    public async Task Explicit_approval_commits_exact_preview_and_completes_the_run()
    {
        Fixture fixture = new();
        GoalCommitPreview preview = Assert.IsType<GoalCommitPreview>(
            (await fixture.Service.PreviewAsync(fixture.GoalId)).Preview);
        GoalCommitApprovalResult requested = await fixture.Service.RequestAsync(new(
            fixture.GoalId,
            preview.RunId,
            preview.Head,
            preview.DiffHash,
            new("Implement accepted work"),
            new("User"),
            new("user@example.test")));

        Assert.Null(requested.Error);
        Assert.Equal(ViewApprovalState.Pending, requested.Approval?.State);
        Assert.Equal(0, fixture.Committer.CommitCount);

        GoalCommitApprovalResult committed = await fixture.Service.DecideAsync(new(
            requested.Approval!.Id,
            GoalCommitDecision.Approve,
            Reason: null));

        Assert.Null(committed.Error);
        Assert.Equal(ViewApprovalState.Committed, committed.Approval?.State);
        Assert.Equal(1, fixture.Committer.CommitCount);
        Assert.Contains(fixture.DiffHash, fixture.Committer.Request?.Message.Value,
            StringComparison.Ordinal);
        Assert.Equal(StoredState.Completed, fixture.Workflow.Snapshot.Run.State);
        Assert.Equal(StoredKind.Accepted, fixture.Workflow.Snapshot.Checkpoints[^1].Kind);
        Assert.Contains("No merge", fixture.Workflow.Snapshot.Checkpoints[^1]
            .EvidenceContent?.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Changed_diff_cannot_be_requested_or_committed()
    {
        Fixture fixture = new();
        GoalCommitPreview preview = Assert.IsType<GoalCommitPreview>(
            (await fixture.Service.PreviewAsync(fixture.GoalId)).Preview);
        fixture.Committer.Inspection = fixture.Committer.Inspection with
        {
            DiffSha256 = new(new string('e', 64)),
            Diff = new("changed diff"),
        };

        GoalCommitApprovalResult result = await fixture.Service.RequestAsync(new(
            fixture.GoalId, preview.RunId, preview.Head, preview.DiffHash,
            new("Commit"), new("User"), new("user@example.test")));

        Assert.Equal("preview_changed", result.ErrorCode);
        Assert.Null(fixture.Approvals.Approval);
        Assert.Equal(0, fixture.Committer.CommitCount);
    }

    [Fact]
    public async Task Denial_is_durable_and_never_invokes_the_committer()
    {
        Fixture fixture = new();
        GoalCommitPreview preview = Assert.IsType<GoalCommitPreview>(
            (await fixture.Service.PreviewAsync(fixture.GoalId)).Preview);
        GoalCommitApprovalResult requested = await fixture.Service.RequestAsync(new(
            fixture.GoalId, preview.RunId, preview.Head, preview.DiffHash,
            new("Commit"), new("User"), new("user@example.test")));

        GoalCommitApprovalResult denied = await fixture.Service.DecideAsync(new(
            requested.Approval!.Id, GoalCommitDecision.Deny, new("Needs another check.")));

        Assert.Equal(ViewApprovalState.Denied, denied.Approval?.State);
        Assert.Equal(0, fixture.Committer.CommitCount);
        Assert.Equal(StoredState.AwaitingAcceptance, fixture.Workflow.Snapshot.Run.State);
    }

    private sealed class Fixture
    {
        internal Fixture()
        {
            string workspaceId = Guid.NewGuid().ToString("N");
            string goalId = Guid.NewGuid().ToString("N");
            string runId = Guid.NewGuid().ToString("N");
            GoalId = new(goalId);
            DateTimeOffset now = DateTimeOffset.Parse("2026-07-28T22:00:00Z");
            StoredGoal goal = new(goalId, workspaceId, "Goal", "Objective", 2, null,
                "Approved", now, now);
            StoredGoalWorktree worktree = new(goalId, workspaceId, "harness/goal",
                "/worktree", new string('a', 40), "Active", now);
            RegisteredWorkspace workspace = new(workspaceId, "/repo", "repo",
                "/repo/Harness.slnx", true, true, "main", false, now, now);
            StoredRunId storedRunId = new(runId);
            Workflow = new(new(
                new(storedRunId, new(goalId), StoredState.AwaitingAcceptance, new(1), now, now),
                [new(
                    Guid.NewGuid().ToString("N"), storedRunId, 1,
                    StoredKind.ReviewCompleted,
                    Harness.DataAccess.Workflows.WorkflowActor.Reviewer,
                    new("Accepted"), new("Review"), new("accept"), now)]));
            Approvals = new();
            Committer = new();
            DiffHash = Committer.Inspection.DiffSha256!.Value;
            Service = new(
                new StubGoalStore(goal, worktree),
                new StubWorkspaceStore(workspace),
                Workflow,
                Approvals,
                Committer,
                new FixedTimeProvider());
        }

        internal GoalId GoalId { get; }
        internal string DiffHash { get; }
        internal InMemoryWorkflowStore Workflow { get; }
        internal InMemoryApprovalStore Approvals { get; }
        internal FakeCommitter Committer { get; }
        internal GoalAcceptanceService Service { get; }
    }

    private sealed class FakeCommitter : IGoalCommitter
    {
        internal GoalCommitInspection Inspection { get; set; } = new(
            new("harness/goal"), new(new string('a', 40)), new(new string('d', 64)),
            new("diff --git a/file b/file"), new(1), null, null);
        internal int CommitCount { get; private set; }
        internal GoalCommitRequest? Request { get; private set; }

        public ValueTask<GoalCommitInspection> InspectAsync(
            GoalCommitInspectionRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(Inspection);

        public ValueTask<GoalCommitResult> CommitAsync(
            GoalCommitRequest request,
            CancellationToken cancellationToken = default)
        {
            CommitCount++;
            Request = request;
            return ValueTask.FromResult(new GoalCommitResult(
                new(new string('c', 40)), false, null, null));
        }
    }

    private sealed class InMemoryApprovalStore : IGoalCommitApprovalStore
    {
        internal StoredGoalCommitApproval? Approval { get; private set; }

        public ValueTask<StoredGoalCommitApproval?> GetForRunAsync(
            GoalWorkflowGoalId goalId,
            GoalWorkflowRunId workflowRunId,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(Approval);

        public ValueTask<StoredGoalCommitApproval?> GetByIdAsync(
            StoredApprovalId approvalId,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(Approval);

        public ValueTask<StoredGoalCommitApprovalStart> CreateAsync(
            StoredGoalCommitApproval approval,
            CancellationToken cancellationToken = default)
        {
            bool created = Approval is null;
            Approval ??= approval;
            return ValueTask.FromResult(new StoredGoalCommitApprovalStart(Approval, created));
        }

        public ValueTask<StoredGoalCommitApproval> DecideAsync(
            StoredApprovalId approvalId,
            StoredApprovalState expectedState,
            StoredApprovalState nextState,
            Harness.DataAccess.Commits.GoalCommitDecisionReason? decisionReason,
            DateTimeOffset decidedAt,
            CancellationToken cancellationToken = default)
        {
            Approval = Approval! with
            {
                State = nextState,
                DecisionReason = decisionReason,
                DecidedAt = decidedAt,
            };
            return ValueTask.FromResult(Approval);
        }

        public ValueTask<StoredGoalCommitApproval> CompleteAsync(
            StoredApprovalId approvalId,
            StoredApprovalState expectedState,
            GitCommitSha commitSha,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken = default)
        {
            Approval = Approval! with
            {
                State = StoredApprovalState.Committed,
                CommitSha = commitSha,
                CompletedAt = completedAt,
            };
            return ValueTask.FromResult(Approval);
        }
    }

    private sealed class InMemoryWorkflowStore(StoredGoalWorkflowSnapshot snapshot)
        : IGoalWorkflowStore
    {
        internal StoredGoalWorkflowSnapshot Snapshot { get; private set; } = snapshot;

        public ValueTask<StoredGoalWorkflowSnapshot?> GetLatestAsync(
            GoalWorkflowGoalId goalId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<StoredGoalWorkflowSnapshot?>(Snapshot);

        public ValueTask<StoredGoalWorkflowSnapshot> AppendAsync(
            StoredGoalWorkflowCheckpoint checkpoint,
            StoredKind expectedCheckpoint,
            StoredState expectedState,
            StoredState nextState,
            CancellationToken cancellationToken = default,
            GoalWorkflowReviewCycle? nextReviewCycle = null)
        {
            StoredGoalWorkflowCheckpoint appended = checkpoint with
            {
                Sequence = Snapshot.Checkpoints.Count + 1,
            };
            Snapshot = new(
                Snapshot.Run with
                {
                    State = nextState,
                    ReviewCycle = nextReviewCycle ?? Snapshot.Run.ReviewCycle,
                    UpdatedAt = checkpoint.CreatedAt,
                },
                [.. Snapshot.Checkpoints, appended]);
            return ValueTask.FromResult(Snapshot);
        }

        public ValueTask<StoredGoalWorkflowSnapshot> StartAsync(
            StoredGoalWorkflowRun run,
            StoredGoalWorkflowCheckpoint checkpoint,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubGoalStore(
        StoredGoal goal,
        StoredGoalWorktree worktree) : IGoalStore
    {
        public ValueTask<StoredGoal?> GetAsync(
            string goalId,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<StoredGoal?>(goal);

        public ValueTask<StoredGoalWorktree?> GetWorktreeAsync(
            string goalId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<StoredGoalWorktree?>(worktree);

        public ValueTask<StoredGoal> CreateAsync(StoredGoal value,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<StoredGoal>> ListAsync(string workspaceId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<StoredPlan?> GetCurrentPlanAsync(string goalId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<StoredPlanSnapshot> SavePlanAsync(StoredPlan plan,
            string expectedGoalState, string nextGoalState,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<StoredPlanSnapshot> DecidePlanAsync(StoredApproval approval,
            StoredGoalWorktree? value, string expectedGoalState, string expectedPlanState,
            string nextGoalState, string nextPlanState,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubWorkspaceStore(RegisteredWorkspace workspace) : IWorkspaceStore
    {
        public ValueTask<RegisteredWorkspace?> GetActiveAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<RegisteredWorkspace?>(workspace);
        public ValueTask<RegisteredWorkspace> SaveAsync(WorkspaceInspection inspection,
            string entryPoint, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<RegisteredWorkspace?> FindByPathAsync(string rootPath,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<RegisteredWorkspace>> ListAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RegisteredWorkspace> SetActiveAsync(string workspaceId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RegisteredWorkspace> SetTrustAsync(string workspaceId, bool isTrusted,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            DateTimeOffset.Parse("2026-07-28T22:00:00Z");
    }
}
