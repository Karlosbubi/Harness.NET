using Harness.DataAccess.Commits;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Workflows;
using Harness.DataAccess.Workspaces;
using Harness.DataAccess.Worktrees;
using StoredApprovalState = Harness.DataAccess.Commits.GoalCommitApprovalState;
using StoredCheckpointKind = Harness.DataAccess.Workflows.GoalWorkflowCheckpointKind;
using StoredRunState = Harness.DataAccess.Workflows.GoalWorkflowRunState;

namespace Harness.BusinessLogic.Acceptance;

internal sealed class GoalAcceptanceService(
    IGoalStore goalStore,
    IWorkspaceStore workspaceStore,
    IGoalWorkflowStore workflowStore,
    IGoalCommitApprovalStore approvalStore,
    IGoalCommitter committer,
    TimeProvider timeProvider) : IGoalAcceptanceService
{
    private const int MaximumCommitMessageCharacters = 3800;
    private const int MaximumDecisionReasonCharacters = 4096;

    public async ValueTask<GoalCommitPreviewResult> PreviewAsync(
        Goals.GoalId goalId,
        CancellationToken cancellationToken = default)
    {
        AcceptanceContext? context = await GetContextAsync(goalId, cancellationToken);
        if (context is null)
        {
            return PreviewFailure(
                "goal_not_acceptable",
                "Commit preview requires an accepted review in an active trusted goal worktree.");
        }

        GoalCommitInspection inspection = await committer.InspectAsync(new(
            new(context.Worktree.Path), new(context.Worktree.Branch)), cancellationToken);
        if (inspection.Error is not null)
        {
            return PreviewFailure(inspection.ErrorCode!, inspection.Error);
        }

        return new(new(
            new(context.Goal.Id),
            new(context.Workflow.Run.Id.Value),
            new(inspection.Branch!.Value),
            new(inspection.Head!.Value),
            new(inspection.DiffSha256!.Value),
            new(inspection.Diff.Value),
            new(inspection.ChangedFileCount.Value)), ErrorCode: null, Error: null);
    }

    public async ValueTask<GoalCommitApprovalView?> GetAsync(
        Goals.GoalId goalId,
        Workflows.GoalWorkflowId runId,
        CancellationToken cancellationToken = default)
    {
        if (!ValidId(goalId?.Value) || !ValidId(runId?.Value))
        {
            return null;
        }

        StoredGoalCommitApproval? approval = await approvalStore.GetForRunAsync(
            new(goalId!.Value), new(runId!.Value), cancellationToken);
        return approval?.ToView();
    }

    public async ValueTask<GoalCommitApprovalResult> RequestAsync(
        GoalCommitApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        string? validationError = ValidateRequest(request);
        if (validationError is not null)
        {
            return Failure("invalid_commit_approval", validationError);
        }

        AcceptanceContext? context = await GetContextAsync(request.GoalId, cancellationToken);
        if (context is null || context.Workflow.Run.Id.Value != request.RunId.Value)
        {
            return Failure(
                "goal_not_acceptable",
                "The accepted review run and trusted goal worktree must remain current.");
        }

        GoalCommitInspection inspection = await committer.InspectAsync(new(
            new(context.Worktree.Path), new(context.Worktree.Branch)), cancellationToken);
        if (inspection.Error is not null)
        {
            return Failure(inspection.ErrorCode!, inspection.Error);
        }

        if (inspection.Head!.Value != request.ExpectedHead.Value ||
            inspection.DiffSha256!.Value != request.ExpectedDiffHash.Value)
        {
            return Failure(
                "preview_changed",
                "The branch HEAD or complete diff changed after the preview was displayed.");
        }

        string finalMessage = request.Message.Value.Trim() + "\n\n" +
            $"Harness-Diff-SHA256: {inspection.DiffSha256.Value}";
        if (finalMessage.Length > 4096)
        {
            return Failure(
                "invalid_commit_approval",
                "The commit message plus approval fingerprint exceeds 4096 characters.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        StoredGoalCommitApprovalStart started = await approvalStore.CreateAsync(new(
            new(Guid.NewGuid().ToString("N")),
            new(context.Goal.Id),
            context.Workflow.Run.Id,
            new(context.Worktree.Branch),
            inspection.Head,
            inspection.DiffSha256,
            inspection.Diff,
            inspection.ChangedFileCount,
            new(finalMessage),
            new(request.AuthorName.Value.Trim()),
            new(request.AuthorEmail.Value.Trim()),
            StoredApprovalState.Pending,
            DecisionReason: null,
            CommitSha: null,
            now,
            DecidedAt: null,
            CompletedAt: null), cancellationToken);
        return started.WasCreated
            ? Success(started.Approval)
            : new(started.Approval.ToView(), WasReconciled: false,
                "duplicate_commit_approval",
                "This workflow run already has a commit approval record.");
    }

    public async ValueTask<GoalCommitApprovalResult> DecideAsync(
        GoalCommitDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request?.ApprovalId is null || !ValidId(request.ApprovalId.Value) ||
            !Enum.IsDefined(request.Decision) ||
            request.Reason?.Value.Length > MaximumDecisionReasonCharacters ||
            (request.Decision is GoalCommitDecision.Deny &&
             string.IsNullOrWhiteSpace(request.Reason?.Value)))
        {
            return Failure(
                "invalid_commit_decision",
                "A valid decision is required; denial requires a reason of at most 4096 characters.");
        }

        StoredGoalCommitApproval? approval = await approvalStore.GetByIdAsync(
            new(request.ApprovalId.Value), cancellationToken);
        if (approval is null)
        {
            return Failure("commit_approval_missing", "The commit approval does not exist.");
        }

        if (request.Decision is GoalCommitDecision.Deny)
        {
            if (approval.State is not StoredApprovalState.Pending)
            {
                return Failure(
                    "invalid_commit_transition", "Only a pending commit may be denied.");
            }

            StoredGoalCommitApproval denied = await approvalStore.DecideAsync(
                approval.Id, StoredApprovalState.Pending, StoredApprovalState.Denied,
                new(request.Reason!.Value.Trim()), timeProvider.GetUtcNow(), cancellationToken);
            return Success(denied);
        }

        AcceptanceContext? context = await GetContextAsync(
            new(approval.GoalId.Value), cancellationToken);
        if (context is null || context.Workflow.Run.Id != approval.WorkflowRunId)
        {
            return Failure(
                "goal_not_acceptable",
                "The accepted review run and trusted goal worktree must remain current.");
        }

        if (approval.State is StoredApprovalState.Pending)
        {
            approval = await approvalStore.DecideAsync(
                approval.Id, StoredApprovalState.Pending, StoredApprovalState.Approved,
                string.IsNullOrWhiteSpace(request.Reason?.Value)
                    ? null
                    : new(request.Reason.Value.Trim()),
                timeProvider.GetUtcNow(), cancellationToken);
        }
        else if (approval.State is StoredApprovalState.Denied)
        {
            return Failure(
                "invalid_commit_transition", "A denied commit approval cannot be reused.");
        }

        if (approval.State is StoredApprovalState.Committed)
        {
            await CompleteWorkflowAsync(context.Workflow, approval, cancellationToken);
            return new(approval.ToView(), WasReconciled: true, ErrorCode: null, Error: null);
        }

        GoalCommitResult committed = await committer.CommitAsync(new(
            new(context.Worktree.Path),
            approval.Branch,
            approval.ExpectedHead,
            approval.DiffSha256,
            approval.CommitMessage,
            approval.AuthorName,
            approval.AuthorEmail,
            approval.DecidedAt!.Value), cancellationToken);
        if (committed.CommitSha is null)
        {
            return new(approval.ToView(), committed.WasReconciled,
                committed.ErrorCode, committed.Error);
        }

        StoredGoalCommitApproval completed = await approvalStore.CompleteAsync(
            approval.Id, StoredApprovalState.Approved, committed.CommitSha,
            timeProvider.GetUtcNow(), CancellationToken.None);
        await CompleteWorkflowAsync(context.Workflow, completed, CancellationToken.None);
        return new(completed.ToView(), committed.WasReconciled, ErrorCode: null, Error: null);
    }

    private async ValueTask<AcceptanceContext?> GetContextAsync(
        Goals.GoalId goalId,
        CancellationToken cancellationToken)
    {
        if (!ValidId(goalId?.Value))
        {
            return null;
        }

        StoredGoal? goal = await goalStore.GetAsync(goalId!.Value, cancellationToken);
        StoredGoalWorktree? worktree = await goalStore.GetWorktreeAsync(
            goalId.Value, cancellationToken);
        RegisteredWorkspace? workspace = await workspaceStore.GetActiveAsync(cancellationToken);
        StoredGoalWorkflowSnapshot? workflow = await workflowStore.GetLatestAsync(
            new(goalId.Value), cancellationToken);
        bool acceptedRun = workflow is not null &&
            ((workflow.Run.State is StoredRunState.AwaitingAcceptance &&
              workflow.Checkpoints[^1].Kind is StoredCheckpointKind.ReviewCompleted) ||
             (workflow.Run.State is StoredRunState.Completed &&
              workflow.Checkpoints[^1].Kind is StoredCheckpointKind.Accepted));
        return goal?.State != "Approved" || worktree?.State != "Active" ||
               workspace is not { IsTrusted: true } ||
               workspace.Id != goal.WorkspaceId || worktree.WorkspaceId != workspace.Id ||
               !acceptedRun
            ? null
            : new(goal, worktree, workflow!);
    }

    private async ValueTask CompleteWorkflowAsync(
        StoredGoalWorkflowSnapshot workflow,
        StoredGoalCommitApproval approval,
        CancellationToken cancellationToken)
    {
        StoredGoalWorkflowSnapshot current = await workflowStore.GetLatestAsync(
            approval.GoalId, cancellationToken) ?? workflow;
        if (current.Run.Id != workflow.Run.Id)
        {
            throw new InvalidOperationException(
                "The accepted workflow changed before commit completion was recorded.");
        }

        if (current.Run.State is StoredRunState.Completed &&
            current.Checkpoints[^1].Kind is StoredCheckpointKind.Accepted)
        {
            return;
        }

        await workflowStore.AppendAsync(new(
                Guid.NewGuid().ToString("N"),
                current.Run.Id,
                Sequence: 0,
                StoredCheckpointKind.Accepted,
                Harness.DataAccess.Workflows.WorkflowActor.System,
                new("User-approved work was committed to the isolated goal branch."),
                new("Accepted commit"),
                new($"Commit: {approval.CommitSha!.Value}\n" +
                    $"Branch: {approval.Branch.Value}\n" +
                    $"Reviewed diff SHA-256: {approval.DiffSha256.Value}\n" +
                    "No merge, rebase, or cherry-pick was performed."),
                timeProvider.GetUtcNow()),
            StoredCheckpointKind.ReviewCompleted,
            StoredRunState.AwaitingAcceptance,
            StoredRunState.Completed,
            cancellationToken);
    }

    private static string? ValidateRequest(GoalCommitApprovalRequest request)
    {
        if (request is null || !ValidId(request.GoalId?.Value) ||
            !ValidId(request.RunId?.Value) || request.ExpectedHead is null ||
            request.ExpectedDiffHash is null || request.Message is null ||
            request.AuthorName is null || request.AuthorEmail is null ||
            !ValidSha(request.ExpectedHead.Value, 40) ||
            !ValidSha(request.ExpectedDiffHash.Value, 64) ||
            string.IsNullOrWhiteSpace(request.Message.Value) ||
            request.Message.Value.Length > MaximumCommitMessageCharacters ||
            string.IsNullOrWhiteSpace(request.AuthorName.Value) ||
            request.AuthorName.Value.Length > 256 ||
            string.IsNullOrWhiteSpace(request.AuthorEmail.Value) ||
            request.AuthorEmail.Value.Length > 320 ||
            !request.AuthorEmail.Value.Contains('@', StringComparison.Ordinal))
        {
            return "The exact run, reviewed hashes, message, author name, and author email are required.";
        }

        return null;
    }

    private static bool ValidId(string? value) =>
        Guid.TryParseExact(value, "N", out _);

    private static bool ValidSha(string? value, int length) =>
        value?.Length == length && value.All(Uri.IsHexDigit);

    private static GoalCommitPreviewResult PreviewFailure(string code, string error) =>
        new(null, code, error);

    private static GoalCommitApprovalResult Success(StoredGoalCommitApproval approval) =>
        new(approval.ToView(), WasReconciled: false, ErrorCode: null, Error: null);

    private static GoalCommitApprovalResult Failure(string code, string error) =>
        new(null, WasReconciled: false, code, error);

    private sealed record AcceptanceContext(
        StoredGoal Goal,
        StoredGoalWorktree Worktree,
        StoredGoalWorkflowSnapshot Workflow);
}

internal static class StoredGoalCommitApprovalMapping
{
    internal static GoalCommitApprovalView ToView(this StoredGoalCommitApproval approval) => new(
        new(approval.Id.Value),
        new(approval.GoalId.Value),
        new(approval.WorkflowRunId.Value),
        new(approval.Branch.Value),
        new(approval.ExpectedHead.Value),
        new(approval.DiffSha256.Value),
        new(approval.Diff.Value),
        new(approval.ChangedFileCount.Value),
        new(approval.CommitMessage.Value),
        new(approval.AuthorName.Value),
        new(approval.AuthorEmail.Value),
        approval.State switch
        {
            StoredApprovalState.Pending => GoalCommitApprovalState.Pending,
            StoredApprovalState.Approved => GoalCommitApprovalState.Approved,
            StoredApprovalState.Denied => GoalCommitApprovalState.Denied,
            StoredApprovalState.Committed => GoalCommitApprovalState.Committed,
            _ => throw new ArgumentOutOfRangeException(nameof(approval)),
        },
        approval.DecisionReason is null ? null : new(approval.DecisionReason.Value),
        approval.CommitSha is null ? null : new(approval.CommitSha.Value),
        approval.RequestedAt,
        approval.DecidedAt,
        approval.CompletedAt);
}
