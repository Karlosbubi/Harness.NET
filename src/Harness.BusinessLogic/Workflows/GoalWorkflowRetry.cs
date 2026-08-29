using System.Runtime.CompilerServices;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Mutations;
using Harness.DataAccess.Workflows;
using StoredActor = Harness.DataAccess.Workflows.WorkflowActor;
using StoredKind = Harness.DataAccess.Workflows.GoalWorkflowCheckpointKind;
using StoredRunId = Harness.DataAccess.Workflows.GoalWorkflowRunId;
using StoredState = Harness.DataAccess.Workflows.GoalWorkflowRunState;

namespace Harness.BusinessLogic.Workflows;

internal sealed partial class GoalWorkflowService
{
    private async IAsyncEnumerable<GoalWorkflowSnapshot> RetryLeadAsync(
        GoalView goal,
        StoredGoalWorkflowSnapshot snapshot,
        GoalRetryGuidance? guidance,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (goal.State is not GoalState.Draft and not GoalState.NeedsPlanRevision)
        {
            snapshot = await MarkDirectionAsync(snapshot,
                "The goal changed before the failed Lead call could be retried.",
                cancellationToken);
            yield return await ToViewAsync(snapshot, cancellationToken);
            yield break;
        }

        AgentRunResult result;
        try
        {
            result = await agentRunner.RunAsync(new(
                goal.Id,
                AgentRole.Lead,
                new(WithRetryGuidance(LeadTask(goal), guidance))), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await MarkUncertainAsync(snapshot,
                "Retried Lead call was cancelled after it started; inspect provider and cost evidence before continuing.");
            throw;
        }

        if (result.Output is null)
        {
            snapshot = await MarkAgentFailureAsync(snapshot, result,
                $"Retried Lead call did not produce a plan: {result.Error?.Value ?? "unknown failure"}",
                cancellationToken);
            yield return await ToViewAsync(snapshot, cancellationToken);
            yield break;
        }

        GoalDelegation delegation = GoalDelegationParser.Parse(result.Output.Value);
        if (delegation.Error is not null)
        {
            snapshot = await MarkRejectedLeadOutputAsync(
                snapshot,
                "Retried Lead call did not produce a bounded delegation",
                result.Output.Value,
                delegation.Error,
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
                $"The retried Lead output could not be persisted as a plan: {plan.Error}",
                cancellationToken);
            yield return await ToViewAsync(snapshot, cancellationToken);
            yield break;
        }

        snapshot = await AppendAsync(snapshot, StoredKind.PlanProposed, StoredActor.Lead,
            $"Retried Lead call proposed plan revision {plan.Plan.Revision.Value}.",
            "Proposed plan", plan.Plan.Content,
            StoredKind.LeadCallStarted, StoredState.Running,
            StoredState.AwaitingPlanApproval, cancellationToken);
        yield return await ToViewAsync(snapshot, cancellationToken);
    }

    private async IAsyncEnumerable<GoalWorkflowSnapshot> RunReviewCyclesAsync(
        GoalView goal,
        PlanView plan,
        IReadOnlyList<StoredGoalWorkflowTask> delegatedTasks,
        StoredGoalWorkflowSnapshot snapshot,
        bool reviewerCallAlreadyStarted,
        bool stopAfterReview,
        AgentOutput? initialImplementationOutput,
        GoalRetryGuidance? retryGuidance,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        AgentOutput implementationOutput = initialImplementationOutput ??
            new(ImplementationSummary(delegatedTasks));

        while (true)
        {
            string reviewerTask = ReviewerTask(goal, plan, implementationOutput);
            if (retryGuidance is not null)
            {
                reviewerTask = WithRetryGuidance(reviewerTask, retryGuidance);
                retryGuidance = null;
            }
            if (!reviewerCallAlreadyStarted)
            {
                snapshot = await AppendAsync(snapshot, StoredKind.ReviewerCallStarted,
                    StoredActor.Reviewer,
                    "Independent reviewer model call started against diff and durable evidence.",
                    "Reviewer prompt", reviewerTask,
                    StoredKind.ImplementationProduced, StoredState.Running,
                    StoredState.Running, cancellationToken);
                yield return await ToViewAsync(snapshot, cancellationToken);
            }

            reviewerCallAlreadyStarted = false;

            AgentRunResult review;
            try
            {
                review = await agentRunner.RunAsync(new(
                    goal.Id,
                    AgentRole.Reviewer,
                    new(reviewerTask)), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await MarkUncertainAsync(snapshot,
                    "Reviewer call was cancelled after it started and was not replayed.");
                throw;
            }

            if (review.Output is null)
            {
                snapshot = await MarkAgentFailureAsync(snapshot, review,
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

            if (stopAfterReview)
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
            HashSet<string> evidenceBefore = await EvidenceIdsAsync(
                goal.Id, cancellationToken);
            try
            {
                revision = await agentRunner.RunAsync(new(
                    goal.Id,
                    AgentRole.Implementer,
                    new(revisionTask),
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
                snapshot = await MarkAgentFailureAsync(snapshot, revision,
                    $"Implementer correction failed and was not replayed: {revision.Error?.Value}",
                    cancellationToken);
                yield return await ToViewAsync(snapshot, cancellationToken);
                yield break;
            }

            if (!await HasNewDurableEvidenceAsync(
                    goal.Id, evidenceBefore, includeVerification: true, CancellationToken.None))
            {
                snapshot = await MarkDirectionAsync(snapshot,
                    "The Implementer returned a review-correction report without successful " +
                    "new mutation or verification evidence. The correction remains in progress.",
                    CancellationToken.None);
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

            GoalFinalValidationOutcome validation = await ValidateAndRepairFinalAsync(
                goal, plan, delegatedTasks, snapshot, cancellationToken);
            foreach (StoredGoalWorkflowSnapshot validationSnapshot in validation.Snapshots)
            {
                yield return await ToViewAsync(validationSnapshot, cancellationToken);
            }
            if (validation.ShouldStop)
            {
                yield break;
            }

            snapshot = validation.Snapshot;
            implementationOutput = validation.ImplementationOutput ?? implementationOutput;
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

    private async ValueTask<StoredGoalWorkflowSnapshot> MarkRejectedLeadOutputAsync(
        StoredGoalWorkflowSnapshot snapshot,
        string summary,
        string output,
        string error,
        CancellationToken cancellationToken)
    {
        string bounded = BoundText(output, MaximumRejectedLeadOutputCharacters);
        string reason = $"{summary}: {BoundText(error, MaximumRejectedLeadErrorCharacters)}";
        string evidence = $$"""
            {{reason}}

            REJECTED LEAD OUTPUT
            {{bounded}}
            """;
        StoredGoalWorkflowCheckpoint latest = snapshot.Checkpoints[^1];
        return await AppendAsync(snapshot, StoredKind.UserDirectionRequired,
            StoredActor.System, reason, "Rejected Lead output", evidence,
            latest.Kind, snapshot.Run.State, StoredState.NeedsDirection,
            cancellationToken);
    }

    private static string BoundText(string value, int maximumCharacters) =>
        value.Length <= maximumCharacters
            ? value
            : value[..maximumCharacters] + "\n[truncated]";

    private async ValueTask<StoredGoalWorkflowSnapshot> MarkAgentFailureAsync(
        StoredGoalWorkflowSnapshot snapshot,
        AgentRunResult result,
        string reason,
        CancellationToken cancellationToken)
    {
        if (result.ErrorCode?.Value is not "remote_cost_cap_exceeded")
        {
            return await MarkDirectionAsync(snapshot, reason, cancellationToken);
        }

        StoredGoalWorkflowCheckpoint latest = snapshot.Checkpoints[^1];
        const string partial =
            "The configured monetary cost limit was reached before the goal could finish. " +
            "Every completed task and durable tool result has been preserved; no uncertain " +
            "model call will be replayed automatically. Review the verified partial result, " +
            "then raise or remove the cost cap to continue, retry the current role, or abort.";
        return await AppendAsync(snapshot, StoredKind.UserDirectionRequired,
            StoredActor.System, partial, "Partial completion", partial,
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

    private static string WithRetryGuidance(string prompt, GoalRetryGuidance? guidance) =>
        guidance is null
            ? prompt
            : $$"""
                {{prompt}}

                USER RETRY GUIDANCE
                Treat the following as additional user direction for this retry. It does not
                expand your tool or file-area authority:
                {{guidance.Value}}
                """;

}
