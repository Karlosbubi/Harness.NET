using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Tools;
using Harness.BusinessLogic.Workflows;

namespace Harness.BusinessLogic.Tests.Workflows;

public sealed partial class GoalWorkflowServiceTests
{
    private static GoalWorkflowService CreateService(
        InMemoryGoalWorkflowStore store,
        IGoalService goals,
        IAgentRoleRunner agents,
        IToolEvidenceService? evidence = null,
        IWorkspaceMutationService? mutations = null) => new(
            store,
            store,
            goals,
            agents,
            evidence ?? new AdvancingToolEvidenceService(),
            new FixedTimeProvider(),
            mutations);

    [Fact]
    public async Task Final_validation_runs_build_and_test_once_after_all_tasks()
    {
        FakeGoalService goals = new();
        FakeAgentRunner agents = new();
        RecordingFinalValidationService validation = new();
        InMemoryGoalWorkflowStore store = new();
        GoalWorkflowService service = CreateService(
            store, goals, agents, mutations: validation);
        _ = await CollectAsync(service.StartPlanningAsync(new(goals.Goal.Id)));
        goals.Approve();

        GoalWorkflowSnapshot result = (await CollectAsync(
            service.ResumeAsync(new(goals.Goal.Id))))[^1];

        Assert.Equal(GoalWorkflowState.AwaitingAcceptance, result.State);
        Assert.All(result.Activities, activity => Assert.Equal(
            DateTimeOffset.Parse("2026-07-28T18:00:00Z"), activity.OccurredAt));
        Assert.Equal([DotNetOperation.Build, DotNetOperation.Test], validation.Operations);
        Assert.Equal([AgentRole.Lead, AgentRole.Implementer, AgentRole.Reviewer],
            agents.Requests.Select(request => request.Role));
    }

    [Fact]
    public async Task Failed_final_validation_gets_one_bounded_repair_and_revalidation()
    {
        FakeGoalService goals = new();
        FakeAgentRunner agents = new()
        {
            LeadOutput = """
                {"plan":"Implement and validate.","tasks":[{"title":"Implement","objective":"Implement the bounded change.","fileAreas":["src/","tests/Generated.Tests"],"acceptanceCriteria":["Tests pass."]}]}
                """,
        };
        RecordingFinalValidationService validation = new(failFirstTest: true);
        InMemoryGoalWorkflowStore store = new();
        GoalWorkflowService service = CreateService(
            store, goals, agents, mutations: validation);
        _ = await CollectAsync(service.StartPlanningAsync(new(goals.Goal.Id)));
        goals.Approve();

        GoalWorkflowSnapshot result = (await CollectAsync(
            service.ResumeAsync(new(goals.Goal.Id))))[^1];

        Assert.Equal(GoalWorkflowState.AwaitingAcceptance, result.State);
        Assert.Equal(
            [DotNetOperation.Build, DotNetOperation.Test,
                DotNetOperation.Build, DotNetOperation.Test],
            validation.Operations);
        Assert.Equal(
            [AgentRole.Lead, AgentRole.Implementer, AgentRole.Implementer,
                AgentRole.Reviewer],
            agents.Requests.Select(request => request.Role));
        Assert.Contains("DETERMINISTIC TEST FAILURE", agents.Requests[^2].Task.Value,
            StringComparison.Ordinal);
        Assert.Equal(["tests/Generated.Tests/UnitTest1.cs"],
            agents.Requests[^2].FileAreas?.Select(area => area.Value));
    }

    [Fact]
    public async Task Persistent_long_validation_failure_becomes_bounded_retryable_direction()
    {
        FakeGoalService goals = new();
        FakeAgentRunner agents = new();
        RecordingFinalValidationService validation = new(failEveryTest: true);
        InMemoryGoalWorkflowStore store = new();
        GoalWorkflowService service = CreateService(
            store, goals, agents, mutations: validation);
        _ = await CollectAsync(service.StartPlanningAsync(new(goals.Goal.Id)));
        goals.Approve();

        GoalWorkflowSnapshot result = (await CollectAsync(
            service.ResumeAsync(new(goals.Goal.Id))))[^1];

        Assert.Equal(GoalWorkflowState.NeedsDirection, result.State);
        Assert.Equal(GoalWorkflowRetryRole.Implementer, result.RetryRole);
        Assert.True(result.Activities[^1].Summary.Value.Length < 512);
    }

    [Fact]
    public async Task Verification_only_review_correction_can_be_reviewed_again()
    {
        FakeGoalService goals = new();
        FakeAgentRunner agents = new();
        agents.ReviewerOutputs.Enqueue(
            "{\"decision\":\"revise\",\"summary\":\"Run the missing validation.\"}");
        agents.ReviewerOutputs.Enqueue(
            "{\"decision\":\"accept\",\"summary\":\"Validation evidence is durable.\"}");
        InMemoryGoalWorkflowStore store = new();
        GoalWorkflowService service = CreateService(
            store, goals, agents, new CorrectionEvidenceService(addsVerification: true));
        _ = await CollectAsync(service.StartPlanningAsync(new(goals.Goal.Id)));
        goals.Approve();

        GoalWorkflowSnapshot result = (await CollectAsync(
            service.ResumeAsync(new(goals.Goal.Id))))[^1];

        Assert.Equal(GoalWorkflowState.AwaitingAcceptance, result.State);
        Assert.Equal(2, result.ReviewCycle.Value);
        Assert.Equal(
            [AgentRole.Lead, AgentRole.Implementer, AgentRole.Reviewer,
                AgentRole.Implementer, AgentRole.Reviewer],
            agents.Requests.Select(request => request.Role));
    }

