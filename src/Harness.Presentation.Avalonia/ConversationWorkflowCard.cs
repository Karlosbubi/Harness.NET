using Harness.BusinessLogic.Acceptance;
using Harness.BusinessLogic.Agents;
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
    Handoff,
    Status,
}

internal enum ConversationWorkflowCardState
{
    Loading,
    Unavailable,
    Stale,
    Pending,
    Active,
    Paused,
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
    RetryRun,
    AbortGoal,
    ExtendBudget,
    CancelRun,
    ReviewAcceptedChanges,
    ApproveRestore,
    DenyRestore,
    ReviewCommitPreview,
    ApproveCommit,
    DenyCommit,
    ResumeCommit,
    ReviewBranchHandoff,
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

        if (card.Kind is ConversationWorkflowCardKind.Goal)
        {
            bool canAbort = goals.Workflow?.State is not GoalWorkflowState.Completed and
                not GoalWorkflowState.Aborted;
            if (goal.State is GoalState.Draft)
            {
                return canAbort
                    ? [
                        new(ConversationWorkflowActionKind.ConfigureGoal, "Goal settings", false),
                        new(ConversationWorkflowActionKind.AbortGoal, "Abort & start new", false),
                    ]
                    : [new(ConversationWorkflowActionKind.ConfigureGoal, "Goal settings", false)];
            }

            return canAbort
                ? [new(ConversationWorkflowActionKind.AbortGoal, "Abort & start new", false)]
                : [];
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
                return goals.IsWorkflowRunning
                    ? [new(ConversationWorkflowActionKind.CancelRun, "Cancel run", false)]
                    : workflow.CanResume
                        ? [new(ConversationWorkflowActionKind.ContinueRun, "Continue run", true)]
                        : [];
            }

            if (workflow.State is GoalWorkflowState.AwaitingPlanApproval &&
                goal.State is GoalState.Approved)
            {
                return [new(ConversationWorkflowActionKind.ContinueRun, "Continue run", true)];
            }

