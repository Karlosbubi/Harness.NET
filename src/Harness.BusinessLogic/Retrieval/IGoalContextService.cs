namespace Harness.BusinessLogic.Retrieval;

public interface IGoalContextService
{
    ValueTask<SemanticSearchResult> SearchAsync(
        GoalContextRequest request,
        CancellationToken cancellationToken = default);
}
