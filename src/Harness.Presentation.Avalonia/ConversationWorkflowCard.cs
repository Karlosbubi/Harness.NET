using Harness.BusinessLogic.Acceptance;
using Harness.BusinessLogic.Approvals;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Workflows;

namespace Harness.Presentation.Avalonia;

internal enum ConversationWorkflowCardKind
{
    Goal,
    Plan,
    Run,
    Task,
    Evidence,
    CapabilityApproval,
    CommitApproval,
    Status,
}

internal enum ConversationWorkflowCardState
{
    Loading,
    Unavailable,
    Stale,
    Pending,
    Active,
    Approved,
    Denied,
    Failed,
    Cancelled,
    Recovered,
    Completed,
}

internal sealed record ConversationWorkflowCard(
    string Id,
    ConversationWorkflowCardKind Kind,
    ConversationWorkflowCardState State,
    string Title,
    string Summary,
    string? Details,
    int Order);

internal enum ConversationWorkflowActionKind
{
    ConfigureGoal,
    StartPlanning,
    WritePlan,
    ApprovePlan,
    RequestPlanChanges,
    ContinueRun,
    CancelRun,
    ReviewAcceptedChanges,
    ApproveRestore,
    DenyRestore,
    ReviewCommitPreview,
    ApproveCommit,
    DenyCommit,
    ResumeCommit,
}

internal sealed record ConversationWorkflowAction(
    ConversationWorkflowActionKind Kind,
    string Label,
    bool IsPrimary);

internal static class ConversationWorkflowActionProjector
{
    internal static IReadOnlyList<ConversationWorkflowAction> Project(
        ConversationWorkflowCard card,
        GoalManagementState goals)
    {
        GoalView? goal = goals.SelectedGoal;
        if (goal is null)
        {
            return [];
        }

        if (card.Kind is ConversationWorkflowCardKind.Goal && goal.State is GoalState.Draft)
        {
            return [new(ConversationWorkflowActionKind.ConfigureGoal, "Goal settings", false)];
        }

        if (card.Kind is ConversationWorkflowCardKind.Plan)
        {
            if (card.State is ConversationWorkflowCardState.Unavailable &&
                goal.State is GoalState.Draft or GoalState.NeedsPlanRevision)
            {
                return
                [
                    new(ConversationWorkflowActionKind.StartPlanning, "Generate plan", true),
                    new(ConversationWorkflowActionKind.WritePlan, "Write plan manually", false),
                ];
            }

            if (goals.CurrentPlan?.State is PlanState.Pending)
            {
                return
                [
                    new(ConversationWorkflowActionKind.ApprovePlan, "Approve plan", true),
                    new(ConversationWorkflowActionKind.RequestPlanChanges, "Request changes", false),
                ];
            }
        }

        if (goals.Workflow is { } workflow && card.Id == $"run.{workflow.Id.Value}")
        {
            if (workflow.State is GoalWorkflowState.Running)
            {
                return [new(ConversationWorkflowActionKind.CancelRun, "Cancel run", false)];
            }

            if (workflow.State is GoalWorkflowState.AwaitingPlanApproval &&
                goal.State is GoalState.Approved)
            {
                return [new(ConversationWorkflowActionKind.ContinueRun, "Continue run", true)];
            }

            if (workflow.State is GoalWorkflowState.AwaitingAcceptance or GoalWorkflowState.Completed &&
                goals.CommitPreview is null && goals.CommitApproval is null)
            {
                return [new(ConversationWorkflowActionKind.ReviewAcceptedChanges,
                    "Review accepted changes", true)];
            }
        }

        if (card.Kind is ConversationWorkflowCardKind.CapabilityApproval &&
            card.State is ConversationWorkflowCardState.Pending)
        {
            return
            [
                new(ConversationWorkflowActionKind.ApproveRestore, "Approve once", true),
                new(ConversationWorkflowActionKind.DenyRestore, "Deny", false),
            ];
        }

        if (card.Kind is ConversationWorkflowCardKind.CommitApproval)
        {
            if (goals.CommitPreview is not null && goals.CommitApproval is null)
            {
                return [new(ConversationWorkflowActionKind.ReviewCommitPreview,
                    "Review exact diff", true)];
            }

            return goals.CommitApproval?.State switch
            {
                GoalCommitApprovalState.Pending =>
                [
                    new(ConversationWorkflowActionKind.ApproveCommit, "Approve exact diff", true),
                    new(ConversationWorkflowActionKind.DenyCommit, "Deny", false),
                ],
                GoalCommitApprovalState.Approved =>
                    [new(ConversationWorkflowActionKind.ResumeCommit, "Resume exact commit", true)],
                _ => [],
            };
        }

        return [];
    }
}