            if (workflow.State is GoalWorkflowState.NeedsDirection &&
                workflow.RetryRole is { } retryRole)
            {
                ConversationWorkflowAction retry = new(
                    ConversationWorkflowActionKind.RetryRun,
                    $"Retry {retryRole} with changes",
                    true);
                AgentRole? agentRole = retryRole switch
                {
                    GoalWorkflowRetryRole.Lead => AgentRole.Lead,
                    GoalWorkflowRetryRole.Implementer => AgentRole.Implementer,
                    GoalWorkflowRetryRole.Reviewer => AgentRole.Reviewer,
                    _ => null,
                };
                bool cappedRemote = RemoteSpendPreference.FromGoalBudget(goal.RemoteBudget).Mode is
                    RemoteSpendMode.Capped && agentRole is not null && goals.ModelSelections.Any(selection =>
                    selection.Role == agentRole && selection.Access is ModelAccess.Remote);
                ConversationWorkflowAction abort = new(
                    ConversationWorkflowActionKind.AbortGoal,
                    "Abort & start new",
                    false);
                return cappedRemote
                    ? [retry, new(ConversationWorkflowActionKind.ExtendBudget,
                        "Increase remote cap", false), abort]
                    : [retry, abort];
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

        if (card.Kind is ConversationWorkflowCardKind.Handoff)
        {
            return [new(ConversationWorkflowActionKind.ReviewBranchHandoff,
                "Review branch in Git", true)];
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
            cards.AddRange(workflow.Activities.Select(activity => new ConversationWorkflowCard(
                $"run.{workflow.Id.Value}.activity.{activity.Sequence}",
                ConversationWorkflowCardKind.Run,
                ActivityState(activity.Kind),
                ActivityTitle(activity.Kind),
                ActivitySummary(workflow, activity),
                ActivityDetails(activity),
                300 + activity.Sequence)));
            cards.AddRange(workflow.Tasks.Select(task => new ConversationWorkflowCard(
                $"task.{task.Id.Value}",
                ConversationWorkflowCardKind.Task,
                TaskState(task.State),
                $"Task {task.Sequence.Value} · {task.Title.Value}",
                task.Objective.Value,
                task.Report?.Value,
                500 + task.Sequence.Value)));
            cards.AddRange(workflow.Evidence
                .Where(item => !DuplicatesDirectionNotice(workflow, item))
                .Select(item => new ConversationWorkflowCard(
                $"evidence.{workflow.Id.Value}.{item.Sequence}",
                ConversationWorkflowCardKind.Evidence,
                ConversationWorkflowCardState.Completed,
                item.Title.Value,
                FirstLine(item.Content.Value),
                item.Content.Value,
                700 + item.Sequence)));
            cards.Add(new(
                $"run.{workflow.Id.Value}",
                ConversationWorkflowCardKind.Run,
                WorkflowState(workflow.State),
                $"Current run · {CurrentPhase(workflow)}",
                RunSummary(workflow),
                RunDetails(workflow),
                Order: 790));
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

            if (commit.State is GoalCommitApprovalState.Committed &&
                commit.CommitSha is { } commitSha)
            {
                cards.Add(new(
                    $"handoff.{commit.Id.Value}",
                    ConversationWorkflowCardKind.Handoff,
                    ConversationWorkflowCardState.Completed,
                    "Goal branch ready",
                    $"{commit.Branch.Value} · commit {ShortSha(commitSha.Value)} · local only",
                    "The exact approved commit is complete in the isolated goal worktree. " +
                    "Next, deliberately push this branch and open a PR, or inspect and merge " +
                    "it with your normal Git workflow. Harness.NET will not push, open a PR, " +
                    "merge, rebase, or change the original branch automatically.",
                    Order: 950));
            }
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

    private static string ShortSha(string value) => value.Length <= 12 ? value : value[..12];

    private static string RunSummary(GoalWorkflowSnapshot workflow) => string.Join(
        '\n',
        $"Now: {CurrentWork(workflow)}",
        $"Result so far: {CurrentResult(workflow)}",
        $"Next: {NextStep(workflow)}");

    private static string RunDetails(GoalWorkflowSnapshot workflow)
    {
        string progress = $"Cycle {workflow.ReviewCycle.Value} · " +
            $"{workflow.Tasks.Count(task => task.State is GoalTaskState.Completed)}/" +
            $"{workflow.Tasks.Count} tasks completed · {workflow.Evidence.Count} evidence item(s).";
        if (!workflow.RequiresUserDirection)
        {
            return progress;
        }

        return workflow.RetryRole is { } retryRole
            ? $"{progress} The run is paused. Inspect the technical detail on the paused checkpoint before explicitly retrying {retryRole}."
            : $"{progress} The run is paused until you supply direction.";
    }

    private static GoalWorkflowCheckpointKind? LatestCheckpoint(GoalWorkflowSnapshot workflow) =>
        workflow.Activities.OrderBy(activity => activity.Sequence).LastOrDefault()?.Kind;

    private static string CurrentPhase(GoalWorkflowSnapshot workflow)
    {
        if (workflow.State is GoalWorkflowState.NeedsDirection)
        {
            return "Needs your direction";
        }

        return LatestCheckpoint(workflow) switch
        {
            GoalWorkflowCheckpointKind.LeadCallStarted => "Lead planning",
            GoalWorkflowCheckpointKind.PlanProposed => "Plan ready",
            GoalWorkflowCheckpointKind.PlanApproved => "Preparing implementation",
            GoalWorkflowCheckpointKind.ImplementerCallStarted => "Implementing",
            GoalWorkflowCheckpointKind.ImplementationProduced => "Implementation ready",
            GoalWorkflowCheckpointKind.ReviewerCallStarted => "Reviewing",
            GoalWorkflowCheckpointKind.ReviewCompleted => "Review complete",
            GoalWorkflowCheckpointKind.Accepted => "Accepted",
            _ => workflow.State switch
            {
                GoalWorkflowState.AwaitingPlanApproval => "Plan ready",
                GoalWorkflowState.AwaitingAcceptance => "Ready for acceptance",
                GoalWorkflowState.Completed => "Completed",
                GoalWorkflowState.Aborted => "Aborted",
                _ => "Starting",
            },
        };
    }

    private static string CurrentWork(GoalWorkflowSnapshot workflow) => workflow.State switch
    {
        GoalWorkflowState.NeedsDirection => "The agent run is paused and waiting for your decision.",
        GoalWorkflowState.AwaitingPlanApproval => "The proposed plan is waiting for your review.",
        GoalWorkflowState.AwaitingAcceptance => "The reviewed result is waiting for your acceptance.",
        GoalWorkflowState.Completed => "The agent run has completed.",
        GoalWorkflowState.Aborted => "The goal was aborted; no agent work is running.",
        _ => LatestCheckpoint(workflow) switch
        {
            GoalWorkflowCheckpointKind.LeadCallStarted => "The Lead is inspecting the workspace and generating a plan.",
            GoalWorkflowCheckpointKind.PlanApproved => "The approved plan is being prepared for implementation.",
            GoalWorkflowCheckpointKind.ImplementerCallStarted => "The Implementer is working on the current delegated task.",
            GoalWorkflowCheckpointKind.ImplementationProduced => "The implementation is being prepared for independent review.",
            GoalWorkflowCheckpointKind.ReviewerCallStarted => "The Reviewer is checking the implementation and evidence.",
            _ => "Harness is advancing the goal from its last durable checkpoint.",
        },
    };

    private static string CurrentResult(GoalWorkflowSnapshot workflow)
    {
        int completed = workflow.Tasks.Count(task => task.State is GoalTaskState.Completed);
        return LatestCheckpoint(workflow) switch
        {
            null or GoalWorkflowCheckpointKind.Started or GoalWorkflowCheckpointKind.LeadCallStarted =>
                "No durable plan has been produced yet.",
            GoalWorkflowCheckpointKind.PlanProposed =>
                $"A durable plan with {workflow.Tasks.Count} delegated task(s) is available.",
            GoalWorkflowCheckpointKind.PlanApproved =>
                $"The plan is approved; {completed}/{workflow.Tasks.Count} tasks are complete.",
            GoalWorkflowCheckpointKind.ImplementerCallStarted =>
                $"{completed}/{workflow.Tasks.Count} delegated tasks are complete.",
            GoalWorkflowCheckpointKind.ImplementationProduced or
                GoalWorkflowCheckpointKind.ReviewerCallStarted or
                GoalWorkflowCheckpointKind.ReviewCompleted =>
                $"{completed}/{workflow.Tasks.Count} tasks and {workflow.Evidence.Count} evidence item(s) are durable.",
            GoalWorkflowCheckpointKind.UserDirectionRequired =>
                workflow.RetryRole is GoalWorkflowRetryRole.Lead
                    ? "No valid plan was produced; the prompt and failure details were preserved."
                    : "No usable role result was produced; the last safe checkpoint and failure details were preserved.",
            GoalWorkflowCheckpointKind.Accepted =>
                $"The result was accepted with {workflow.Evidence.Count} evidence item(s).",
            _ => "The latest durable checkpoint is preserved.",
        };
    }

    private static string NextStep(GoalWorkflowSnapshot workflow) => workflow.State switch
    {
        GoalWorkflowState.NeedsDirection when workflow.RetryRole is { } role =>
            $"Retry {role} with a different model or guidance, or abort and start a new goal.",
        GoalWorkflowState.NeedsDirection => "Choose whether to retry or abort this goal.",
        GoalWorkflowState.AwaitingPlanApproval => "Approve the plan or request changes.",
        GoalWorkflowState.AwaitingAcceptance => "Review the accepted changes and continue the Git workflow.",
        GoalWorkflowState.Completed => "Review the result and continue with the exact-diff commit flow.",
        GoalWorkflowState.Aborted => "Start a new goal when you are ready.",
        _ => LatestCheckpoint(workflow) switch
        {
            GoalWorkflowCheckpointKind.LeadCallStarted => "When planning finishes, review the durable plan before any repository mutation.",
            GoalWorkflowCheckpointKind.ImplementerCallStarted => "The completed task will be recorded, then independent review begins.",
            GoalWorkflowCheckpointKind.ReviewerCallStarted => "The review will either accept the result or start a bounded correction cycle.",
            _ => "Harness will advance to the next durable workflow checkpoint.",
        },
    };

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
        GoalWorkflowState.NeedsDirection => ConversationWorkflowCardState.Paused,
        GoalWorkflowState.Completed => ConversationWorkflowCardState.Completed,
        GoalWorkflowState.Aborted => ConversationWorkflowCardState.Cancelled,
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
            ? ConversationWorkflowCardState.Paused
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

    private static string ActivitySummary(
        GoalWorkflowSnapshot workflow,
        GoalWorkflowActivityView activity)
    {
        if (activity.Kind is not GoalWorkflowCheckpointKind.UserDirectionRequired)
        {
            return activity.Summary.Value;
        }

        return workflow.RetryRole switch
        {
            GoalWorkflowRetryRole.Lead =>
                "The Lead response could not be converted into a valid plan. No repository changes were made.",
            GoalWorkflowRetryRole.Implementer =>
                "The Implementer did not produce a usable task result. Work is paused at the last safe checkpoint.",
            GoalWorkflowRetryRole.Reviewer =>
                "The Reviewer did not produce a usable decision. Work is paused at the last safe checkpoint.",
            _ => "The agent call did not produce a usable result. Work is paused at the last safe checkpoint.",
        };
    }

    private static string ActivityDetails(GoalWorkflowActivityView activity) =>
        activity.Kind is GoalWorkflowCheckpointKind.UserDirectionRequired
            ? $"Technical detail: {activity.Summary.Value}"
            : activity.Actor.ToString();

    private static bool DuplicatesDirectionNotice(
        GoalWorkflowSnapshot workflow,
        WorkflowEvidenceView evidence) =>
        evidence.Title.Value.Equals("Recovery notice", StringComparison.OrdinalIgnoreCase) &&
        workflow.Activities.Any(activity =>
            activity.Kind is GoalWorkflowCheckpointKind.UserDirectionRequired &&
            activity.Summary.Value.Equals(evidence.Content.Value, StringComparison.Ordinal));

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
