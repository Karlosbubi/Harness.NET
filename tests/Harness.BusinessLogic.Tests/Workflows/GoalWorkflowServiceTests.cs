using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Workflows;
using Harness.DataAccess.Workflows;
using StoredActor = Harness.DataAccess.Workflows.WorkflowActor;
using StoredKind = Harness.DataAccess.Workflows.GoalWorkflowCheckpointKind;
using StoredRunId = Harness.DataAccess.Workflows.GoalWorkflowRunId;
using StoredState = Harness.DataAccess.Workflows.GoalWorkflowRunState;
using ViewKind = Harness.BusinessLogic.Workflows.GoalWorkflowCheckpointKind;

namespace Harness.BusinessLogic.Tests.Workflows;

public sealed class GoalWorkflowServiceTests
{
    [Fact]
    public async Task Runs_real_role_sequence_around_plan_approval()
    {
        FakeGoalService goals = new();
        FakeAgentRunner agents = new();
        InMemoryGoalWorkflowStore store = new();
        GoalWorkflowService service = CreateService(store, goals, agents);

        List<GoalWorkflowSnapshot> planning = await CollectAsync(
            service.StartPlanningAsync(new(goals.Goal.Id, new(1024))));

        Assert.Equal(
            [
                ViewKind.Started,
                ViewKind.LeadCallStarted,
                ViewKind.PlanProposed,
            ],
            planning[^1].Activities.Select(item => item.Kind));
        Assert.Equal(GoalWorkflowState.AwaitingPlanApproval, planning[^1].State);
        goals.Approve();

        List<GoalWorkflowSnapshot> resumed = await CollectAsync(service.ResumeAsync(new(
            goals.Goal.Id,
            new(2048),
            new(512))));

        GoalWorkflowSnapshot completedReview = resumed[^1];
        Assert.Equal(GoalWorkflowState.AwaitingAcceptance, completedReview.State);
        Assert.Equal(ViewKind.ReviewCompleted,
            completedReview.Activities[^1].Kind);
        Assert.False(completedReview.RequiresUserDirection);
        Assert.Equal(
            [AgentRole.Lead, AgentRole.Implementer, AgentRole.Reviewer],
            agents.Requests.Select(request => request.Role));
        Assert.Equal([1024, 2048, 512],
            agents.Requests.Select(request => request.MaximumOutputTokens?.Value));
    }