    [Fact]
    public async Task Review_correction_is_deterministically_revalidated_before_next_review()
    {
        FakeGoalService goals = new();
        FakeAgentRunner agents = new();
        agents.ReviewerOutputs.Enqueue(
            "{\"decision\":\"revise\",\"summary\":\"Correct the implementation.\"}");
        agents.ReviewerOutputs.Enqueue(
            "{\"decision\":\"accept\",\"summary\":\"Correction accepted.\"}");
        RecordingFinalValidationService validation = new();
        InMemoryGoalWorkflowStore store = new();
        GoalWorkflowService service = CreateService(
            store, goals, agents,
            new CorrectionEvidenceService(addsVerification: true), validation);
        _ = await CollectAsync(service.StartPlanningAsync(new(goals.Goal.Id)));
        goals.Approve();

        GoalWorkflowSnapshot result = (await CollectAsync(
            service.ResumeAsync(new(goals.Goal.Id))))[^1];

        Assert.Equal(GoalWorkflowState.AwaitingAcceptance, result.State);
        Assert.Equal(
            [DotNetOperation.Build, DotNetOperation.Test,
                DotNetOperation.Build, DotNetOperation.Test],
            validation.Operations);
    }

    [Fact]
    public async Task Review_correction_without_new_evidence_has_an_implementer_retry_role()
    {
        FakeGoalService goals = new();
        FakeAgentRunner agents = new();
        agents.ReviewerOutputs.Enqueue(
            "{\"decision\":\"revise\",\"summary\":\"Run the missing validation.\"}");
        InMemoryGoalWorkflowStore store = new();
        GoalWorkflowService service = CreateService(
            store, goals, agents, new CorrectionEvidenceService(
                addsVerification: false, retryAddsMutation: true));
        _ = await CollectAsync(service.StartPlanningAsync(new(goals.Goal.Id)));
        goals.Approve();

        GoalWorkflowSnapshot result = (await CollectAsync(
            service.ResumeAsync(new(goals.Goal.Id))))[^1];

        Assert.Equal(GoalWorkflowState.NeedsDirection, result.State);
        Assert.Equal(GoalWorkflowRetryRole.Implementer, result.RetryRole);
        Assert.Contains("mutation or verification evidence",
            result.Activities[^1].Summary.Value, StringComparison.Ordinal);

        GoalWorkflowSnapshot retried = (await CollectAsync(service.RetryAsync(new(
            goals.Goal.Id,
            GoalWorkflowRetryRole.Implementer,
            new("Repair the cited validation failure.")))))[^1];
        Assert.Equal(GoalWorkflowState.Running, retried.State);
        Assert.Equal(GoalWorkflowCheckpointKind.ImplementationProduced,
            retried.Activities[^1].Kind);

        GoalWorkflowSnapshot completed = (await CollectAsync(
            service.ResumeAsync(new(goals.Goal.Id))))[^1];
        Assert.Equal(GoalWorkflowState.AwaitingAcceptance, completed.State);
    }

    private sealed class CorrectionEvidenceService(
        bool addsVerification,
        bool retryAddsMutation = false)
        : IToolEvidenceService
    {
        private int reads;

        public ValueTask<ToolEvidenceSnapshot> ListAsync(
            string goalId,
            CancellationToken cancellationToken = default)
        {
            int currentRead = ++reads;
            List<ToolEvidenceView> items = [];
            if (currentRead >= 2)
            {
                items.Add(Evidence(goalId, "mutation", ToolKind.FileEdit));
            }
            if (addsVerification && currentRead >= 4)
            {
                items.Add(Evidence(goalId, "verification", ToolKind.Test));
            }
            if (retryAddsMutation && currentRead >= 7)
            {
                items.Add(Evidence(goalId, "retry-mutation", ToolKind.FileEdit));
            }

            return ValueTask.FromResult(new ToolEvidenceSnapshot(items, null, null));
        }

        private static ToolEvidenceView Evidence(
            string goalId,
            string id,
            ToolKind tool) => new(
            new(id),
            goalId,
            new($"correlation-{id}"),
            tool,
            "{}",
            ToolEvidenceState.Succeeded,
            "{}",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
    }

    private sealed class RecordingFinalValidationService(
        bool failFirstTest = false,
        bool failEveryTest = false)
        : IWorkspaceMutationService
    {
        private bool testFailed;
        internal List<DotNetOperation> Operations { get; } = [];

        public ValueTask<FileEditView> ApplyFileEditAsync(
            FileEditRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<DotNetOperationView> RunDotNetAsync(
            DotNetOperationRequest request,
            CancellationToken cancellationToken = default)
        {
            Operations.Add(request.Operation);
            bool fails = request.Operation is DotNetOperation.Test &&
                (failEveryTest || failFirstTest && !testFailed);
            testFailed |= fails;
            return ValueTask.FromResult(new DotNetOperationView(
                request.GoalId,
                request.CorrelationId,
                request.Operation,
                "Harness.slnx",
                fails ? 1 : 0,
                fails
                    ? new string('x', failEveryTest ? 20_000 : 0) +
                      " Failed at tests/Generated.Tests/UnitTest1.cs:line 20 and " +
                      "tests/Generated.Tests/UnitTest1.cs:line 30 after " +
                      "src/Engine.cs:line 10."
                    : "ok",
                string.Empty,
                false,
                false,
                false,
                1,
                null,
                null));
        }
    }
}
