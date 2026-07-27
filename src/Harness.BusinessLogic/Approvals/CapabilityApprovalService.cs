using Harness.DataAccess.Approvals;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Workspaces;
using Harness.DataAccess.Worktrees;

namespace Harness.BusinessLogic.Approvals;

internal sealed class CapabilityApprovalService(
    IGoalStore goalStore,
    IWorkspaceStore workspaceStore,
    ICapabilityApprovalStore approvalStore) : ICapabilityApprovalService
{
    private const int MaximumRationaleCharacters = 2 * 1024;
    private const int MaximumDecisionReasonCharacters = 4 * 1024;

    public async ValueTask<CapabilityApprovalResult> RequestAsync(
        CapabilityApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.CorrelationId is null ||
            string.IsNullOrWhiteSpace(request.CorrelationId.Value) ||
            request.CorrelationId.Value.Length > 128 ||
            request.Capability is not CapabilityKind.Restore ||
            string.IsNullOrWhiteSpace(request.Rationale) ||
            request.Rationale.Length > MaximumRationaleCharacters)
        {
            return Failure(
                "invalid_approval_request",
                "Restore approval requires a correlation identifier and a rationale of at most 2048 characters.");
        }

        GoalContext? context = await GetContextAsync(
            request.GoalId,
            requireTrust: true,
            cancellationToken);
        if (context is null)
        {
            return Failure(
                "goal_not_approved",
                "Restore approval requires an active, trusted goal worktree.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        StoredCapabilityApprovalStart started = await approvalStore.StartAsync(new(
            new(Guid.NewGuid().ToString("N")),
            context.Goal.Id,
            new(request.CorrelationId.Value),
            DataAccess.Approvals.CapabilityKind.Restore,
            Path.GetRelativePath(context.Workspace.RootPath, context.Workspace.EntryPoint),
            request.Rationale.Trim(),
            DataAccess.Approvals.CapabilityApprovalState.Pending,
            DecisionReason: null,
            now,
            DecidedAt: null), cancellationToken);
        return started.WasCreated
            ? Success(started.Approval)
            : new(
                started.Approval.ToView(),
                "duplicate_approval_request",
                "This goal already has a restore approval for that correlation identifier.");
    }

    public async ValueTask<CapabilityApprovalResult> DecideAsync(
        CapabilityDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ApprovalId is null ||
            !Guid.TryParseExact(request.ApprovalId.Value, "N", out _) ||
            !Enum.IsDefined(request.Decision) ||
            request.Reason?.Length > MaximumDecisionReasonCharacters ||
            (request.Decision is CapabilityDecision.Deny &&
             string.IsNullOrWhiteSpace(request.Reason)))
        {
            return Failure(
                "invalid_decision",
                "A valid decision is required, and denial requires a reason of at most 4096 characters.");
        }

        StoredCapabilityApproval? approval = await approvalStore.GetByIdAsync(
            new(request.ApprovalId.Value),
            cancellationToken);
        if (approval is null)
        {
            return Failure("approval_missing", "The capability approval does not exist.");
        }

        bool approve = request.Decision is CapabilityDecision.Approve;
        GoalContext? context = await GetContextAsync(
            approval.GoalId,
            requireTrust: approve,
            cancellationToken);
        if (context is null)
        {
            return Failure(
                approve ? "workspace_not_trusted" : "goal_not_approved",
                approve
                    ? "The active goal workspace must remain trusted before approval."
                    : "The goal worktree must remain active before a decision.");
        }

        try
        {
            StoredCapabilityApproval decided = await approvalStore.DecideAsync(
                approval.Id,
                DataAccess.Approvals.CapabilityApprovalState.Pending,
                approve
                    ? DataAccess.Approvals.CapabilityApprovalState.Approved
                    : DataAccess.Approvals.CapabilityApprovalState.Denied,
                string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
                DateTimeOffset.UtcNow,
                cancellationToken);
            return Success(decided);
        }
        catch (InvalidOperationException exception)
        {
            return Failure("invalid_transition", exception.Message);
        }
    }

    public async ValueTask<CapabilityApprovalSnapshot> ListAsync(
        string goalId,
        CancellationToken cancellationToken = default)
    {
        StoredGoal? goal = await goalStore.GetAsync(goalId, cancellationToken);
        RegisteredWorkspace? workspace = await workspaceStore.GetActiveAsync(cancellationToken);
        if (goal is null || workspace is null ||
            !workspace.Id.Equals(goal.WorkspaceId, StringComparison.Ordinal))
        {
            return new([], "workspace_not_active", "The goal workspace must be active.");
        }

        IReadOnlyList<StoredCapabilityApproval> approvals = await approvalStore.ListAsync(
            goal.Id,
            cancellationToken);
        return new(
            approvals.Select(approval => approval.ToView()).ToArray(),
            ErrorCode: null,
            Error: null);
    }

    private async ValueTask<GoalContext?> GetContextAsync(
        string goalId,
        bool requireTrust,
        CancellationToken cancellationToken)
    {
        StoredGoal? goal = await goalStore.GetAsync(goalId, cancellationToken);
        StoredGoalWorktree? worktree = await goalStore.GetWorktreeAsync(goalId, cancellationToken);
        RegisteredWorkspace? workspace = await workspaceStore.GetActiveAsync(cancellationToken);
        return goal?.State is "Approved" &&
               worktree?.State is "Active" &&
               workspace is not null &&
               workspace.Id.Equals(goal.WorkspaceId, StringComparison.Ordinal) &&
               worktree.WorkspaceId.Equals(workspace.Id, StringComparison.Ordinal) &&
               (!requireTrust || workspace.IsTrusted)
            ? new(goal, worktree, workspace)
            : null;
    }

    private static CapabilityApprovalResult Success(StoredCapabilityApproval approval) =>
        new(approval.ToView(), ErrorCode: null, Error: null);

    private static CapabilityApprovalResult Failure(string code, string error) =>
        new(null, code, error);

    private sealed record GoalContext(
        StoredGoal Goal,
        StoredGoalWorktree Worktree,
        RegisteredWorkspace Workspace);
}

internal static class StoredCapabilityApprovalMapping
{
    internal static CapabilityApprovalView ToView(this StoredCapabilityApproval approval) =>
        new(
            new(approval.Id.Value),
            approval.GoalId,
            new(approval.CorrelationId.Value),
            approval.Capability switch
            {
                DataAccess.Approvals.CapabilityKind.Restore => CapabilityKind.Restore,
                _ => throw new InvalidOperationException("The stored capability is unsupported."),
            },
            approval.Target,
            approval.Rationale,
            approval.State switch
            {
                DataAccess.Approvals.CapabilityApprovalState.Pending => CapabilityApprovalState.Pending,
                DataAccess.Approvals.CapabilityApprovalState.Approved => CapabilityApprovalState.Approved,
                DataAccess.Approvals.CapabilityApprovalState.Denied => CapabilityApprovalState.Denied,
                _ => throw new InvalidOperationException("The stored approval state is unsupported."),
            },
            approval.DecisionReason,
            approval.RequestedAt,
            approval.DecidedAt);
}
