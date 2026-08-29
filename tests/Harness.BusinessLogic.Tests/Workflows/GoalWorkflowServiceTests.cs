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
    [Fact]
    public async Task Runs_real_role_sequence_around_plan_approval()
    {
        FakeGoalService goals = new();
        FakeAgentRunner agents = new();
        InMemoryGoalWorkflowStore store = new();
        GoalWorkflowService service = CreateService(store, goals, agents);

        List<GoalWorkflowSnapshot> planning = await CollectAsync(
            service.StartPlanningAsync(new(goals.Goal.Id)));

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

        List<GoalWorkflowSnapshot> resumed = await CollectAsync(
            service.ResumeAsync(new(goals.Goal.Id)));

        GoalWorkflowSnapshot completedReview = resumed[^1];
        Assert.Equal(GoalWorkflowState.AwaitingAcceptance, completedReview.State);
        Assert.Equal(1, completedReview.ReviewCycle.Value);
        Assert.Equal(ViewKind.ReviewCompleted,
            completedReview.Activities[^1].Kind);
        Assert.False(completedReview.RequiresUserDirection);
        Assert.Equal(
            [AgentRole.Lead, AgentRole.Implementer, AgentRole.Reviewer],
            agents.Requests.Select(request => request.Role));
        Assert.Contains("call inspect_dotnet", agents.Requests[0].Task.Value,
            StringComparison.Ordinal);
        Assert.Contains("find_symbol_references", agents.Requests[0].Task.Value,
            StringComparison.Ordinal);
        Assert.Contains("Objective", agents.Requests[0].Task.Value,
            StringComparison.Ordinal);
        Assert.Contains("FULL GOAL OBJECTIVE (AUTHORITATIVE)",
            agents.Requests[1].Task.Value, StringComparison.Ordinal);
        Assert.Contains("expectedSha256", agents.Requests[1].Task.Value,
            StringComparison.Ordinal);
        Assert.Contains("inspect_code_problems", agents.Requests[1].Task.Value,
            StringComparison.Ordinal);
        Assert.Contains("FULL GOAL OBJECTIVE", agents.Requests[2].Task.Value,
            StringComparison.Ordinal);
        Assert.Contains("Roslyn problems", agents.Requests[2].Task.Value,
            StringComparison.Ordinal);
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
            service.StartPlanningAsync(new(goals.Goal.Id))))[^1];
        goals.Approve();

        GoalWorkflowSnapshot result = (await CollectAsync(
            service.ResumeAsync(new(goals.Goal.Id))))[^1];

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
    public async Task Implementer_report_without_successful_mutation_evidence_does_not_complete_task()
    {
        FakeGoalService goals = new();
        FakeAgentRunner agents = new();
        InMemoryGoalWorkflowStore store = new();
        GoalWorkflowService service = CreateService(
            store, goals, agents, new EmptyToolEvidenceService());
        _ = await CollectAsync(service.StartPlanningAsync(new(goals.Goal.Id)));
        goals.Approve();

        GoalWorkflowSnapshot result = (await CollectAsync(
            service.ResumeAsync(new(goals.Goal.Id))))[^1];

        Assert.Equal(GoalWorkflowState.NeedsDirection, result.State);
        Assert.Equal(GoalWorkflowRetryRole.Implementer, result.RetryRole);
        Assert.Equal(GoalTaskState.InProgress, Assert.Single(result.Tasks).State);
        Assert.Contains("without successful new mutation evidence",
            result.Activities[^1].Summary.Value, StringComparison.Ordinal);
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

        List<GoalWorkflowSnapshot> snapshots = await CollectAsync(
            service.ResumeAsync(new(goals.Goal.Id)));

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

        GoalWorkflowSnapshot result = Assert.Single(await CollectAsync(
            service.ResumeAsync(new(goals.Goal.Id))));

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

        GoalWorkflowSnapshot result = Assert.Single(await CollectAsync(
            service.ResumeAsync(new(goals.Goal.Id))));

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
        await CollectAsync(service.StartPlanningAsync(new(goals.Goal.Id)));
        goals.Approve();

        GoalWorkflowSnapshot result = (await CollectAsync(
            service.ResumeAsync(new(goals.Goal.Id))))[^1];

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
        await CollectAsync(service.StartPlanningAsync(new(goals.Goal.Id)));
        goals.Approve();

        GoalWorkflowSnapshot result = (await CollectAsync(
            service.ResumeAsync(new(goals.Goal.Id))))[^1];

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
        await CollectAsync(firstProcess.StartPlanningAsync(new(goals.Goal.Id)));
        goals.Approve();
        await using (IAsyncEnumerator<GoalWorkflowSnapshot> enumerator = firstProcess.ResumeAsync(
                         new(goals.Goal.Id)).GetAsyncEnumerator())
        {
            Assert.True(await enumerator.MoveNextAsync());
            Assert.True(await enumerator.MoveNextAsync());
            Assert.True(await enumerator.MoveNextAsync());
            Assert.Equal(ViewKind.ImplementationProduced,
                enumerator.Current.Activities[^1].Kind);
        }

        agents.Requests.Clear();
        GoalWorkflowService restartedProcess = CreateService(store, goals, agents);
        GoalWorkflowSnapshot result = (await CollectAsync(
            restartedProcess.ResumeAsync(new(goals.Goal.Id))))[^1];

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
        await CollectAsync(service.StartPlanningAsync(new(goals.Goal.Id)));
        goals.Approve();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await CollectAsync(service.ResumeAsync(
                new(goals.Goal.Id), cancellation.Token)));

        Assert.NotNull(store.Snapshot);
        Assert.Equal(StoredState.NeedsDirection, store.Snapshot.Run.State);
        Assert.Equal(StoredKind.UserDirectionRequired, store.Snapshot.Checkpoints[^1].Kind);
    }

    [Fact]
    public async Task Rejected_lead_delegation_preserves_bounded_output_for_recovery()
    {
        const string rejected = """
            {"plan":"Inspect first.","tasks":[{"title":"Inspect the editor","objective":"Analyze the current editor implementation.","fileAreas":["src/"],"acceptanceCriteria":["Inspection recorded."]}]}
            """;
        FakeGoalService goals = new();
        FakeAgentRunner agents = new() { LeadOutput = rejected };
        InMemoryGoalWorkflowStore store = new();
        GoalWorkflowService service = CreateService(store, goals, agents);

        GoalWorkflowSnapshot result = (await CollectAsync(
            service.StartPlanningAsync(new(goals.Goal.Id))))[^1];

        Assert.Equal(GoalWorkflowState.NeedsDirection, result.State);
        WorkflowEvidenceView evidence = result.Evidence[^1];
        Assert.Equal("Rejected Lead output", evidence.Title.Value);
        Assert.Contains("Standalone discovery", evidence.Content.Value,
            StringComparison.Ordinal);
        Assert.Contains(rejected, evidence.Content.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejected_lead_output_is_truncated_before_it_becomes_recovery_evidence()
    {
        string rejected = "not-json:" + new string('x', 20_000);
        FakeGoalService goals = new();
        FakeAgentRunner agents = new() { LeadOutput = rejected };
        InMemoryGoalWorkflowStore store = new();
        GoalWorkflowService service = CreateService(store, goals, agents);

        GoalWorkflowSnapshot result = (await CollectAsync(
            service.StartPlanningAsync(new(goals.Goal.Id))))[^1];

        WorkflowEvidenceView evidence = result.Evidence[^1];
        Assert.Contains("[truncated]", evidence.Content.Value, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('x', 17_000), evidence.Content.Value,
            StringComparison.Ordinal);
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
            service.StartPlanningAsync(new(goals.Goal.Id))))[^1];

        Assert.Equal(GoalWorkflowState.NeedsDirection, failed.State);
        Assert.Equal(GoalWorkflowRetryRole.Lead, failed.RetryRole);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await CollectAsync(service.RetryAsync(new(
                goals.Goal.Id,
                GoalWorkflowRetryRole.Reviewer,
                new("Use a different approach.")))));
        GoalWorkflowSnapshot recovered = (await CollectAsync(service.RetryAsync(new(
            goals.Goal.Id,
            GoalWorkflowRetryRole.Lead,
            new("Inspect the actual workspace before planning.")))))[^1];

        Assert.Equal(GoalWorkflowState.AwaitingPlanApproval, recovered.State);
        Assert.Null(recovered.RetryRole);
        Assert.Contains(recovered.Evidence, item =>
            item.Title.Value == "Explicit retry");
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
            await CollectAsync(service.StartPlanningAsync(new(goals.Goal.Id))));
    }

    [Fact]
    public async Task Cost_limit_preserves_partial_completion_and_allows_explicit_retry()
    {
        FakeGoalService goals = new();
        FakeAgentRunner agents = new();
        InMemoryGoalWorkflowStore store = new();
        GoalWorkflowService service = CreateService(store, goals, agents);
        await CollectAsync(service.StartPlanningAsync(new(goals.Goal.Id)));
        goals.Approve();
        agents.Failures.Enqueue((AgentRole.Implementer, "remote_cost_cap_exceeded", "Cost cap exhausted"));

        GoalWorkflowSnapshot failed = (await CollectAsync(
            service.ResumeAsync(new(goals.Goal.Id))))[^1];

        Assert.Equal(GoalWorkflowState.PartiallyCompleted, failed.State);
        Assert.Equal(GoalWorkflowRetryRole.Implementer, failed.RetryRole);
        Assert.Equal(GoalTaskState.InProgress, Assert.Single(failed.Tasks).State);
        Assert.Equal("Partial completion", failed.Evidence[^1].Title.Value);
        GoalWorkflowSnapshot retried = (await CollectAsync(service.RetryAsync(new(
            goals.Goal.Id,
            GoalWorkflowRetryRole.Implementer,
            new("Apply the bounded task without repeating the failed call.")))))[^1];

        Assert.Equal(GoalWorkflowState.Running, retried.State);
        Assert.True(retried.CanResume);
        Assert.Equal(GoalTaskState.Completed, Assert.Single(retried.Tasks).State);
        GoalWorkflowSnapshot completed = (await CollectAsync(
            service.ResumeAsync(new(goals.Goal.Id))))[^1];
        Assert.Equal(GoalWorkflowState.AwaitingAcceptance, completed.State);
    }

    [Fact]
    public async Task Explicit_retry_without_guidance_reenters_the_normal_reviewer_cycle()
    {
        FakeGoalService goals = new();
        FakeAgentRunner agents = new();
        InMemoryGoalWorkflowStore store = new();
        GoalWorkflowService service = CreateService(store, goals, agents);
        await CollectAsync(service.StartPlanningAsync(new(goals.Goal.Id)));
        goals.Approve();
        agents.Failures.Enqueue((AgentRole.Reviewer, "provider_unavailable", "Provider unavailable"));
        GoalWorkflowSnapshot failed = (await CollectAsync(
            service.ResumeAsync(new(goals.Goal.Id))))[^1];

        GoalWorkflowSnapshot recovered = (await CollectAsync(service.RetryAsync(new(
            goals.Goal.Id,
            GoalWorkflowRetryRole.Reviewer))))[^1];

        Assert.Equal(GoalWorkflowRetryRole.Reviewer, failed.RetryRole);
        Assert.Equal(GoalWorkflowState.AwaitingAcceptance, recovered.State);
        Assert.Equal(1, recovered.ReviewCycle.Value);
        Assert.Equal([AgentRole.Lead, AgentRole.Implementer, AgentRole.Reviewer, AgentRole.Reviewer],
            agents.Requests.Select(request => request.Role));
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
        await CollectAsync(service.StartPlanningAsync(new(goals.Goal.Id)));
        goals.Approve();
        agents.Failures.Enqueue((AgentRole.Reviewer, "provider_unavailable", "Provider unavailable"));
        await CollectAsync(service.ResumeAsync(new(goals.Goal.Id)));
        agents.ReviewerOutputs.Enqueue(
            "{\"decision\":\"revise\",\"summary\":\"Add the boundary case.\"}");

        GoalWorkflowSnapshot reviewed = (await CollectAsync(service.RetryAsync(new(
            goals.Goal.Id,
            GoalWorkflowRetryRole.Reviewer,
            new("Focus on the missing boundary case.")))))[^1];

        Assert.Equal(GoalWorkflowState.Running, reviewed.State);
        Assert.True(reviewed.CanResume);
        Assert.Equal(AgentRole.Reviewer, agents.Requests[^1].Role);
        Assert.Equal(4, agents.Requests.Count);
        GoalWorkflowSnapshot completed = (await CollectAsync(
            service.ResumeAsync(new(goals.Goal.Id))))[^1];
        Assert.Equal(GoalWorkflowState.AwaitingAcceptance, completed.State);
        Assert.Equal(AgentRole.Implementer, agents.Requests[^2].Role);
        Assert.Contains("Add the boundary case", agents.Requests[^2].Task.Value,
            StringComparison.Ordinal);
        Assert.Equal(AgentRole.Reviewer, agents.Requests[^1].Role);
    }

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


}