internal static class ConversationWorkflowProjector
{
    internal static IReadOnlyList<ConversationWorkflowCard> Project(
        GoalManagementState goals,
        string? shellError = null)
    {
        List<ConversationWorkflowCard> cards = [];
        if (goals.SelectedGoal is not { } goal)
        {
            if (goals.IsBusy)
            {
                cards.Add(Status("goal.loading", ConversationWorkflowCardState.Loading,
                    "Loading goal context", "Harness is resolving durable goal state."));
            }
            return cards;
        }

        cards.Add(new(
            $"goal.{goal.Id.Value}",
            ConversationWorkflowCardKind.Goal,
            MapGoalState(goal.State),
            goal.Title,
            goal.Objective,
            $"Goal · {goal.State} · review limit {goal.ReviewCycleLimit.Value}",
            Order: 0));

        if (goals.CurrentPlan is { } plan)
        {
            cards.Add(new(
                $"plan.{plan.Id.Value}.{plan.Revision.Value}",
                ConversationWorkflowCardKind.Plan,
                MapPlanState(plan.State),
                $"Plan revision {plan.Revision.Value}",
                FirstLine(plan.Content),
                plan.Content,
                Order: 100));
        }
        else
        {
            cards.Add(new(
                $"plan.unavailable.{goal.Id.Value}",
                ConversationWorkflowCardKind.Plan,
                ConversationWorkflowCardState.Unavailable,
                "No plan proposed",
                "Planning has not produced a durable proposal yet.",
                Details: null,
                Order: 100));
        }

        if (goals.Workflow is { } workflow)
        {
            cards.Add(new(
                $"run.{workflow.Id.Value}",
                ConversationWorkflowCardKind.Run,
                WorkflowState(workflow.State),
                $"Agent run · cycle {workflow.ReviewCycle.Value}",
                RunSummary(workflow),
                workflow.RequiresUserDirection
                    ? "The run is paused until the user supplies direction."
                    : null,
                Order: 200));
            cards.AddRange(workflow.Activities.Select(activity => new ConversationWorkflowCard(
                $"run.{workflow.Id.Value}.activity.{activity.Sequence}",
                ConversationWorkflowCardKind.Run,
                ActivityState(activity.Kind),
                ActivityTitle(activity.Kind),
                activity.Summary.Value,
                activity.Actor.ToString(),
                300 + activity.Sequence)));
            cards.AddRange(workflow.Tasks.Select(task => new ConversationWorkflowCard(
                $"task.{task.Id.Value}",
                ConversationWorkflowCardKind.Task,
                TaskState(task.State),
                $"Task {task.Sequence.Value} · {task.Title.Value}",
                task.Objective.Value,
                task.Report?.Value,
                500 + task.Sequence.Value)));
            cards.AddRange(workflow.Evidence.Select(item => new ConversationWorkflowCard(
                $"evidence.{workflow.Id.Value}.{item.Sequence}",
                ConversationWorkflowCardKind.Evidence,
                ConversationWorkflowCardState.Completed,
                item.Title.Value,
                FirstLine(item.Content.Value),
                item.Content.Value,
                700 + item.Sequence)));
        }

        cards.AddRange(goals.CapabilityApprovals.Select((approval, index) => new ConversationWorkflowCard(
            $"capability.{approval.Id.Value}",
            ConversationWorkflowCardKind.CapabilityApproval,
            ApprovalState(approval.State),
            $"{approval.Capability} approval",
            $"{approval.Target} · {approval.Rationale}",
            approval.DecisionReason,
            800 + index)));

        if (goals.CommitApproval is { } commit)
        {
            cards.Add(new(
                $"commit.{commit.Id.Value}",
                ConversationWorkflowCardKind.CommitApproval,
                CommitState(commit.State),
                "Exact commit approval",
                $"{commit.ChangedFileCount.Value} file(s) · {commit.Branch.Value}",
                $"Diff {commit.DiffHash.Value} · expected head {commit.ExpectedHead.Value}",
                Order: 900));
        }
        else if (goals.CommitPreview is { } preview)
        {
            cards.Add(new(
                $"commit.preview.{preview.DiffHash.Value}",
                ConversationWorkflowCardKind.CommitApproval,
                ConversationWorkflowCardState.Pending,
                "Commit preview ready",
                $"{preview.ChangedFileCount.Value} file(s) · {preview.Branch.Value}",
                $"Diff {preview.DiffHash.Value} · head {preview.Head.Value}",
                Order: 900));
        }

        if (shellError is { Length: > 0 })
        {
            cards.Add(Status("workflow.error", ConversationWorkflowCardState.Failed,
                "Workflow update failed", shellError) with { Order = 1000 });
        }
        return cards.OrderBy(card => card.Order).ToArray();
    }

