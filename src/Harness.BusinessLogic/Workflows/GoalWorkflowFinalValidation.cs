using System.Text.RegularExpressions;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Tools;
using Harness.DataAccess.Workflows;
using StoredActor = Harness.DataAccess.Workflows.WorkflowActor;
using StoredKind = Harness.DataAccess.Workflows.GoalWorkflowCheckpointKind;
using StoredState = Harness.DataAccess.Workflows.GoalWorkflowRunState;

namespace Harness.BusinessLogic.Workflows;

internal sealed partial class GoalWorkflowService
{
    private async ValueTask<StoredGoalWorkflowSnapshot> RetryCompletedCorrectionAsync(
        GoalView goal,
        PlanView plan,
        IReadOnlyList<StoredGoalWorkflowTask> tasks,
        StoredGoalWorkflowSnapshot snapshot,
        GoalRetryGuidance? guidance,
        CancellationToken cancellationToken)
    {
        StoredGoalWorkflowCheckpoint? review = snapshot.Checkpoints
            .LastOrDefault(item => item.Kind is StoredKind.ReviewCompleted);
        string task = $$"""
            Continue the bounded Implementer correction for goal '{{goal.Title}}'. Inspect before
            editing, preserve completed work, and correct the latest concrete validation or review
            finding through typed tools. Do not report completion without new durable evidence.

            FULL GOAL OBJECTIVE (AUTHORITATIVE)
            {{goal.Objective}}

            APPROVED PLAN
            {{plan.Content}}

            LATEST REVIEW FINDINGS
            {{review?.EvidenceContent?.Value ?? "No model review preceded this validation retry."}}
            """;
        task = WithRetryGuidance(task, guidance) +
            await LatestFailedToolFeedbackAsync(goal.Id, cancellationToken);
        HashSet<string> evidenceBefore = await EvidenceIdsAsync(goal.Id, cancellationToken);
        AgentRunResult result;
        try
        {
            result = await agentRunner.RunAsync(new(
                goal.Id, AgentRole.Implementer, new(task), FileAreas(tasks)), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await MarkUncertainAsync(snapshot,
                "Retried correction was cancelled after it started; inspect durable evidence.");
            throw;
        }

        if (result.Output is null)
        {
            return await MarkAgentFailureAsync(snapshot, result,
                $"Retried correction failed and was not replayed: {result.Error?.Value}",
                cancellationToken);
        }

        if (!await HasNewDurableEvidenceAsync(
                goal.Id, evidenceBefore, includeVerification: true, CancellationToken.None))
        {
            return await MarkDirectionAsync(snapshot,
                "Retried correction returned without successful new mutation or verification evidence.",
                CancellationToken.None);
        }

        return await AppendAsync(snapshot, StoredKind.ImplementationProduced,
            StoredActor.Implementer,
            "Implementer completed the explicitly retried correction.",
            "Retried correction report", result.Output.Value,
            StoredKind.ImplementerCallStarted, StoredState.Running, StoredState.Running,
            CancellationToken.None);
    }

    private async ValueTask<GoalFinalValidationOutcome> ValidateAndRepairFinalAsync(
        GoalView goal,
        PlanView plan,
        IReadOnlyList<StoredGoalWorkflowTask> tasks,
        StoredGoalWorkflowSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        FinalValidationFailure? failure = await ValidateFinalAsync(goal.Id, cancellationToken);
        if (failure is null)
        {
            return new(snapshot, [], ImplementationOutput: null, ShouldStop: false);
        }

        string repairTask = FinalValidationRepairTask(goal, plan, failure);
        snapshot = await AppendAsync(snapshot, StoredKind.ImplementerCallStarted,
            StoredActor.Implementer,
            "Implementer final-validation repair started from deterministic diagnostics.",
            "Final validation repair prompt", repairTask,
            StoredKind.ImplementationProduced, StoredState.Running, StoredState.Running,
            cancellationToken);
        List<StoredGoalWorkflowSnapshot> snapshots = [snapshot];
        HashSet<string> evidenceBefore = await EvidenceIdsAsync(goal.Id, cancellationToken);
        AgentRunResult repair;
        try
        {
            repair = await agentRunner.RunAsync(new(
                goal.Id, AgentRole.Implementer, new(repairTask),
                RepairFileAreas(failure, tasks)),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await MarkUncertainAsync(snapshot,
                "Final-validation repair was cancelled after it started; inspect durable evidence.");
            throw;
        }

        if (repair.Output is null)
        {
            snapshot = await MarkAgentFailureAsync(snapshot, repair,
                $"Final-validation repair failed and was not replayed: {repair.Error?.Value}",
                cancellationToken);
            snapshots.Add(snapshot);
            return new(snapshot, snapshots, ImplementationOutput: null, ShouldStop: true);
        }

        if (!await HasNewDurableEvidenceAsync(
                goal.Id, evidenceBefore, includeVerification: false, CancellationToken.None))
        {
            snapshot = await MarkDirectionAsync(snapshot,
                "Final-validation repair returned without successful new mutation evidence.",
                CancellationToken.None);
            snapshots.Add(snapshot);
            return new(snapshot, snapshots, ImplementationOutput: null, ShouldStop: true);
        }

        snapshot = await AppendAsync(snapshot, StoredKind.ImplementationProduced,
            StoredActor.Implementer,
            "Implementer completed the bounded final-validation repair.",
            "Final validation repair report", repair.Output.Value,
            StoredKind.ImplementerCallStarted, StoredState.Running, StoredState.Running,
            CancellationToken.None);
        snapshots.Add(snapshot);

        failure = await ValidateFinalAsync(goal.Id, CancellationToken.None);
        if (failure is not null)
        {
            snapshot = await MarkDirectionAsync(snapshot,
                "Deterministic final validation still fails after one bounded repair pass. " +
                "Inspect the latest failed tool evidence and retry the Implementer correction.",
                CancellationToken.None);
            snapshots.Add(snapshot);
            return new(snapshot, snapshots, repair.Output, ShouldStop: true);
        }

        return new(snapshot, snapshots, repair.Output, ShouldStop: false);
    }

    private async ValueTask<FinalValidationFailure?> ValidateFinalAsync(
        GoalId goalId,
        CancellationToken cancellationToken)
    {
        if (mutationService is null)
        {
            return null;
        }

        foreach (DotNetOperation operation in new[] { DotNetOperation.Build, DotNetOperation.Test })
        {
            DotNetOperationView result = await mutationService.RunDotNetAsync(new(
                goalId.Value,
                new ToolCorrelationId($"workflow-{operation.ToString().ToLowerInvariant()}-" +
                    Guid.NewGuid().ToString("N")),
                operation), cancellationToken);
            if (!AgentRunValidationPolicy.Succeeded(result))
            {
                AgentRunResult failed = AgentRunValidationPolicy.Failure(
                    AgentRole.Implementer, result);
                return new(operation, failed.Error?.Value ?? "Validation failed without details.");
            }
        }

        return null;
    }

    private static string FinalValidationRepairTask(
        GoalView goal,
        PlanView plan,
        FinalValidationFailure failure) => $$"""
        Correct only the concrete deterministic final-validation failure for goal
        '{{goal.Title}}'. Inspect the cited source and preserve every completed slice. Use typed
        tools, make the smallest bounded repair, and do not claim success without mutation evidence.
        Inspect the failing test setup and assertion before changing production behavior. When
        independent contracts pass but a generated test fails, correct invalid test data or setup;
        never weaken a production invariant merely to satisfy an impossible test sequence.

        FULL GOAL OBJECTIVE (AUTHORITATIVE)
        {{goal.Objective}}

        APPROVED PLAN
        {{plan.Content}}

        DETERMINISTIC {{failure.Operation.ToString().ToUpperInvariant()}} FAILURE
        {{failure.Summary}}
        """;

    private static IReadOnlyList<AgentFileArea> RepairFileAreas(
        FinalValidationFailure failure,
        IReadOnlyList<StoredGoalWorkflowTask> tasks)
    {
        IReadOnlyList<AgentFileArea> grants = FileAreas(tasks);
        AgentFileArea? implicated = Regex.Matches(
                failure.Summary,
                @"(?<path>(?:src|tests)/[A-Za-z0-9_./-]+\.cs)",
                RegexOptions.CultureInvariant)
            .Cast<Match>()
            .Select(match => match.Groups["path"].Value)
            .Where(path => grants.Any(grant =>
                path.Equals(grant.Value, StringComparison.Ordinal) ||
                path.StartsWith(grant.Value.TrimEnd('/') + "/", StringComparison.Ordinal)))
            .GroupBy(path => path, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key.StartsWith("src/", StringComparison.Ordinal) ? 0 : 1)
            .Select(group => new AgentFileArea(group.Key))
            .FirstOrDefault();
        return implicated is null ? grants : [implicated];
    }

    private sealed record FinalValidationFailure(
        DotNetOperation Operation,
        string Summary);

    private sealed record GoalFinalValidationOutcome(
        StoredGoalWorkflowSnapshot Snapshot,
        IReadOnlyList<StoredGoalWorkflowSnapshot> Snapshots,
        AgentOutput? ImplementationOutput,
        bool ShouldStop);
}
