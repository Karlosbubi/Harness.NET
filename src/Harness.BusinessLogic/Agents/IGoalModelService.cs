using Harness.BusinessLogic.Goals;

namespace Harness.BusinessLogic.Agents;

public interface IGoalModelService
{
    ValueTask<GoalModelCatalog> DiscoverAsync(
        GoalId goalId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<GoalModelSelectionView>> GetSelectionsAsync(
        GoalId goalId,
        CancellationToken cancellationToken = default);

    ValueTask<GoalModelSelectionResult> SelectAsync(
        GoalModelSelectionRequest request,
        CancellationToken cancellationToken = default);
}