    private static ConversationWorkflowCard Status(
        string id,
        ConversationWorkflowCardState state,
        string title,
        string summary) => new(
        id,
        ConversationWorkflowCardKind.Status,
        state,
        title,
        summary,
        Details: null,
        Order: 0);

    private static string FirstLine(string value) =>
        value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "No details recorded.";

    private static string RunSummary(GoalWorkflowSnapshot workflow) =>
        $"{workflow.State} · {workflow.Tasks.Count} task(s) · " +
        $"{workflow.Evidence.Count} evidence item(s)";

    private static ConversationWorkflowCardState MapGoalState(GoalState state) => state switch
    {
        GoalState.Draft => ConversationWorkflowCardState.Pending,
        GoalState.AwaitingPlanApproval => ConversationWorkflowCardState.Pending,
        GoalState.NeedsPlanRevision => ConversationWorkflowCardState.Denied,
        GoalState.Approved => ConversationWorkflowCardState.Approved,
        _ => ConversationWorkflowCardState.Stale,
    };

    private static ConversationWorkflowCardState MapPlanState(PlanState state) => state switch
    {
        PlanState.Pending => ConversationWorkflowCardState.Pending,
        PlanState.Approved => ConversationWorkflowCardState.Approved,
        PlanState.Denied => ConversationWorkflowCardState.Denied,
        _ => ConversationWorkflowCardState.Stale,
    };

    private static ConversationWorkflowCardState WorkflowState(GoalWorkflowState state) => state switch
    {
        GoalWorkflowState.Running => ConversationWorkflowCardState.Active,
        GoalWorkflowState.AwaitingPlanApproval => ConversationWorkflowCardState.Pending,
        GoalWorkflowState.AwaitingAcceptance => ConversationWorkflowCardState.Pending,
        GoalWorkflowState.NeedsDirection => ConversationWorkflowCardState.Denied,
        GoalWorkflowState.Completed => ConversationWorkflowCardState.Completed,
        _ => ConversationWorkflowCardState.Stale,
    };

    private static ConversationWorkflowCardState TaskState(GoalTaskState state) => state switch
    {
        GoalTaskState.Pending => ConversationWorkflowCardState.Pending,
        GoalTaskState.InProgress => ConversationWorkflowCardState.Active,
        GoalTaskState.Completed => ConversationWorkflowCardState.Completed,
        _ => ConversationWorkflowCardState.Stale,
    };

    private static ConversationWorkflowCardState ActivityState(GoalWorkflowCheckpointKind kind) =>
        kind is GoalWorkflowCheckpointKind.UserDirectionRequired
            ? ConversationWorkflowCardState.Denied
            : kind is GoalWorkflowCheckpointKind.Accepted
                ? ConversationWorkflowCardState.Completed
                : ConversationWorkflowCardState.Active;

    private static string ActivityTitle(GoalWorkflowCheckpointKind kind) => kind switch
    {
        GoalWorkflowCheckpointKind.Started => "Run started",
        GoalWorkflowCheckpointKind.LeadCallStarted => "Lead planning",
        GoalWorkflowCheckpointKind.PlanProposed => "Plan proposed",
        GoalWorkflowCheckpointKind.PlanApproved => "Plan approved",
        GoalWorkflowCheckpointKind.ImplementerCallStarted => "Implementation started",
        GoalWorkflowCheckpointKind.ImplementationProduced => "Implementation produced",
        GoalWorkflowCheckpointKind.ReviewerCallStarted => "Independent review started",
        GoalWorkflowCheckpointKind.ReviewCompleted => "Review completed",
        GoalWorkflowCheckpointKind.UserDirectionRequired => "User direction required",
        GoalWorkflowCheckpointKind.Accepted => "Run accepted",
        _ => kind.ToString(),
    };

    private static ConversationWorkflowCardState ApprovalState(CapabilityApprovalState state) =>
        state switch
        {
            CapabilityApprovalState.Pending => ConversationWorkflowCardState.Pending,
            CapabilityApprovalState.Approved => ConversationWorkflowCardState.Approved,
            CapabilityApprovalState.Denied => ConversationWorkflowCardState.Denied,
            _ => ConversationWorkflowCardState.Stale,
        };

    private static ConversationWorkflowCardState CommitState(GoalCommitApprovalState state) =>
        state switch
        {
            GoalCommitApprovalState.Pending => ConversationWorkflowCardState.Pending,
            GoalCommitApprovalState.Approved => ConversationWorkflowCardState.Approved,
            GoalCommitApprovalState.Denied => ConversationWorkflowCardState.Denied,
            GoalCommitApprovalState.Committed => ConversationWorkflowCardState.Completed,
            _ => ConversationWorkflowCardState.Stale,
        };
}
