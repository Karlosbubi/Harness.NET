using Harness.DataAccess.Goals;
using Harness.DataAccess.Workspaces;
using Harness.DataAccess.Worktrees;

namespace Harness.BusinessLogic.Goals;

internal sealed class GoalService(
    IGoalStore goalStore,
    IWorkspaceStore workspaceStore,
    IGoalWorktreeManager worktreeManager) : IGoalService
{
    private const int MaximumTitleCharacters = 160;
    private const int MaximumObjectiveCharacters = 16 * 1024;
    private const int MaximumPlanCharacters = 64 * 1024;
    private const int MaximumDecisionReasonCharacters = 4 * 1024;

    public async ValueTask<GoalResult> CreateAsync(
        GoalCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        string? validationError = Validate(request);
        if (validationError is not null)
        {
            return new(null, "invalid_goal", validationError);
        }

        RegisteredWorkspace? workspace = await workspaceStore.GetActiveAsync(cancellationToken);
        if (workspace is null || !workspace.Id.Equals(request.WorkspaceId, StringComparison.Ordinal))
        {
            return new(null, "workspace_not_active", "The goal workspace must be active.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        StoredGoal stored = await goalStore.CreateAsync(new(
            Guid.NewGuid().ToString("N"),
            workspace.Id,
            request.Title.Trim(),
            request.Objective.Trim(),
            request.ReviewCycleLimit.Value,
            request.RemoteBudget?.Value,
            "Draft",
            now,
            now), cancellationToken);
        return new(stored.ToView(), ErrorCode: null, Error: null);
    }

    public async ValueTask<GoalView?> GetAsync(
        GoalId goalId,
        CancellationToken cancellationToken = default) =>
        goalId is null || string.IsNullOrWhiteSpace(goalId.Value)
            ? null
            : (await goalStore.GetAsync(goalId.Value, cancellationToken))?.ToView();

    public async ValueTask<IReadOnlyList<GoalView>> ListAsync(
        string workspaceId,
        CancellationToken cancellationToken = default) =>
        (await goalStore.ListAsync(workspaceId, cancellationToken))
        .Select(goal => goal.ToView())
        .ToArray();

    public async ValueTask<PlanView?> GetCurrentPlanAsync(
        GoalId goalId,
        CancellationToken cancellationToken = default) =>
        goalId is null || string.IsNullOrWhiteSpace(goalId.Value)
            ? null
            : (await goalStore.GetCurrentPlanAsync(goalId.Value, cancellationToken))?.ToView();

    public async ValueTask<PlanResult> ProposePlanAsync(
        PlanProposalRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.GoalId is null ||
            string.IsNullOrWhiteSpace(request.GoalId.Value) ||
            string.IsNullOrWhiteSpace(request.Content) ||
            request.Content.Length > MaximumPlanCharacters)
        {
            return PlanFailure(
                "invalid_plan",
                $"The plan must contain 1-{MaximumPlanCharacters} characters.");
        }

        StoredGoal? goal = await goalStore.GetAsync(request.GoalId.Value, cancellationToken);
        if (goal is null)
        {
            return PlanFailure("goal_missing", "The goal does not exist.");
        }

        if (goal.State is not "Draft" and not "NeedsPlanRevision")
        {
            return PlanFailure("invalid_transition", "The goal is not ready for a plan proposal.");
        }

        RegisteredWorkspace? workspace = await workspaceStore.GetActiveAsync(cancellationToken);
        if (workspace is null || !workspace.Id.Equals(goal.WorkspaceId, StringComparison.Ordinal))
        {
            return PlanFailure("workspace_not_active", "The goal workspace must be active.");
        }

        StoredPlan? current = await goalStore.GetCurrentPlanAsync(goal.Id, cancellationToken);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        StoredPlan plan = new(
            Guid.NewGuid().ToString("N"),
            goal.Id,
            (current?.Revision ?? 0) + 1,
            request.Content.Trim(),
            "Pending",
            now,
            now);
        try
        {
            return (await goalStore.SavePlanAsync(
                plan,
                goal.State,
                "AwaitingPlanApproval",
                cancellationToken)).ToResult();
        }
        catch (InvalidOperationException exception)
        {
            return PlanFailure("invalid_transition", exception.Message);
        }
    }

    public async ValueTask<PlanResult> DecidePlanAsync(
        PlanDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.GoalId is null ||
            string.IsNullOrWhiteSpace(request.GoalId.Value) ||
            request.PlanId is null ||
            string.IsNullOrWhiteSpace(request.PlanId.Value) ||
            !Enum.IsDefined(request.Decision))
        {
            return PlanFailure("invalid_decision", "The decision must be Approve or Deny.");
        }

        bool approve = request.Decision is PlanDecision.Approve;
        if (request.Decision is PlanDecision.Deny && string.IsNullOrWhiteSpace(request.Reason))
        {
            return PlanFailure("invalid_decision", "A denial reason is required.");
        }

        if (request.Reason?.Length > MaximumDecisionReasonCharacters)
        {
            return PlanFailure(
                "invalid_decision",
                $"The decision reason cannot exceed {MaximumDecisionReasonCharacters} characters.");
        }

        StoredGoal? goal = await goalStore.GetAsync(request.GoalId.Value, cancellationToken);
        StoredPlan? plan = await goalStore.GetCurrentPlanAsync(request.GoalId.Value, cancellationToken);
        if (goal is null || plan is null || !plan.Id.Equals(request.PlanId.Value, StringComparison.Ordinal))
        {
            return PlanFailure("plan_missing", "The current plan does not match the decision request.");
        }

        if (goal.State != "AwaitingPlanApproval" || plan.State != "Pending")
        {
            return PlanFailure("invalid_transition", "The plan is not awaiting a decision.");
        }

        RegisteredWorkspace? workspace = await workspaceStore.GetActiveAsync(cancellationToken);
        if (workspace is null || !workspace.Id.Equals(goal.WorkspaceId, StringComparison.Ordinal))
        {
            return PlanFailure("workspace_not_active", "The goal workspace must be active.");
        }

        if (approve && !workspace.IsTrusted)
        {
            return PlanFailure(
                "workspace_not_trusted",
                "Trust the workspace before approving mutation capabilities.");
        }

        string decision = approve ? "Approved" : "Denied";
        DateTimeOffset decidedAt = DateTimeOffset.UtcNow;
        StoredGoalWorktree? storedWorktree = null;
        if (approve)
        {
            GoalWorktreeResult worktree = await worktreeManager.CreateAsync(
                goal.Id,
                workspace.RootPath,
                cancellationToken);
            if (worktree.Error is not null)
            {
                return PlanFailure(
                    worktree.ErrorCode ?? "worktree_failed",
                    worktree.Error);
            }

            storedWorktree = new(
                goal.Id,
                workspace.Id,
                worktree.Branch,
                worktree.Path,
                worktree.BaseCommit,
                "Active",
                decidedAt);
        }

        StoredApproval approval = new(
            Guid.NewGuid().ToString("N"),
            goal.Id,
            plan.Id,
            "Plan",
            decision,
            string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
            decidedAt);
        try
        {
            return (await goalStore.DecidePlanAsync(
                approval,
                storedWorktree,
                "AwaitingPlanApproval",
                "Pending",
                approve ? "Approved" : "NeedsPlanRevision",
                decision,
                cancellationToken)).ToResult();
        }
        catch (InvalidOperationException exception)
        {
            return PlanFailure("invalid_transition", exception.Message);
        }
    }

    private static string? Validate(GoalCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.WorkspaceId))
        {
            return "A workspace is required.";
        }

        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > MaximumTitleCharacters)
        {
            return $"The title must contain 1-{MaximumTitleCharacters} characters.";
        }

        if (string.IsNullOrWhiteSpace(request.Objective) ||
            request.Objective.Length > MaximumObjectiveCharacters)
        {
            return $"The objective must contain 1-{MaximumObjectiveCharacters} characters.";
        }

        if (request.ReviewCycleLimit is null || request.ReviewCycleLimit.Value is < 1 or > 20)
        {
            return "The review-cycle limit must be between 1 and 20.";
        }

        return request.RemoteBudget?.Value is <= 0
            ? "The remote-model budget must be positive when provided."
            : null;
    }

    private static PlanResult PlanFailure(string code, string error) =>
        new(null, null, null, null, code, error);
}