    [Fact]
    public async Task Does_not_replay_an_uncertain_implementer_call()
    {
        FakeGoalService goals = new();
        goals.Approve();
        InMemoryGoalWorkflowStore store = new();
        StoredRunId runId = new(Guid.NewGuid().ToString("N"));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await store.StartAsync(
            new(runId, new(goals.Goal.Id.Value), StoredState.Running, 0, now, now),
            Checkpoint(runId, 1, StoredKind.Started, now));
        await store.AppendAsync(
            Checkpoint(runId, 0, StoredKind.LeadCallStarted, now),
            StoredKind.Started, StoredState.Running, StoredState.Running);
        await store.AppendAsync(
            Checkpoint(runId, 0, StoredKind.PlanProposed, now),
            StoredKind.LeadCallStarted, StoredState.Running,
            StoredState.AwaitingPlanApproval);
        await store.AppendAsync(
            Checkpoint(runId, 0, StoredKind.PlanApproved, now),
            StoredKind.PlanProposed, StoredState.AwaitingPlanApproval, StoredState.Running);
        await store.AppendAsync(
            Checkpoint(runId, 0, StoredKind.ImplementerCallStarted, now),
            StoredKind.PlanApproved, StoredState.Running, StoredState.Running);
        FakeAgentRunner agents = new();
        GoalWorkflowService service = CreateService(store, goals, agents);

        GoalWorkflowSnapshot result = Assert.Single(await CollectAsync(service.ResumeAsync(new(
            goals.Goal.Id, new(512), new(512)))));

        Assert.Equal(GoalWorkflowState.NeedsDirection, result.State);
        Assert.True(result.RequiresUserDirection);
        Assert.Empty(agents.Requests);
        Assert.Contains("not replayed", result.Evidence[^1].Content.Value,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reconciles_a_durable_plan_without_repeating_the_lead_call()
    {
        FakeGoalService goals = new();
        goals.SetPendingPlan();
        InMemoryGoalWorkflowStore store = new();
        StoredRunId runId = new(Guid.NewGuid().ToString("N"));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await store.StartAsync(
            new(runId, new(goals.Goal.Id.Value), StoredState.Running, 0, now, now),
            Checkpoint(runId, 1, StoredKind.Started, now));
        await store.AppendAsync(
            Checkpoint(runId, 0, StoredKind.LeadCallStarted, now),
            StoredKind.Started, StoredState.Running, StoredState.Running);
        FakeAgentRunner agents = new();
        GoalWorkflowService service = CreateService(store, goals, agents);

        GoalWorkflowSnapshot result = Assert.Single(await CollectAsync(service.ResumeAsync(new(
            goals.Goal.Id, new(512), new(512)))));

        Assert.Equal(GoalWorkflowState.AwaitingPlanApproval, result.State);
        Assert.Equal(ViewKind.PlanProposed, result.Activities[^1].Kind);
        Assert.Empty(agents.Requests);
        Assert.Contains("Recovered", result.Evidence[^1].Title.Value,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reviewer_revision_prevents_acceptance()
    {
        FakeGoalService goals = new();
        FakeAgentRunner agents = new()
        {
            ReviewerOutput = "{\"decision\":\"revise\",\"summary\":\"Tests are missing.\"}",
        };
        InMemoryGoalWorkflowStore store = new();
        GoalWorkflowService service = CreateService(store, goals, agents);
        await CollectAsync(service.StartPlanningAsync(new(goals.Goal.Id, new(512))));
        goals.Approve();

        GoalWorkflowSnapshot result = (await CollectAsync(service.ResumeAsync(new(
            goals.Goal.Id, new(512), new(512)))))[^1];

        Assert.Equal(GoalWorkflowState.NeedsDirection, result.State);
        Assert.True(result.RequiresUserDirection);
    }

    [Fact]
    public async Task Resumes_the_reviewer_from_the_completed_implementation_boundary()
    {
        FakeGoalService goals = new();
        FakeAgentRunner agents = new();
        InMemoryGoalWorkflowStore store = new();
        GoalWorkflowService firstProcess = CreateService(store, goals, agents);
        await CollectAsync(firstProcess.StartPlanningAsync(new(goals.Goal.Id, new(512))));
        goals.Approve();
        await using (IAsyncEnumerator<GoalWorkflowSnapshot> enumerator = firstProcess.ResumeAsync(
                         new(goals.Goal.Id, new(512), new(512))).GetAsyncEnumerator())
        {
            Assert.True(await enumerator.MoveNextAsync());
            Assert.True(await enumerator.MoveNextAsync());
            Assert.True(await enumerator.MoveNextAsync());
            Assert.Equal(ViewKind.ImplementationProduced,
                enumerator.Current.Activities[^1].Kind);
        }

        agents.Requests.Clear();
        GoalWorkflowService restartedProcess = CreateService(store, goals, agents);
        GoalWorkflowSnapshot result = (await CollectAsync(restartedProcess.ResumeAsync(new(
            goals.Goal.Id, new(512), new(512)))))[^1];

        Assert.Equal(GoalWorkflowState.AwaitingAcceptance, result.State);
        Assert.Equal([AgentRole.Reviewer], agents.Requests.Select(request => request.Role));
    }

    [Fact]
    public async Task Cancellation_after_a_role_call_starts_persists_uncertainty()
    {
        FakeGoalService goals = new();
        using CancellationTokenSource cancellation = new();
        FakeAgentRunner agents = new()
        {
            CancelRole = AgentRole.Implementer,
            Cancellation = cancellation,
        };
        InMemoryGoalWorkflowStore store = new();
        GoalWorkflowService service = CreateService(store, goals, agents);
        await CollectAsync(service.StartPlanningAsync(new(goals.Goal.Id, new(512))));
        goals.Approve();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await CollectAsync(service.ResumeAsync(
                new(goals.Goal.Id, new(512), new(512)), cancellation.Token)));

        Assert.NotNull(store.Snapshot);
        Assert.Equal(StoredState.NeedsDirection, store.Snapshot.Run.State);
        Assert.Equal(StoredKind.UserDirectionRequired, store.Snapshot.Checkpoints[^1].Kind);
    }

    private static GoalWorkflowService CreateService(
        IGoalWorkflowStore store,
        IGoalService goals,
        IAgentRoleRunner agents) => new(store, goals, agents, new FixedTimeProvider());

    private static StoredGoalWorkflowCheckpoint Checkpoint(
        StoredRunId runId,
        int sequence,
        StoredKind kind,
        DateTimeOffset createdAt) => new(
        Guid.NewGuid().ToString("N"), runId, sequence, kind, StoredActor.System,
        new(kind.ToString()), null, null, createdAt);

    private static async Task<List<GoalWorkflowSnapshot>> CollectAsync(
        IAsyncEnumerable<GoalWorkflowSnapshot> source)
    {
        List<GoalWorkflowSnapshot> values = [];
        await foreach (GoalWorkflowSnapshot value in source)
        {
            values.Add(value);
        }

        return values;
    }

    private sealed class FakeAgentRunner : IAgentRoleRunner
    {
        internal List<AgentRunRequest> Requests { get; } = [];
        internal string ReviewerOutput { get; init; } =
            "{\"decision\":\"accept\",\"summary\":\"Diff and evidence are sound.\"}";
        internal AgentRole? CancelRole { get; init; }
        internal CancellationTokenSource? Cancellation { get; init; }

        public ValueTask<AgentRunResult> RunAsync(
            AgentRunRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (request.Role == CancelRole)
            {
                Cancellation!.Cancel();
                throw new OperationCanceledException(Cancellation.Token);
            }

            string output = request.Role switch
            {
                AgentRole.Lead => "1. Inspect. 2. Implement. 3. Build and test. 4. Review.",
                AgentRole.Implementer => "Implemented and verified through typed tools.",
                AgentRole.Reviewer => ReviewerOutput,
                _ => throw new ArgumentOutOfRangeException(nameof(request)),
            };
            return ValueTask.FromResult(new AgentRunResult(
                request.Role, new(output), ErrorCode: null, Error: null));
        }
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

        public ValueTask<PlanResult> DecidePlanAsync(
            PlanDecisionRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class InMemoryGoalWorkflowStore : IGoalWorkflowStore
    {
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
            CancellationToken cancellationToken = default)
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
                Snapshot.Run with { State = nextState, UpdatedAt = checkpoint.CreatedAt },
                [.. Snapshot.Checkpoints, appended]);
            return ValueTask.FromResult(Snapshot);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            DateTimeOffset.Parse("2026-07-28T18:00:00Z");
    }
}
