using System.Runtime.CompilerServices;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Goals;
using Harness.DataAccess.Workflows;
using StoredActor = Harness.DataAccess.Workflows.WorkflowActor;
using StoredKind = Harness.DataAccess.Workflows.GoalWorkflowCheckpointKind;
using StoredRunId = Harness.DataAccess.Workflows.GoalWorkflowRunId;
using StoredState = Harness.DataAccess.Workflows.GoalWorkflowRunState;

namespace Harness.BusinessLogic.Workflows;

internal sealed class GoalWorkflowService(
    IGoalWorkflowStore store,
    IGoalWorkflowTaskStore taskStore,
    IGoalService goalService,
    IAgentRoleRunner agentRunner,
    TimeProvider timeProvider) : IGoalWorkflowService
{
    private const int MaximumOutputTokens = 8192;

    public async ValueTask<GoalWorkflowSnapshot?> GetLatestAsync(
        GoalId goalId,
        CancellationToken cancellationToken = default)
    {
        if (!ValidGoalId(goalId))
        {
            return null;
        }

        StoredGoalWorkflowSnapshot? snapshot = await store.GetLatestAsync(
            new(goalId.Value), cancellationToken);
        return snapshot is null ? null : await ToViewAsync(snapshot, cancellationToken);
    }

    public async IAsyncEnumerable<GoalWorkflowSnapshot> StartPlanningAsync(
        GoalWorkflowStartRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ValidateStart(request);
        GoalView goal = await RequireGoalAsync(request.GoalId, cancellationToken);
        if (goal.State is not GoalState.Draft and not GoalState.NeedsPlanRevision)
        {
            throw new InvalidOperationException("The goal is not ready for lead planning.");
        }

        StoredGoalWorkflowSnapshot? latest = await store.GetLatestAsync(
            new(request.GoalId.Value), cancellationToken);
        if (latest is not null && latest.Run.State is not StoredState.Completed)
        {
            throw new InvalidOperationException("Resume the active run for this goal.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        StoredRunId runId = new(Guid.NewGuid().ToString("N"));
        StoredGoalWorkflowSnapshot snapshot = await store.StartAsync(
            new(runId, new(goal.Id.Value), StoredState.Running, new(0), now, now),
            Checkpoint(runId, StoredKind.Started, StoredActor.System,
                "Goal workflow started.", null, null, now) with { Sequence = 1 },
            cancellationToken);
        yield return await ToViewAsync(snapshot, cancellationToken);

        string leadTask = LeadTask(goal);
        snapshot = await AppendAsync(snapshot, StoredKind.LeadCallStarted, StoredActor.Lead,
            "Lead model call started; interruption after this point requires reconciliation.",
            "Lead prompt", leadTask, StoredKind.Started, StoredState.Running, StoredState.Running,
            cancellationToken);
        yield return await ToViewAsync(snapshot, cancellationToken);

        AgentRunResult result;
        try
        {
            result = await agentRunner.RunAsync(new(
                goal.Id,
                AgentRole.Lead,
                new(leadTask),
                request.LeadMaximumOutputTokens), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await MarkUncertainAsync(snapshot,
                "Lead call was cancelled after it started; inspect provider and cost evidence before continuing.");
            throw;
        }

        if (result.Output is null)
        {
            snapshot = await MarkDirectionAsync(snapshot,
                $"Lead call did not produce a plan: {result.Error?.Value ?? "unknown failure"}",
                cancellationToken);
            yield return await ToViewAsync(snapshot, cancellationToken);
            yield break;
        }

        GoalDelegation delegation = GoalDelegationParser.Parse(result.Output.Value);
        if (delegation.Error is not null)
        {
            snapshot = await MarkDirectionAsync(snapshot,
                $"Lead call did not produce a bounded delegation: {delegation.Error}",
                cancellationToken);
            yield return await ToViewAsync(snapshot, cancellationToken);
            yield break;
        }

        DateTimeOffset delegatedAt = timeProvider.GetUtcNow();
        await taskStore.CreateAsync(
            snapshot.Run.Id,
            delegation.Tasks.Select((task, index) => new StoredGoalWorkflowTask(
                new(Guid.NewGuid().ToString("N")),
                snapshot.Run.Id,
                new(index + 1),
                new(task.Title.Value),
                new(task.Objective.Value),
                new(task.FileAreas.Value),
                new(task.AcceptanceCriteria.Value),
                GoalWorkflowTaskState.Pending,
                Report: null,
                delegatedAt,
                StartedAt: null,
                CompletedAt: null)).ToArray(),
            cancellationToken);
        PlanResult plan = await goalService.ProposePlanAsync(
            new(goal.Id, delegation.Plan!), cancellationToken);
        if (plan.Plan is null)
        {
            snapshot = await MarkDirectionAsync(snapshot,
                $"The lead output could not be persisted as a plan: {plan.Error}",
                cancellationToken);
            yield return await ToViewAsync(snapshot, cancellationToken);
            yield break;
        }

        snapshot = await AppendAsync(snapshot, StoredKind.PlanProposed, StoredActor.Lead,
            $"Lead proposed plan revision {plan.Plan.Revision.Value}.",
            "Proposed plan", plan.Plan.Content,
            StoredKind.LeadCallStarted, StoredState.Running,
            StoredState.AwaitingPlanApproval, cancellationToken);
        yield return await ToViewAsync(snapshot, cancellationToken);
    }

    public async IAsyncEnumerable<GoalWorkflowSnapshot> ResumeAsync(
        GoalWorkflowResumeRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ValidateResume(request);
        GoalView goal = await RequireGoalAsync(request.GoalId, cancellationToken);
        StoredGoalWorkflowSnapshot snapshot = await store.GetLatestAsync(
            new(request.GoalId.Value), cancellationToken) ??
            throw new InvalidOperationException("The goal has no production workflow run.");
        StoredGoalWorkflowCheckpoint latest = snapshot.Checkpoints[^1];

        if (latest.Kind is StoredKind.LeadCallStarted)
        {
            PlanView? reconciledPlan = await goalService.GetCurrentPlanAsync(
                goal.Id, cancellationToken);
            if (goal.State is GoalState.AwaitingPlanApproval &&
                reconciledPlan?.State is PlanState.Pending &&
                (await taskStore.ListAsync(snapshot.Run.Id, cancellationToken)).Count > 0)
            {
                snapshot = await AppendAsync(snapshot, StoredKind.PlanProposed, StoredActor.System,
                    "Recovered the plan that was durable before its workflow checkpoint.",
                    "Recovered proposed plan", reconciledPlan.Content,
                    StoredKind.LeadCallStarted, StoredState.Running,
                    StoredState.AwaitingPlanApproval, cancellationToken);
                yield return await ToViewAsync(snapshot, cancellationToken);
                yield break;
            }

            snapshot = await MarkDirectionAsync(snapshot,
                "The interrupted lead call has no durable plan result and was not replayed.",
                cancellationToken);
            yield return await ToViewAsync(snapshot, cancellationToken);
            yield break;
        }

        if (latest.Kind is StoredKind.ImplementerCallStarted)
        {
            IReadOnlyList<StoredGoalWorkflowTask> interruptedTasks =
                await taskStore.ListAsync(snapshot.Run.Id, cancellationToken);
            int durableReports = snapshot.Checkpoints
                .TakeWhile(checkpoint => checkpoint.Kind is not StoredKind.ReviewerCallStarted)
                .Count(checkpoint => checkpoint.Kind is StoredKind.ImplementationProduced);
            StoredGoalWorkflowTask[] completedTasks = interruptedTasks
                .Where(task => task.State is GoalWorkflowTaskState.Completed)
                .OrderBy(task => task.Sequence.Value)
                .ToArray();
            if (interruptedTasks.All(task => task.State is not GoalWorkflowTaskState.InProgress) &&
                completedTasks.Length == durableReports + 1)
            {
                StoredGoalWorkflowTask recovered = completedTasks[^1];
                snapshot = await AppendAsync(snapshot, StoredKind.ImplementationProduced,
                    StoredActor.System,
                    $"Recovered durable delegated task {recovered.Sequence.Value}: " +
                    recovered.Title.Value,
                    "Recovered implementation report", recovered.Report!.Value,
                    StoredKind.ImplementerCallStarted, StoredState.Running,
                    StoredState.Running, cancellationToken);
                yield return await ToViewAsync(snapshot, cancellationToken);
                latest = snapshot.Checkpoints[^1];
            }
            else
            {
                snapshot = await MarkDirectionAsync(snapshot,
                    "The interrupted implementer call is uncertain and was not replayed.",
                    cancellationToken);
                yield return await ToViewAsync(snapshot, cancellationToken);
                yield break;
            }
        }

        if (latest.Kind is StoredKind.ReviewerCallStarted)
        {
            snapshot = await MarkDirectionAsync(snapshot,
                "The interrupted reviewer call is uncertain and was not replayed.",
                cancellationToken);
            yield return await ToViewAsync(snapshot, cancellationToken);
            yield break;
        }

        PlanView? plan = await goalService.GetCurrentPlanAsync(goal.Id, cancellationToken);
        if (latest.Kind is StoredKind.PlanProposed &&
            (goal.State is not GoalState.Approved || plan?.State is not PlanState.Approved))
        {
            yield return await ToViewAsync(snapshot, cancellationToken);
            yield break;
        }

        if (goal.State is not GoalState.Approved || plan?.State is not PlanState.Approved)
        {
            yield return await ToViewAsync(snapshot, cancellationToken);
            yield break;
        }

        if (latest.Kind is StoredKind.PlanProposed)
        {
            snapshot = await AppendAsync(snapshot, StoredKind.PlanApproved, StoredActor.System,
                $"User approved plan revision {plan.Revision.Value} and its isolated worktree.",
                "Approved plan", plan.Content,
                StoredKind.PlanProposed, StoredState.AwaitingPlanApproval,
                StoredState.Running, cancellationToken);
            yield return await ToViewAsync(snapshot, cancellationToken);
            latest = snapshot.Checkpoints[^1];
        }

        if (latest.Kind is not (StoredKind.PlanApproved or StoredKind.ImplementationProduced))
        {
            yield return await ToViewAsync(snapshot, cancellationToken);
            yield break;
        }

        IReadOnlyList<StoredGoalWorkflowTask> delegatedTasks = await taskStore.ListAsync(
            snapshot.Run.Id, cancellationToken);
        if (delegatedTasks.Count is 0 ||
            delegatedTasks.Any(task => task.State is GoalWorkflowTaskState.InProgress))
        {
            snapshot = await MarkDirectionAsync(snapshot,
                "The approved workflow has no consistent durable task delegation.",
                cancellationToken);
            yield return await ToViewAsync(snapshot, cancellationToken);
            yield break;
        }

        while (delegatedTasks.FirstOrDefault(task =>
                   task.State is GoalWorkflowTaskState.Pending) is { } delegatedTask)
        {
            string implementerTask = ImplementerTask(
                goal, plan, delegatedTask, delegatedTasks.Count);
            snapshot = await AppendAsync(snapshot, StoredKind.ImplementerCallStarted,
                StoredActor.Implementer,
                $"Implementer started delegated task {delegatedTask.Sequence.Value}/" +
                $"{delegatedTasks.Count}: {delegatedTask.Title.Value}",
                $"Delegated task {delegatedTask.Sequence.Value} prompt", implementerTask,
                latest.Kind, StoredState.Running,
                StoredState.Running, cancellationToken);
            yield return await ToViewAsync(snapshot, cancellationToken);
            await taskStore.StartAsync(
                delegatedTask.Id, timeProvider.GetUtcNow(), cancellationToken);

            AgentRunResult implementation;
            try
            {
                implementation = await agentRunner.RunAsync(new(
                    goal.Id,
                    AgentRole.Implementer,
                    new(implementerTask),
                    request.ImplementerMaximumOutputTokens,
                    FileAreas(delegatedTask)), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await MarkUncertainAsync(snapshot,
                    "Implementer call was cancelled after it started; completed tool evidence must be inspected.");
                throw;
            }

            if (implementation.Output is null)
            {
                snapshot = await MarkDirectionAsync(snapshot,
                    $"Delegated task {delegatedTask.Sequence.Value} failed and was not replayed: " +
                    implementation.Error?.Value,
                    cancellationToken);
                yield return await ToViewAsync(snapshot, cancellationToken);
                yield break;
            }

            StoredGoalWorkflowTask completedTask = await taskStore.CompleteAsync(
                delegatedTask.Id,
                new(implementation.Output.Value),
                timeProvider.GetUtcNow(),
                CancellationToken.None);
            snapshot = await AppendAsync(snapshot, StoredKind.ImplementationProduced,
                StoredActor.Implementer,
                $"Implementer completed delegated task {completedTask.Sequence.Value}/" +
                $"{delegatedTasks.Count}: {completedTask.Title.Value}",
                $"Delegated task {completedTask.Sequence.Value} report",
                completedTask.Report!.Value,
                StoredKind.ImplementerCallStarted, StoredState.Running,
                StoredState.Running, CancellationToken.None);
            yield return await ToViewAsync(snapshot, cancellationToken);
            latest = snapshot.Checkpoints[^1];
            delegatedTasks = await taskStore.ListAsync(snapshot.Run.Id, cancellationToken);
        }

        AgentOutput implementationOutput = new(ImplementationSummary(delegatedTasks));

        while (true)
        {
            string reviewerTask = ReviewerTask(goal, plan, implementationOutput);
            snapshot = await AppendAsync(snapshot, StoredKind.ReviewerCallStarted,
                StoredActor.Reviewer,
                "Independent reviewer model call started against diff and durable evidence.",
                "Reviewer prompt", reviewerTask,
                StoredKind.ImplementationProduced, StoredState.Running,
                StoredState.Running, cancellationToken);
            yield return await ToViewAsync(snapshot, cancellationToken);

            AgentRunResult review;
            try
            {
                review = await agentRunner.RunAsync(new(
                    goal.Id,
                    AgentRole.Reviewer,
                    new(reviewerTask),
                    request.ReviewerMaximumOutputTokens), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await MarkUncertainAsync(snapshot,
                    "Reviewer call was cancelled after it started and was not replayed.");
                throw;
            }

            if (review.Output is null)
            {
                snapshot = await MarkDirectionAsync(snapshot,
                    $"Reviewer call failed and was not replayed: {review.Error?.Value}",
                    cancellationToken);
                yield return await ToViewAsync(snapshot, cancellationToken);
                yield break;
            }

            GoalReviewResult decision = GoalReviewParser.Parse(review.Output.Value);
            int completedCycle = snapshot.Run.ReviewCycle.Value + 1;
            bool revisionAllowed = decision.Decision is GoalReviewDecision.Revise &&
                completedCycle < goal.ReviewCycleLimit.Value;
            StoredState nextState = decision.Decision switch
            {
                GoalReviewDecision.Accept => StoredState.AwaitingAcceptance,
                GoalReviewDecision.Revise when revisionAllowed => StoredState.Running,
                _ => StoredState.NeedsDirection,
            };
            string summary = decision.Decision switch
            {
                GoalReviewDecision.Accept =>
                    $"Independent reviewer accepted review cycle {completedCycle}.",
                GoalReviewDecision.Revise when revisionAllowed =>
                    $"Independent reviewer requested revision after cycle {completedCycle}; " +
                    "a bounded correction pass will follow.",
                GoalReviewDecision.Revise =>
                    $"Independent reviewer requested revision at the configured " +
                    $"{goal.ReviewCycleLimit.Value}-cycle limit; user direction is required.",
                _ => $"Reviewer returned an invalid structured decision: {decision.Error}",
            };
            snapshot = await AppendAsync(snapshot, StoredKind.ReviewCompleted,
                StoredActor.Reviewer, summary, "Independent review", review.Output.Value,
                StoredKind.ReviewerCallStarted, StoredState.Running, nextState,
                cancellationToken, new(completedCycle));
            yield return await ToViewAsync(snapshot, cancellationToken);

            if (!revisionAllowed)
            {
                yield break;
            }

            string revisionTask = RevisionTask(goal, plan, review.Output);
            snapshot = await AppendAsync(snapshot, StoredKind.ImplementerCallStarted,
                StoredActor.Implementer,
                $"Implementer correction pass started for review cycle {completedCycle}.",
                "Implementer revision prompt", revisionTask,
                StoredKind.ReviewCompleted, StoredState.Running,
                StoredState.Running, cancellationToken);
            yield return await ToViewAsync(snapshot, cancellationToken);

            AgentRunResult revision;
            try
            {
                revision = await agentRunner.RunAsync(new(
                    goal.Id,
                    AgentRole.Implementer,
                    new(revisionTask),
                    request.ImplementerMaximumOutputTokens,
                    FileAreas(delegatedTasks)), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await MarkUncertainAsync(snapshot,
                    "Implementer correction call was cancelled after it started; " +
                    "completed tool evidence must be inspected.");
                throw;
            }

            if (revision.Output is null)
            {
                snapshot = await MarkDirectionAsync(snapshot,
                    $"Implementer correction failed and was not replayed: {revision.Error?.Value}",
                    cancellationToken);
                yield return await ToViewAsync(snapshot, cancellationToken);
                yield break;
            }

            implementationOutput = revision.Output;
            snapshot = await AppendAsync(snapshot, StoredKind.ImplementationProduced,
                StoredActor.Implementer,
                $"Implementer completed the correction requested after review cycle {completedCycle}.",
                "Implementation correction report", implementationOutput.Value,
                StoredKind.ImplementerCallStarted, StoredState.Running,
                StoredState.Running, cancellationToken);
            yield return await ToViewAsync(snapshot, cancellationToken);
        }
    }

    private async ValueTask<GoalView> RequireGoalAsync(
        GoalId goalId,
        CancellationToken cancellationToken) =>
        await goalService.GetAsync(goalId, cancellationToken) ??
        throw new InvalidOperationException("The goal does not exist.");

    private async ValueTask<StoredGoalWorkflowSnapshot> MarkDirectionAsync(
        StoredGoalWorkflowSnapshot snapshot,
        string reason,
        CancellationToken cancellationToken)
    {
        StoredGoalWorkflowCheckpoint latest = snapshot.Checkpoints[^1];
        return await AppendAsync(snapshot, StoredKind.UserDirectionRequired,
            StoredActor.System, reason, "Recovery notice", reason,
            latest.Kind, snapshot.Run.State, StoredState.NeedsDirection,
            cancellationToken);
    }

    private async ValueTask MarkUncertainAsync(
        StoredGoalWorkflowSnapshot snapshot,
        string reason) =>
        await MarkDirectionAsync(snapshot, reason, CancellationToken.None);

    private async ValueTask<StoredGoalWorkflowSnapshot> AppendAsync(
        StoredGoalWorkflowSnapshot snapshot,
        StoredKind kind,
        StoredActor actor,
        string summary,
        string? evidenceTitle,
        string? evidenceContent,
        StoredKind expectedKind,
        StoredState expectedState,
        StoredState nextState,
        CancellationToken cancellationToken,
        GoalWorkflowReviewCycle? nextReviewCycle = null) =>
        await store.AppendAsync(
            Checkpoint(snapshot.Run.Id, kind, actor, summary, evidenceTitle,
                evidenceContent, timeProvider.GetUtcNow()),
            expectedKind, expectedState, nextState, cancellationToken, nextReviewCycle);

    private static StoredGoalWorkflowCheckpoint Checkpoint(
        StoredRunId runId,
        StoredKind kind,
        StoredActor actor,
        string summary,
        string? evidenceTitle,
        string? evidenceContent,
        DateTimeOffset createdAt) => new(
        Guid.NewGuid().ToString("N"), runId, Sequence: 0, kind, actor, new(summary),
        evidenceTitle is null ? null : new(evidenceTitle),
        evidenceContent is null ? null : new(evidenceContent), createdAt);

    private static string LeadTask(GoalView goal) => $$"""
        Inspect the trusted workspace with your read-only typed tools and propose a bounded,
        verifiable implementation plan for this goal.

        Goal: {{goal.Title}}
        Objective: {{goal.Objective}}

        Return JSON only with exactly this shape. Supply 1-12 ordered tasks; each task must be
        independently bounded, name concrete repository-relative file areas, and define
        objective acceptance criteria. Do not implement or claim that work is complete.

        {
          "plan": "reviewable plan including verification, risks, and non-goals",
          "tasks": [
            {
              "title": "bounded task title",
              "objective": "one independently implementable outcome",
              "fileAreas": ["relative/path/or/component"],
              "acceptanceCriteria": ["specific verifiable criterion"]
            }
          ]
        }
        """;

    private static string ImplementerTask(
        GoalView goal,
        PlanView plan,
        StoredGoalWorkflowTask task,
        int taskCount) => $$"""
        Implement only delegated task {{task.Sequence.Value}}/{{taskCount}} for goal
        '{{goal.Title}}' using typed goal-worktree tools. Respect its file-area boundary;
        inspect before editing, use atomic edits with correlation identifiers, then run the
        narrowest relevant build and tests without restore. Do not broaden scope or claim
        success without durable tool evidence.

        APPROVED PLAN
        {{plan.Content}}

        DELEGATED TASK
        Title: {{task.Title.Value}}
        Objective: {{task.Objective.Value}}
        File areas:
        {{task.FileAreas.Value}}
        Acceptance criteria:
        {{task.AcceptanceCriteria.Value}}
        """;

    private static string ImplementationSummary(
        IReadOnlyList<StoredGoalWorkflowTask> tasks) => string.Join(
        "\n\n",
        tasks.OrderBy(task => task.Sequence.Value).Select(task =>
        {
            const int maximumReportCharacters = 4_000;
            string report = task.Report?.Value ?? "No durable report.";
            if (report.Length > maximumReportCharacters)
            {
                report = report[..maximumReportCharacters] +
                    "\n[report abbreviated; inspect durable task and tool evidence]";
            }

            return $"Task {task.Sequence.Value}: {task.Title.Value}\n{report}";
        }));

    private static IReadOnlyList<AgentFileArea> FileAreas(
        StoredGoalWorkflowTask task) => task.FileAreas.Value
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(value => new AgentFileArea(value.StartsWith("- ", StringComparison.Ordinal)
            ? value[2..].Trim()
            : value))
        .ToArray();

    private static IReadOnlyList<AgentFileArea> FileAreas(
        IReadOnlyList<StoredGoalWorkflowTask> tasks) => tasks
        .SelectMany(FileAreas)
        .Distinct()
        .ToArray();

    private static string ReviewerTask(
        GoalView goal,
        PlanView plan,
        AgentOutput implementation) => $$"""
        Independently review the approved goal worktree. Use inspect_git and list_tool_evidence;
        inspect relevant files as needed. Check correctness, regressions, architecture, tests,
        and unsupported completion claims against the approved plan.

        GOAL: {{goal.Title}}
        APPROVED PLAN:
        {{plan.Content}}

        IMPLEMENTER REPORT:
        {{implementation.Value}}

        Return JSON only in one of these exact shapes:
        {"decision":"accept","summary":"specific evidence-based rationale"}
        {"decision":"revise","summary":"specific evidence-based rationale"}
        """;

    private static string RevisionTask(
        GoalView goal,
        PlanView plan,
        AgentOutput review) => $$"""
        Correct only the concrete findings from the independent review for goal
        '{{goal.Title}}'. Use only typed goal-worktree tools, inspect before editing, preserve
        the approved plan's scope, and build and test without restore. Do not claim success
        without durable tool evidence.

        APPROVED PLAN
        {{plan.Content}}

        REVIEW FINDINGS
        {{review.Value}}
        """;

    private static void ValidateStart(GoalWorkflowStartRequest request)
    {
        if (request is null || !ValidGoalId(request.GoalId) ||
            !ValidMaximum(request.LeadMaximumOutputTokens))
        {
            throw new ArgumentException(
                $"A valid goal and lead output maximum of 1-{MaximumOutputTokens} tokens are required.");
        }
    }

    private static void ValidateResume(GoalWorkflowResumeRequest request)
    {
        if (request is null || !ValidGoalId(request.GoalId) ||
            !ValidMaximum(request.ImplementerMaximumOutputTokens) ||
            !ValidMaximum(request.ReviewerMaximumOutputTokens))
        {
            throw new ArgumentException(
                $"A valid goal and role output maxima of 1-{MaximumOutputTokens} tokens are required.");
        }
    }

    private static bool ValidGoalId(GoalId? goalId) =>
        goalId is not null && Guid.TryParseExact(goalId.Value, "N", out _);

    private static bool ValidMaximum(MaximumAgentOutputTokens? maximum) =>
        maximum is not null && maximum.Value is > 0 and <= MaximumOutputTokens;

    private async ValueTask<GoalWorkflowSnapshot> ToViewAsync(
        StoredGoalWorkflowSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        StoredGoalWorkflowCheckpoint latest = snapshot.Checkpoints[^1];
        IReadOnlyList<StoredGoalWorkflowTask> tasks = await taskStore.ListAsync(
            snapshot.Run.Id, cancellationToken);
        return new(
            new(snapshot.Run.Id.Value),
            new(snapshot.Run.GoalId.Value),
            snapshot.Run.State switch
            {
                StoredState.Running => GoalWorkflowState.Running,
                StoredState.AwaitingPlanApproval => GoalWorkflowState.AwaitingPlanApproval,
                StoredState.AwaitingAcceptance => GoalWorkflowState.AwaitingAcceptance,
                StoredState.NeedsDirection => GoalWorkflowState.NeedsDirection,
                StoredState.Completed => GoalWorkflowState.Completed,
                _ => throw new ArgumentOutOfRangeException(nameof(snapshot)),
            },
            new(snapshot.Run.ReviewCycle.Value),
            tasks.Select(task => new GoalTaskView(
                new(task.Id.Value),
                new(task.Sequence.Value),
                new(task.Title.Value),
                new(task.Objective.Value),
                new(task.FileAreas.Value),
                new(task.AcceptanceCriteria.Value),
                task.State switch
                {
                    GoalWorkflowTaskState.Pending => GoalTaskState.Pending,
                    GoalWorkflowTaskState.InProgress => GoalTaskState.InProgress,
                    GoalWorkflowTaskState.Completed => GoalTaskState.Completed,
                    _ => throw new ArgumentOutOfRangeException(nameof(snapshot)),
                },
                task.Report is null ? null : new(task.Report.Value))).ToArray(),
            snapshot.Checkpoints.Select(checkpoint => new GoalWorkflowActivityView(
                checkpoint.Sequence,
                checkpoint.Kind switch
                {
                    StoredKind.Started => GoalWorkflowCheckpointKind.Started,
                    StoredKind.LeadCallStarted => GoalWorkflowCheckpointKind.LeadCallStarted,
                    StoredKind.PlanProposed => GoalWorkflowCheckpointKind.PlanProposed,
                    StoredKind.PlanApproved => GoalWorkflowCheckpointKind.PlanApproved,
                    StoredKind.ImplementerCallStarted =>
                        GoalWorkflowCheckpointKind.ImplementerCallStarted,
                    StoredKind.ImplementationProduced =>
                        GoalWorkflowCheckpointKind.ImplementationProduced,
                    StoredKind.ReviewerCallStarted =>
                        GoalWorkflowCheckpointKind.ReviewerCallStarted,
                    StoredKind.ReviewCompleted => GoalWorkflowCheckpointKind.ReviewCompleted,
                    StoredKind.UserDirectionRequired =>
                        GoalWorkflowCheckpointKind.UserDirectionRequired,
                    StoredKind.Accepted => GoalWorkflowCheckpointKind.Accepted,
                    _ => throw new ArgumentOutOfRangeException(nameof(snapshot)),
                },
                checkpoint.Actor switch
                {
                    StoredActor.System => WorkflowActor.System,
                    StoredActor.Lead => WorkflowActor.Lead,
                    StoredActor.Implementer => WorkflowActor.Implementer,
                    StoredActor.Reviewer => WorkflowActor.Reviewer,
                    _ => throw new ArgumentOutOfRangeException(nameof(snapshot)),
                },
                new(checkpoint.Summary.Value))).ToArray(),
            snapshot.Checkpoints.Where(checkpoint => checkpoint.EvidenceTitle is not null)
                .Select(checkpoint => new WorkflowEvidenceView(
                    checkpoint.Sequence,
                    new(checkpoint.EvidenceTitle!.Value),
                    new(checkpoint.EvidenceContent!.Value))).ToArray(),
            CanResume: latest.Kind is StoredKind.PlanProposed or
                StoredKind.PlanApproved or StoredKind.ImplementationProduced or
                StoredKind.LeadCallStarted or StoredKind.ImplementerCallStarted or
                StoredKind.ReviewerCallStarted,
            RequiresUserDirection: snapshot.Run.State is StoredState.NeedsDirection);
    }
}
