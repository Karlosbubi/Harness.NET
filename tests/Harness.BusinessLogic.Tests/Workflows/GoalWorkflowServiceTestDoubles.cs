using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Tools;
using Harness.BusinessLogic.Workflows;
using Harness.DataAccess.Workflows;
using StoredActor = Harness.DataAccess.Workflows.WorkflowActor;
using StoredKind = Harness.DataAccess.Workflows.GoalWorkflowCheckpointKind;
using StoredRunId = Harness.DataAccess.Workflows.GoalWorkflowRunId;
using StoredState = Harness.DataAccess.Workflows.GoalWorkflowRunState;
using ViewKind = Harness.BusinessLogic.Workflows.GoalWorkflowCheckpointKind;

namespace Harness.BusinessLogic.Tests.Workflows;

public sealed partial class GoalWorkflowServiceTests
{
    private sealed class FakeAgentRunner : IAgentRoleRunner
    {
        internal List<AgentRunRequest> Requests { get; } = [];
        internal Queue<(AgentRole Role, string Code, string Message)> Failures { get; } = new();
        internal string LeadOutput { get; init; } = """
            {"plan":"Inspect, implement, build, test, and review.","tasks":[{"title":"Implement change","objective":"Implement the approved bounded change.","fileAreas":["src/"],"acceptanceCriteria":["Build and focused tests pass."]}]}
            """;
        internal string ReviewerOutput { get; init; } =
            "{\"decision\":\"accept\",\"summary\":\"Diff and evidence are sound.\"}";
        internal Queue<string> ReviewerOutputs { get; } = new();
        internal AgentRole? CancelRole { get; init; }
        internal CancellationTokenSource? Cancellation { get; init; }

        public ValueTask<AgentRunResult> RunAsync(
            AgentRunRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (Failures.TryPeek(out var failure) && failure.Role == request.Role)
            {
                Failures.Dequeue();
                return ValueTask.FromResult(new AgentRunResult(
                    request.Role,
                    Output: null,
                    ErrorCode: new(failure.Code),
                    Error: new(failure.Message)));
            }

            if (request.Role == CancelRole)
            {
                Cancellation!.Cancel();
                throw new OperationCanceledException(Cancellation.Token);
            }

            string output = request.Role switch
            {
                AgentRole.Lead => LeadOutput,
                AgentRole.Implementer => "Implemented and verified through typed tools.",
                AgentRole.Reviewer => ReviewerOutputs.TryDequeue(out string? reviewerOutput)
                    ? reviewerOutput
                    : ReviewerOutput,
                _ => throw new ArgumentOutOfRangeException(nameof(request)),
            };
            return ValueTask.FromResult(new AgentRunResult(
                request.Role, new(output), ErrorCode: null, Error: null));
        }
    }

    private sealed class AdvancingToolEvidenceService : IToolEvidenceService
    {
        private int reads;

        public ValueTask<ToolEvidenceSnapshot> ListAsync(
            string goalId,
            CancellationToken cancellationToken = default)
        {
            int completedMutations = ++reads;
            ToolEvidenceView[] items = Enumerable.Range(1, completedMutations)
                .Select(index => new ToolEvidenceView(
                    new($"evidence-{index}"),
                    goalId,
                    new($"correlation-{index}"),
                    ToolKind.FileEdit,
                    "{}",
                    ToolEvidenceState.Succeeded,
                    "{}",
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow))
                .ToArray();
            return ValueTask.FromResult(new ToolEvidenceSnapshot(items, null, null));
        }
    }

    private sealed class EmptyToolEvidenceService : IToolEvidenceService
    {
        public ValueTask<ToolEvidenceSnapshot> ListAsync(
            string goalId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ToolEvidenceSnapshot([], null, null));
    }

    private sealed class FakeGoalService : IGoalService
    {
        private readonly DateTimeOffset now = DateTimeOffset.Parse("2026-07-28T18:00:00Z");

        internal FakeGoalService()
        {
            Goal = new(new(Guid.NewGuid().ToString("N")), Guid.NewGuid().ToString("N"),
                "Goal", "Objective", new(2), RemoteBudget: null, GoalState.Draft, now, now);
        }

        internal GoalView Goal { get; private set; }
        internal PlanView? Plan { get; private set; }

        internal void SetPendingPlan()
        {
            Plan = new(new(Guid.NewGuid().ToString("N")), Goal.Id, new(1), "Durable plan",
                PlanState.Pending, now, now);
            Goal = Goal with { State = GoalState.AwaitingPlanApproval };
        }

        internal void Approve()
        {
            Plan ??= new(new(Guid.NewGuid().ToString("N")), Goal.Id, new(1), "Plan",
                PlanState.Pending, now, now);
            Plan = Plan with { State = PlanState.Approved };
            Goal = Goal with { State = GoalState.Approved };
        }

