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
        GoalTaskView plannedTask = Assert.Single(planning[^1].Tasks);
        Assert.Equal(GoalTaskState.Pending, plannedTask.State);
        goals.Approve();

        List<GoalWorkflowSnapshot> resumed = await CollectAsync(service.ResumeAsync(new(
            goals.Goal.Id,
            new(2048),
            new(512))));

        GoalWorkflowSnapshot completedReview = resumed[^1];
        Assert.Equal(GoalWorkflowState.AwaitingAcceptance, completedReview.State);
        Assert.Equal(1, completedReview.ReviewCycle.Value);
        Assert.Equal(ViewKind.ReviewCompleted,
            completedReview.Activities[^1].Kind);
        Assert.False(completedReview.RequiresUserDirection);
        Assert.Equal(
            [AgentRole.Lead, AgentRole.Implementer, AgentRole.Reviewer],
            agents.Requests.Select(request => request.Role));
        Assert.Equal([1024, 2048, 512],
            agents.Requests.Select(request => request.MaximumOutputTokens?.Value));
        Assert.Equal(GoalTaskState.Completed, Assert.Single(completedReview.Tasks).State);
    }

    [Fact]
    public async Task Lead_delegates_ordered_bounded_tasks_executed_one_at_a_time()
    {
        FakeGoalService goals = new();
        FakeAgentRunner agents = new()
        {
            LeadOutput = """
                {"plan":"Implement two bounded slices.","tasks":[{"title":"Data slice","objective":"Add persistence.","fileAreas":["src/Data"],"acceptanceCriteria":["Store tests pass."]},{"title":"Logic slice","objective":"Add orchestration.","fileAreas":["src/Logic"],"acceptanceCriteria":["Workflow tests pass."]}]}
                """,
        };
        InMemoryGoalWorkflowStore store = new();
        GoalWorkflowService service = CreateService(store, goals, agents);
        GoalWorkflowSnapshot planned = (await CollectAsync(
            service.StartPlanningAsync(new(goals.Goal.Id, new(512)))))[^1];
        goals.Approve();

        GoalWorkflowSnapshot result = (await CollectAsync(service.ResumeAsync(new(
            goals.Goal.Id, new(512), new(512)))))[^1];

        Assert.Equal(["Data slice", "Logic slice"],
            planned.Tasks.Select(task => task.Title.Value));
        Assert.All(result.Tasks, task => Assert.Equal(GoalTaskState.Completed, task.State));
        Assert.Equal(
            [AgentRole.Lead, AgentRole.Implementer, AgentRole.Implementer,
                AgentRole.Reviewer],
            agents.Requests.Select(request => request.Role));
        Assert.Contains("src/Data", agents.Requests[1].Task.Value, StringComparison.Ordinal);
        Assert.Equal(["src/Data"], agents.Requests[1].FileAreas?.Select(area => area.Value));
        Assert.DoesNotContain("src/Logic", agents.Requests[1].Task.Value,
            StringComparison.Ordinal);
        Assert.Contains("src/Logic", agents.Requests[2].Task.Value, StringComparison.Ordinal);
        Assert.Equal(["src/Logic"], agents.Requests[2].FileAreas?.Select(area => area.Value));
    }

    [Fact]
    public async Task Reconciles_a_durable_task_report_without_replaying_implementer()
    {
        FakeGoalService goals = new();
        goals.Approve();
        InMemoryGoalWorkflowStore store = new();
        StoredRunId runId = new(Guid.NewGuid().ToString("N"));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await store.StartAsync(
            new(runId, new(goals.Goal.Id.Value), StoredState.Running, new(0), now, now),
            Checkpoint(runId, 1, StoredKind.Started, now));
        await store.AppendAsync(Checkpoint(runId, 0, StoredKind.LeadCallStarted, now),
            StoredKind.Started, StoredState.Running, StoredState.Running);
        await store.AppendAsync(Checkpoint(runId, 0, StoredKind.PlanProposed, now),
            StoredKind.LeadCallStarted, StoredState.Running,
            StoredState.AwaitingPlanApproval);
        await store.AppendAsync(Checkpoint(runId, 0, StoredKind.PlanApproved, now),
            StoredKind.PlanProposed, StoredState.AwaitingPlanApproval, StoredState.Running);
        StoredGoalWorkflowTask task = Task(runId, 1, now);
        await store.CreateAsync(runId, [task]);
        await store.AppendAsync(Checkpoint(runId, 0,
                StoredKind.ImplementerCallStarted, now),
            StoredKind.PlanApproved, StoredState.Running, StoredState.Running);
        await store.StartAsync(task.Id, now);
        await store.CompleteAsync(task.Id, new("Durable implementation report."), now);
        FakeAgentRunner agents = new();
        GoalWorkflowService service = CreateService(store, goals, agents);

        List<GoalWorkflowSnapshot> snapshots = await CollectAsync(service.ResumeAsync(new(
            goals.Goal.Id, new(512), new(512))));

        Assert.Equal(ViewKind.ImplementationProduced, snapshots[0].Activities[^1].Kind);
        Assert.Contains("Recovered durable delegated task",
            snapshots[0].Activities[^1].Summary.Value, StringComparison.Ordinal);
        Assert.Equal([AgentRole.Reviewer], agents.Requests.Select(request => request.Role));
        Assert.Equal(GoalWorkflowState.AwaitingAcceptance, snapshots[^1].State);
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
            new(runId, new(goals.Goal.Id.Value), StoredState.Running, new(0), now, now),
            Checkpoint(runId, 1, StoredKind.Started, now));
        await store.AppendAsync(
            Checkpoint(runId, 0, StoredKind.LeadCallStarted, now),
            StoredKind.Started, StoredState.Running, StoredState.Running);
        await store.CreateAsync(runId, [Task(runId, 1, now)]);
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
            new(runId, new(goals.Goal.Id.Value), StoredState.Running, new(0), now, now),
            Checkpoint(runId, 1, StoredKind.Started, now));
        await store.AppendAsync(
            Checkpoint(runId, 0, StoredKind.LeadCallStarted, now),
            StoredKind.Started, StoredState.Running, StoredState.Running);
        await store.CreateAsync(runId, [Task(runId, 1, now)]);
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
        Assert.Equal(2, result.ReviewCycle.Value);
        Assert.Equal(
            [AgentRole.Lead, AgentRole.Implementer, AgentRole.Reviewer,
                AgentRole.Implementer, AgentRole.Reviewer],
            agents.Requests.Select(request => request.Role));
        Assert.Contains("configured 2-cycle limit", result.Activities[^1].Summary.Value,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reviewer_findings_drive_a_bounded_correction_then_re_review()
    {
        FakeGoalService goals = new();
        FakeAgentRunner agents = new();
        agents.ReviewerOutputs.Enqueue(
            "{\"decision\":\"revise\",\"summary\":\"Add the missing boundary test.\"}");
        agents.ReviewerOutputs.Enqueue(
            "{\"decision\":\"accept\",\"summary\":\"Boundary test is now durable.\"}");
        InMemoryGoalWorkflowStore store = new();
        GoalWorkflowService service = CreateService(store, goals, agents);
        await CollectAsync(service.StartPlanningAsync(new(goals.Goal.Id, new(512))));
        goals.Approve();

        GoalWorkflowSnapshot result = (await CollectAsync(service.ResumeAsync(new(
            goals.Goal.Id, new(512), new(512)))))[^1];

        Assert.Equal(GoalWorkflowState.AwaitingAcceptance, result.State);
        Assert.Equal(2, result.ReviewCycle.Value);
        Assert.Equal(
            [AgentRole.Lead, AgentRole.Implementer, AgentRole.Reviewer,
                AgentRole.Implementer, AgentRole.Reviewer],
            agents.Requests.Select(request => request.Role));
        AgentRunRequest correction = agents.Requests[^2];
        Assert.Contains("Add the missing boundary test", correction.Task.Value,
            StringComparison.Ordinal);
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

    [Fact]
    public async Task Explicit_retry_recovers_a_definitive_lead_provider_outage()
    {
        FakeGoalService goals = new();
        FakeAgentRunner agents = new();
        agents.Failures.Enqueue((AgentRole.Lead, "provider_unavailable", "Provider unavailable"));
        InMemoryGoalWorkflowStore store = new();
        GoalWorkflowService service = CreateService(store, goals, agents);

        GoalWorkflowSnapshot failed = (await CollectAsync(
            service.StartPlanningAsync(new(goals.Goal.Id, new(512)))))[^1];

        Assert.Equal(GoalWorkflowState.NeedsDirection, failed.State);
        Assert.Equal(GoalWorkflowRetryRole.Lead, failed.RetryRole);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await CollectAsync(service.RetryAsync(new(
                goals.Goal.Id,
                GoalWorkflowRetryRole.Reviewer,
                new(768),
                new("Use a different approach.")))));
        GoalWorkflowSnapshot recovered = (await CollectAsync(service.RetryAsync(new(
            goals.Goal.Id,
            GoalWorkflowRetryRole.Lead,
            new(768),
            new("Inspect the actual workspace before planning.")))))[^1];

        Assert.Equal(GoalWorkflowState.AwaitingPlanApproval, recovered.State);
        Assert.Null(recovered.RetryRole);
        Assert.Contains(recovered.Evidence, item =>
            item.Title.Value == "Explicit retry" &&
            item.Content.Value.Contains("768 tokens", StringComparison.Ordinal));
        Assert.Equal([AgentRole.Lead, AgentRole.Lead],
            agents.Requests.Select(request => request.Role));
        Assert.Contains("Inspect the actual workspace before planning.",
            agents.Requests[^1].Task.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Abort_without_an_existing_run_is_durable_and_prevents_restart()
    {
        FakeGoalService goals = new();
        InMemoryGoalWorkflowStore store = new();
        GoalWorkflowService service = CreateService(store, goals, new FakeAgentRunner());

        GoalWorkflowSnapshot aborted = await service.AbortAsync(new(
            goals.Goal.Id,
            new("The objective is obsolete; start over with corrected input.")));

        Assert.Equal(GoalWorkflowState.Aborted, aborted.State);
        Assert.Contains(aborted.Evidence, evidence =>
            evidence.Title.Value == "Goal aborted" &&
            evidence.Content.Value.Contains("objective is obsolete", StringComparison.Ordinal));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await CollectAsync(service.StartPlanningAsync(new(goals.Goal.Id, new(512)))));
    }

    [Fact]
    public async Task Explicit_retry_recovers_an_implementer_budget_failure_at_safe_boundary()
    {
        FakeGoalService goals = new();
        FakeAgentRunner agents = new();
        InMemoryGoalWorkflowStore store = new();
        GoalWorkflowService service = CreateService(store, goals, agents);
        await CollectAsync(service.StartPlanningAsync(new(goals.Goal.Id, new(512))));
        goals.Approve();
        agents.Failures.Enqueue((AgentRole.Implementer, "budget_exhausted", "Budget exhausted"));

        GoalWorkflowSnapshot failed = (await CollectAsync(service.ResumeAsync(new(
            goals.Goal.Id, new(512), new(512)))))[^1];

        Assert.Equal(GoalWorkflowState.NeedsDirection, failed.State);
        Assert.Equal(GoalWorkflowRetryRole.Implementer, failed.RetryRole);
        Assert.Equal(GoalTaskState.InProgress, Assert.Single(failed.Tasks).State);
        GoalWorkflowSnapshot retried = (await CollectAsync(service.RetryAsync(new(
            goals.Goal.Id,
            GoalWorkflowRetryRole.Implementer,
            new(1024),
            new("Apply the bounded task without repeating the failed call.")))))[^1];

        Assert.Equal(GoalWorkflowState.Running, retried.State);
        Assert.True(retried.CanResume);
        Assert.Equal(GoalTaskState.Completed, Assert.Single(retried.Tasks).State);
        GoalWorkflowSnapshot completed = (await CollectAsync(service.ResumeAsync(new(
            goals.Goal.Id, new(512), new(512)))))[^1];
        Assert.Equal(GoalWorkflowState.AwaitingAcceptance, completed.State);
    }

    [Fact]
    public async Task Explicit_retry_without_guidance_reenters_the_normal_reviewer_cycle()
    {
        FakeGoalService goals = new();
        FakeAgentRunner agents = new();
        InMemoryGoalWorkflowStore store = new();
        GoalWorkflowService service = CreateService(store, goals, agents);
        await CollectAsync(service.StartPlanningAsync(new(goals.Goal.Id, new(512))));
        goals.Approve();
        agents.Failures.Enqueue((AgentRole.Reviewer, "provider_unavailable", "Provider unavailable"));
        GoalWorkflowSnapshot failed = (await CollectAsync(service.ResumeAsync(new(
            goals.Goal.Id, new(512), new(512)))))[^1];

        GoalWorkflowSnapshot recovered = (await CollectAsync(service.RetryAsync(new(
            goals.Goal.Id,
            GoalWorkflowRetryRole.Reviewer,
            new(1_000_000)))))[^1];

        Assert.Equal(GoalWorkflowRetryRole.Reviewer, failed.RetryRole);
        Assert.Equal(GoalWorkflowState.AwaitingAcceptance, recovered.State);
        Assert.Equal(1, recovered.ReviewCycle.Value);
        Assert.Equal([AgentRole.Lead, AgentRole.Implementer, AgentRole.Reviewer, AgentRole.Reviewer],
            agents.Requests.Select(request => request.Role));
        Assert.Equal(1_000_000, agents.Requests[^1].MaximumOutputTokens?.Value);
        Assert.DoesNotContain("USER RETRY GUIDANCE", agents.Requests[^1].Task.Value,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Retried_reviewer_revision_stops_before_a_separately_authorized_correction()
    {
        FakeGoalService goals = new();
        FakeAgentRunner agents = new();
        InMemoryGoalWorkflowStore store = new();
        GoalWorkflowService service = CreateService(store, goals, agents);
        await CollectAsync(service.StartPlanningAsync(new(goals.Goal.Id, new(512))));
        goals.Approve();
        agents.Failures.Enqueue((AgentRole.Reviewer, "provider_unavailable", "Provider unavailable"));
        await CollectAsync(service.ResumeAsync(new(goals.Goal.Id, new(512), new(512))));
        agents.ReviewerOutputs.Enqueue(
            "{\"decision\":\"revise\",\"summary\":\"Add the boundary case.\"}");

        GoalWorkflowSnapshot reviewed = (await CollectAsync(service.RetryAsync(new(
            goals.Goal.Id,
            GoalWorkflowRetryRole.Reviewer,
            new(768),
            new("Focus on the missing boundary case.")))))[^1];

        Assert.Equal(GoalWorkflowState.Running, reviewed.State);
        Assert.True(reviewed.CanResume);
        Assert.Equal(AgentRole.Reviewer, agents.Requests[^1].Role);
        Assert.Equal(4, agents.Requests.Count);
        GoalWorkflowSnapshot completed = (await CollectAsync(service.ResumeAsync(new(
            goals.Goal.Id, new(640), new(896)))))[^1];
        Assert.Equal(GoalWorkflowState.AwaitingAcceptance, completed.State);
        Assert.Equal(AgentRole.Implementer, agents.Requests[^2].Role);
        Assert.Equal(640, agents.Requests[^2].MaximumOutputTokens?.Value);
        Assert.Contains("Add the boundary case", agents.Requests[^2].Task.Value,
            StringComparison.Ordinal);
        Assert.Equal(AgentRole.Reviewer, agents.Requests[^1].Role);
        Assert.Equal(896, agents.Requests[^1].MaximumOutputTokens?.Value);
    }

    private static GoalWorkflowService CreateService(
        InMemoryGoalWorkflowStore store,
        IGoalService goals,
        IAgentRoleRunner agents) => new(store, store, goals, agents, new FixedTimeProvider());

    private static StoredGoalWorkflowTask Task(
        StoredRunId runId,
        int sequence,
        DateTimeOffset createdAt) => new(
        new(Guid.NewGuid().ToString("N")), runId, new(sequence), new($"Task {sequence}"),
        new("Implement the bounded change."), new("src/"), new("- Build succeeds"),
        GoalWorkflowTaskState.Pending, Report: null, createdAt,
        StartedAt: null, CompletedAt: null);

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
    }
}