internal static class StoredGoalMapping
{
    internal static GoalView ToView(this StoredGoal goal) => new(
        new(goal.Id),
        goal.WorkspaceId,
        goal.Title,
        goal.Objective,
        new(goal.ReviewCycleLimit),
        goal.RemoteBudgetMicrousd is null ? null : new(goal.RemoteBudgetMicrousd.Value),
        ParseEnum<GoalState>(goal.State, "goal state"),
        goal.CreatedAt,
        goal.UpdatedAt);

    internal static PlanView ToView(this StoredPlan plan) => new(
        new(plan.Id),
        new(plan.GoalId),
        new(plan.Revision),
        plan.Content,
        ParseEnum<PlanState>(plan.State, "plan state"),
        plan.CreatedAt,
        plan.UpdatedAt);

    internal static ApprovalView ToView(this StoredApproval approval) => new(
        new(approval.Id),
        new(approval.GoalId),
        new(approval.PlanId),
        ParseEnum<ApprovalKind>(approval.Kind, "approval kind"),
        ParseEnum<ApprovalDecision>(approval.Decision, "approval decision"),
        approval.Reason,
        approval.DecidedAt);

    internal static PlanResult ToResult(this StoredPlanSnapshot snapshot) => new(
        snapshot.Goal.ToView(),
        snapshot.Plan.ToView(),
        snapshot.Approval?.ToView(),
        snapshot.Worktree is null
            ? null
            : new(
                new(snapshot.Worktree.GoalId),
                snapshot.Worktree.WorkspaceId,
                snapshot.Worktree.Branch,
                snapshot.Worktree.Path,
                snapshot.Worktree.BaseCommit,
                ParseEnum<GoalWorktreeState>(snapshot.Worktree.State, "goal worktree state"),
                snapshot.Worktree.CreatedAt),
        ErrorCode: null,
        Error: null);

    private static TEnum ParseEnum<TEnum>(string value, string field)
        where TEnum : struct, Enum =>
        Enum.TryParse(value, ignoreCase: false, out TEnum parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new InvalidDataException($"Stored {field} '{value}' is invalid.");
}
