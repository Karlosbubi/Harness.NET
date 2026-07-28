using Harness.BusinessLogic.Goals;

namespace Harness.BusinessLogic.Agents;

public sealed record GoalModelSelectionView(
    GoalId GoalId,
    AgentRole Role,
    ModelProviderName Provider,
    AgentModel Model,
    ModelAccess Access,
    bool IsExplicit,
    DateTimeOffset? SelectedAt);