        public ValueTask<GoalView?> GetAsync(
            GoalId goalId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<GoalView?>(goalId == Goal.Id ? Goal : null);

        public ValueTask<PlanView?> GetCurrentPlanAsync(
            GoalId goalId,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(Plan);

        public ValueTask<PlanResult> ProposePlanAsync(
            PlanProposalRequest request,
            CancellationToken cancellationToken = default)
        {
            Plan = new(new(Guid.NewGuid().ToString("N")), Goal.Id, new(1), request.Content,
                PlanState.Pending, now, now);
            Goal = Goal with { State = GoalState.AwaitingPlanApproval };
            return ValueTask.FromResult(new PlanResult(
                Goal, Plan, Approval: null, Worktree: null, ErrorCode: null, Error: null));
        }

        public ValueTask<GoalResult> CreateAsync(
            GoalCreateRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<GoalView>> ListAsync(
            string workspaceId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<GoalResult> UpdateSettingsAsync(
            GoalSettingsUpdateRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<GoalBudgetExtensionResult> ExtendRemoteBudgetAsync(
            GoalBudgetExtensionRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<PlanResult> DecidePlanAsync(
            PlanDecisionRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class InMemoryGoalWorkflowStore : IGoalWorkflowStore, IGoalWorkflowTaskStore
    {
        private readonly List<StoredGoalWorkflowTask> tasks = [];
        internal StoredGoalWorkflowSnapshot? Snapshot { get; private set; }

        public ValueTask<StoredGoalWorkflowSnapshot?> GetLatestAsync(
            GoalWorkflowGoalId goalId,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(Snapshot);

        public ValueTask<StoredGoalWorkflowSnapshot> StartAsync(
            StoredGoalWorkflowRun run,
            StoredGoalWorkflowCheckpoint checkpoint,
            CancellationToken cancellationToken = default)
        {
            Snapshot = new(run, [checkpoint]);
            return ValueTask.FromResult(Snapshot);
        }

        public ValueTask<StoredGoalWorkflowSnapshot> AppendAsync(
            StoredGoalWorkflowCheckpoint checkpoint,
            StoredKind expectedCheckpoint,
            StoredState expectedState,
            StoredState nextState,
            CancellationToken cancellationToken = default,
            GoalWorkflowReviewCycle? nextReviewCycle = null)
        {
            if (Snapshot is null || Snapshot.Run.State != expectedState ||
                Snapshot.Checkpoints[^1].Kind != expectedCheckpoint)
            {
                throw new InvalidOperationException("Stale transition.");
            }

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

        public ValueTask<StoredGoalWorkflowSnapshot> AbortAsync(
            GoalWorkflowGoalId goalId,
            WorkflowCheckpointSummary reason,
            DateTimeOffset abortedAt,
            CancellationToken cancellationToken = default)
        {
            StoredRunId runId = Snapshot?.Run.Id ?? new(Guid.NewGuid().ToString("N"));
            StoredGoalWorkflowCheckpoint checkpoint = new(
                Guid.NewGuid().ToString("N"), runId,
                (Snapshot?.Checkpoints.Count ?? 0) + 1,
                StoredKind.UserDirectionRequired,
                Harness.DataAccess.Workflows.WorkflowActor.System,
                new("User aborted the goal."),
                new("Goal aborted"),
                new(reason.Value),
                abortedAt);
            StoredGoalWorkflowRun run = Snapshot is null
                ? new(runId, goalId, StoredState.Completed, new(0), abortedAt, abortedAt)
                : Snapshot.Run with { State = StoredState.Completed, UpdatedAt = abortedAt };
            Snapshot = new(run,
                Snapshot is null ? [checkpoint] : [.. Snapshot.Checkpoints, checkpoint]);
            return ValueTask.FromResult(Snapshot);
        }

        public ValueTask<IReadOnlyList<StoredGoalWorkflowTask>> CreateAsync(
            StoredRunId runId,
            IReadOnlyList<StoredGoalWorkflowTask> values,
            CancellationToken cancellationToken = default)
        {
            tasks.AddRange(values);
            return ValueTask.FromResult<IReadOnlyList<StoredGoalWorkflowTask>>(values);
        }

        public ValueTask<IReadOnlyList<StoredGoalWorkflowTask>> ListAsync(
            StoredRunId runId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<StoredGoalWorkflowTask>>(
                tasks.Where(task => task.RunId == runId)
                    .OrderBy(task => task.Sequence.Value).ToArray());

        public ValueTask<StoredGoalWorkflowTask> StartAsync(
            GoalWorkflowTaskId taskId,
            DateTimeOffset startedAt,
            CancellationToken cancellationToken = default)
        {
            int index = tasks.FindIndex(task => task.Id == taskId);
            tasks[index] = tasks[index] with
            {
                State = GoalWorkflowTaskState.InProgress,
                StartedAt = startedAt,
            };
            return ValueTask.FromResult(tasks[index]);
        }

        public ValueTask<StoredGoalWorkflowTask> CompleteAsync(
            GoalWorkflowTaskId taskId,
            GoalWorkflowTaskReport report,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken = default)
        {
            int index = tasks.FindIndex(task => task.Id == taskId);
            tasks[index] = tasks[index] with
            {
                State = GoalWorkflowTaskState.Completed,
                Report = report,
                CompletedAt = completedAt,
            };
            return ValueTask.FromResult(tasks[index]);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            DateTimeOffset.Parse("2026-07-28T18:00:00Z");
    }}
