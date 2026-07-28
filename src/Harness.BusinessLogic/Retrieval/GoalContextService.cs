using Harness.BusinessLogic.Goals;

namespace Harness.BusinessLogic.Retrieval;

internal sealed class GoalContextService(
    IGoalService goalService,
    ISemanticIndexService semanticIndexService) : IGoalContextService
{
    public async ValueTask<SemanticSearchResult> SearchAsync(
        GoalContextRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request?.GoalId is null || request.Query is null ||
            string.IsNullOrWhiteSpace(request.Query.Value) ||
            request.Query.Value.Length > 2_000 || request.MaximumMatches is null ||
            request.MaximumMatches.Value is < 1 or > 8)
        {
            return new(null, [], new(0, Cost: null), "invalid_goal_context_request",
                "A goal, query of 1-2000 characters, and 1-8 matches are required.");
        }

        GoalView? goal = await goalService.GetAsync(request.GoalId, cancellationToken);
        if (goal is null)
        {
            return new(null, [], new(0, Cost: null), "goal_missing",
                "The goal does not exist.");
        }

        return await semanticIndexService.SearchAsync(new(
            goal.WorkspaceId,
            request.Query.Value,
            request.MaximumMatches.Value,
            goal.Id.Value,
            SemanticPrivacyPolicy.NoCollectionAndZeroDataRetention), cancellationToken);
    }
}
