using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Retrieval;

namespace Harness.BusinessLogic.Tests.Retrieval;

public sealed class GoalContextServiceTests
{
    [Fact]
    public async Task Maps_goal_workspace_and_strict_privacy_into_bounded_search()
    {
        GoalView goal = Goal();
        CapturingSemanticIndexService index = new();
        GoalContextService service = new(new StubGoalService(goal), index);

        SemanticSearchResult result = await service.SearchAsync(new(
            goal.Id, new("repository architecture"), new(4)));

        Assert.Null(result.Error);
        SemanticSearchRequest request = Assert.IsType<SemanticSearchRequest>(index.Request);
        Assert.Equal(goal.WorkspaceId, request.WorkspaceId);
        Assert.Equal(goal.Id.Value, request.RemoteGoalId);
        Assert.Equal(4, request.MaximumResults);
        Assert.Equal(SemanticPrivacyPolicy.NoCollectionAndZeroDataRetention,
            request.PrivacyPolicy);
    }

    [Fact]
    public async Task Rejects_invalid_match_limit_before_search()
    {
        GoalView goal = Goal();
        CapturingSemanticIndexService index = new();
        GoalContextService service = new(new StubGoalService(goal), index);

        SemanticSearchResult result = await service.SearchAsync(new(
            goal.Id, new("query"), new(9)));

        Assert.Equal("invalid_goal_context_request", result.ErrorCode);
        Assert.Null(index.Request);
    }

    private static GoalView Goal()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-29T10:00:00Z");
        return new(new(Guid.NewGuid().ToString("N")), "workspace-1", "Goal", "Objective",
            new(2), RemoteBudget: null, GoalState.Draft, now, now);
    }

    private sealed class CapturingSemanticIndexService : ISemanticIndexService
    {
        internal SemanticSearchRequest? Request { get; private set; }

        public ValueTask<SemanticSearchResult> SearchAsync(
            SemanticSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return ValueTask.FromResult(new SemanticSearchResult(
                Partition: null, Matches: [], new(1, Cost: null),
                ErrorCode: null, Error: null));
        }

        public ValueTask<SemanticIndexStatusResult> GetStatusAsync(
            SemanticIndexRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<SemanticIndexResult> RebuildAsync(
            SemanticIndexRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubGoalService(GoalView goal) : IGoalService
    {
        public ValueTask<GoalView?> GetAsync(
            GoalId goalId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<GoalView?>(goalId == goal.Id ? goal : null);

        public ValueTask<GoalResult> CreateAsync(
            GoalCreateRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<GoalView>> ListAsync(
            string workspaceId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<GoalResult> UpdateSettingsAsync(
            GoalSettingsUpdateRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<PlanView?> GetCurrentPlanAsync(
            GoalId goalId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<PlanResult> ProposePlanAsync(
            PlanProposalRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<PlanResult> DecidePlanAsync(
            PlanDecisionRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
